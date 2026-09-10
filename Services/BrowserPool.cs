using Microsoft.Playwright;

namespace Atlas.Print.Services;

public sealed class BrowserPool : IBrowserPool, IHostedService, IAsyncDisposable
{
	private static readonly string[] _chromiumArgs =
	[
		"--no-sandbox",
		"--disable-setuid-sandbox",
		"--disable-dev-shm-usage",
		"--disable-gpu"
	];

	private IPlaywright? _playwright;
	private IBrowser? _browser;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		_playwright = await Playwright.CreateAsync();
		_browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
		{
			Headless = true,
			Args = _chromiumArgs
		});
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task<Microsoft.Playwright.IPage> AcquirePageAsync()
	{
		if (_browser is null)
		{
			throw new InvalidOperationException("BrowserPool has not been initialised.");
		}

		// CHANGED: Removed DeviceScaleFactor completely to stop Chromium from forcing 
		// sub-pixel rounding translation math down to the vector generation layer.
		IBrowserContext context = await _browser.NewContextAsync();

		return await context.NewPageAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (_browser is not null)
		{
			await _browser.DisposeAsync();
		}

		_playwright?.Dispose();
	}
}
