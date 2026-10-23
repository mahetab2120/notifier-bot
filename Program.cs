using Microsoft.Extensions.Options;
using Notifyer;
using Notifyer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<WatcherOptions>()
    .Bind(builder.Configuration.GetSection(WatcherOptions.SectionName))
    .Validate(options => options.PollIntervalSeconds >= 15,
        "Watcher:PollIntervalSeconds must be at least 15 seconds.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.StateFile),
        "Watcher:StateFile must be configured.")
    .Validate(options => options.Products.Count > 0,
        "Configure at least one product under Watcher:Products.")
    .Validate(options => options.Products.Select(product => product.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == options.Products.Count,
        "Product names must be unique because they identify saved availability state.")
    .Validate(options => options.Products.All(product =>
            !string.IsNullOrWhiteSpace(product.Name) &&
            Uri.TryCreate(product.Url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(product.AvailableSelector)),
        "Every product needs a name, an absolute HTTP(S) URL, and an AvailableSelector.")
    .ValidateOnStart();

builder.Services
    .AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection(TelegramOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BotToken),
        "Configure Telegram:BotToken using a secret store or environment variable.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ChatId),
        "Configure Telegram:ChatId using a secret store or environment variable.")
    .ValidateOnStart();

builder.Services.AddSingleton(new HttpClient());
builder.Services.AddTransient<TelegramNotifier>();
builder.Services.AddHostedService<StockWatcherService>();

var app = builder.Build();

app.MapGet("/health", (IOptions<WatcherOptions> options) => Results.Ok(new
{
    status = "healthy",
    watching = options.Value.Products.Count
}));

app.Run();
