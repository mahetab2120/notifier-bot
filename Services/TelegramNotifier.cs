using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Notifyer.Services;

public sealed class TelegramNotifier(
    HttpClient httpClient,
    IOptions<TelegramOptions> options,
    ILogger<TelegramNotifier> logger)
{
    public async Task SendStockAlertAsync(string productName, string productUrl, CancellationToken cancellationToken)
    {
        await SendMessageAsync($"In stock: {productName}\n{productUrl}", cancellationToken);
    }

    public async Task SendServiceStartedAsync(int productCount, CancellationToken cancellationToken)
    {
        await SendMessageAsync(
            $"✅ Notifyer is online and watching {productCount} product(s).\nStarted: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            cancellationToken);
    }

    private async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.telegram.org/bot{options.Value.BotToken}/sendMessage";
        using var response = await httpClient.PostAsJsonAsync(endpoint, new
        {
            chat_id = options.Value.ChatId,
            text = message,
            disable_web_page_preview = false
        }, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var description = document.RootElement.TryGetProperty("description", out var error)
                ? error.GetString()
                : "Telegram API returned an unsuccessful response.";
            logger.LogError("Telegram rejected a notification: {TelegramError}", description);
            throw new HttpRequestException($"Telegram rejected the notification: {description}");
        }
    }
}
