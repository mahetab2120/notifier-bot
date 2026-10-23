# notifier-bot

Notifyer is an ASP.NET Core 8 application that checks product pages with headless Chromium and sends a Telegram message when a product becomes available. It runs as a hosted background service inside the web app, so it can be published to IIS.

## Configure products and Telegram

Edit `Watcher:Products` in `appsettings.json`. Each product needs:

- `Name`: unique label used in the alert and saved state.
- `Url`: absolute product page URL.
- `AvailableSelector`: CSS selector that is present and visible when the product can be purchased.
- `UnavailableSelector` (optional): visible CSS selector that means unavailable. If present, it takes precedence over the available selector.

Selectors are site-specific. Inspect the rendered product page and choose stable selectors for its add-to-cart and unavailable controls. The watcher does not infer availability from page text.

Create a Telegram bot with [@BotFather](https://t.me/BotFather), start a private chat with it, and set the bot token and destination chat ID as environment variables. For a group or channel, add the bot with permission to post and use that destination's chat ID.

```powershell
$env:Telegram__BotToken = "your-bot-token"
$env:Telegram__ChatId = "your-chat-id"
```

Do not commit bot tokens to source control. Configure these values in the IIS application environment or a protected secret store in production.

`Watcher:PollIntervalSeconds` defaults to 60 and cannot be less than 15. `Watcher:NotifyOnStartup` defaults to `true` and sends a Telegram confirmation after Chromium starts successfully; set it to `false` to disable that message. Availability transitions are stored in `Watcher:StateFile`; the first successful check sends an alert if the product is already available. Ensure the IIS application-pool identity can write to the state file's directory.

## Run locally

Install the .NET 8 SDK, configure Telegram credentials, and build/install the Playwright Chromium browser. On Windows, run in PowerShell:

```powershell
dotnet restore
dotnet build
.\bin\Debug\net8.0\playwright.ps1 install chromium
dotnet run
```

On Linux, install PowerShell (`pwsh`) first, then run:

```bash
dotnet restore
dotnet build
pwsh bin/Debug/net8.0/playwright.ps1 install --with-deps chromium
dotnet run
```

The browser must be installed for the same OS, architecture, and account that runs the app. If Chromium is already installed but still fails to launch, install the OS libraries it requires (`install-deps chromium` on supported Linux distributions) and check the application logs for the original Playwright error.

The health endpoint is available at `/health`. It confirms that the web host is running and reports the configured watch count; it does not guarantee that product pages or Telegram are reachable.

## Publish to IIS

1. Install the .NET 8 Hosting Bundle on the IIS server.
2. Publish the app, for example: `dotnet publish -c Release -o .\publish`.
3. Install Chromium using the generated Playwright script: `.\publish\playwright.ps1 install chromium`. Run the command in PowerShell under the IIS application-pool identity, or configure a shared `PLAYWRIGHT_BROWSERS_PATH` for both the install process and the app pool. The app-pool identity must be able to read and execute the browser installation.
4. Configure an IIS site/application with the published directory and an application pool using **No Managed Code**. Set `Telegram__BotToken` and `Telegram__ChatId` in the IIS application environment; grant the app-pool identity write access to `App_Data`.
5. Enable IIS **Always Running** application-pool start mode and site **Preload Enabled**, and set the idle timeout appropriately. IIS recycling or shutdown pauses the watcher; configure recycling for your operational needs.

The app validates its product list and Telegram credentials at startup. A Chromium launch failure is reported with an installation hint and stops the hosted watcher; resolve the browser installation or OS dependency error in the logs before starting the app again. Check the ASP.NET Core application logs for configuration errors and per-product navigation or Telegram delivery failures.
