using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Notifyer.Services;

public sealed class StockWatcherService(
    IOptions<WatcherOptions> options,
    TelegramNotifier telegramNotifier,
    IWebHostEnvironment environment,
    ILogger<StockWatcherService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly WatcherOptions _options = options.Value;
    private readonly string _statePath = Path.GetFullPath(options.Value.StateFile, environment.ContentRootPath);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var states = await LoadStatesAsync(stoppingToken);
        using var playwright = await Playwright.CreateAsync();
        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        }
        catch (PlaywrightException exception)
        {
            throw new InvalidOperationException(
                "Playwright could not launch Chromium. Install the Playwright Chromium browser and its system dependencies for the same account that runs Notifyer. See the Playwright setup steps in README.md.",
                exception);
        }

        await using (browser)
        {
            await RunWatcherAsync(browser, states, stoppingToken);
        }
    }

    private async Task RunWatcherAsync(
        IBrowser browser,
        Dictionary<string, bool> states,
        CancellationToken stoppingToken)
    {
        if (_options.NotifyOnStartup)
        {
            await telegramNotifier.SendServiceStartedAsync(_options.Products.Count, stoppingToken);
        }

        logger.LogInformation("Stock watcher started for {ProductCount} products.", _options.Products.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var product in _options.Products)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var available = await CheckAvailabilityAsync(browser, product, stoppingToken);
                    var wasAvailable = states.TryGetValue(product.Name, out var previous) && previous;

                    if (available && !wasAvailable)
                    {
                        await telegramNotifier.SendStockAlertAsync(product.Name, product.Url, stoppingToken);
                        logger.LogInformation("Stock alert sent for {ProductName}.", product.Name);
                    }

                    if (!states.TryGetValue(product.Name, out previous) || previous != available)
                    {
                        states[product.Name] = available;
                        await SaveStatesAsync(states, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Unable to check or alert for product {ProductName}.", product.Name);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<bool> CheckAvailabilityAsync(
        IBrowser browser,
        ProductOptions product,
        CancellationToken cancellationToken)
    {
        var page = await browser.NewPageAsync();
        try
        {
            await page.GotoAsync(product.Url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(product.UnavailableSelector) &&
                await HasVisibleElementAsync(page, product.UnavailableSelector))
            {
                return false;
            }

            return await HasVisibleElementAsync(page, product.AvailableSelector);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task<bool> HasVisibleElementAsync(IPage page, string selector)
    {
        var locator = page.Locator(selector).First;
        return await locator.CountAsync() > 0 && await locator.IsVisibleAsync();
    }

    private async Task<Dictionary<string, bool>> LoadStatesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(_statePath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, bool>>(
                   stream, StateJsonOptions, cancellationToken)
               ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveStatesAsync(Dictionary<string, bool> states, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_statePath)
                        ?? throw new InvalidOperationException("The state file must have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_statePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, states, StateJsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }
}
