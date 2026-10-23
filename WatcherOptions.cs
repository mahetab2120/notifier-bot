namespace Notifyer;

public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    public int PollIntervalSeconds { get; set; } = 60;
    public bool NotifyOnStartup { get; set; } = true;
    public string StateFile { get; set; } = "App_Data/stock-state.json";
    public List<ProductOptions> Products { get; set; } = [];
}

public sealed class ProductOptions
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string AvailableSelector { get; set; } = "";
    public string? UnavailableSelector { get; set; }
}

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
}
