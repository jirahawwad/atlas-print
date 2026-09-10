using Microsoft.Playwright;

namespace Atlas.Print.Services;

/// <summary>
/// Manages a single shared, headless Chromium browser instance for the lifetime of
/// the application. Started once at host startup and disposed at shutdown; each
/// print request acquires its own isolated <see cref="IBrowserContext"/>/<see cref="IPage"/>
/// from the running browser rather than launching a new browser process per request.
/// </summary>
public sealed class BrowserPool(ILogger<BrowserPool> logger) : IBrowserPool, IHostedService, IAsyncDisposable
{
	private static readonly string[] _chromiumArgs =
	[
		"--no-sandbox",
		"--disable-setuid-sandbox",
		"--disable-dev-shm-usage",
		"--disable-gpu"
	];

	private readonly ILogger<BrowserPool> _logger = logger;

	private IPlaywright? _playwright;
	private IBrowser? _browser;

	/// <summary>
	/// Starts Playwright and launches the shared headless Chromium instance.
	/// Called once by the host at application startup.
	/// </summary>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		_playwright = await Playwright.CreateAsync();

		System.Reflection.Assembly playwrightAssembly = typeof(IPlaywright).Assembly;
		_logger.LogInformation(
			"BrowserPool|method:{Method}|playwrightAssemblyVersion:{Version}|chromiumVersion:{ChromiumVersion}",
			nameof(StartAsync),
			playwrightAssembly.GetName().Version,
			_playwright.Chromium.Name);

		_browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
		{
			Headless = true,
			Args = _chromiumArgs
		});
	}

	/// <summary>
	/// No-op — the shared browser is torn down via <see cref="DisposeAsync"/>, not here.
	/// </summary>
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// Creates a new, isolated <see cref="IBrowserContext"/> on the shared browser and
	/// returns a fresh <see cref="IPage"/> from it. Each caller gets its own context —
	/// no cookies, cache, or state is shared between concurrent print requests.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called before <see cref="StartAsync"/> has completed.
	/// </exception>
	public async Task<Microsoft.Playwright.IPage> AcquirePageAsync()
	{
		if (_browser is null)
		{
			throw new InvalidOperationException("BrowserPool has not been initialised.");
		}

		// DeviceScaleFactor intentionally left unset (defaults to 1): an explicit
		// scale factor here was forcing Chromium's PDF rasterizer to round hairline
		// (1px) borders up, making them render visibly thicker than declared.
		IBrowserContext context = await _browser.NewContextAsync();

		return await context.NewPageAsync();
	}

	/// <summary>
	/// Disposes the shared Chromium browser and the Playwright driver.
	/// Called once by the host at application shutdown.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_browser is not null)
		{
			await _browser.DisposeAsync();
		}

		_playwright?.Dispose();
	}
}
