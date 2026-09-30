using System.Diagnostics;

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

		_browser.Disconnected += OnBrowserDisconnected;

		_logger.LogInformation(
			"BrowserPool|method:{Method}|browserVersion:{BrowserVersion}|connected:{Connected}",
			nameof(StartAsync),
			_browser.Version,
			_browser.IsConnected);
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

		if (!_browser.IsConnected)
		{
			_logger.LogError(
				"BrowserPool|method:{Method}|reason:{Reason}",
				nameof(AcquirePageAsync),
				"BrowserNotConnected");
		}

		Stopwatch sw = Stopwatch.StartNew();
		int contextsBefore = _browser.Contexts.Count;

		// DeviceScaleFactor intentionally left unset (defaults to 1): an explicit
		// scale factor here was forcing Chromium's PDF rasterizer to round hairline
		// (1px) borders up, making them render visibly thicker than declared.
		IPage page = await _browser.NewPageAsync();

		_logger.LogDebug(
			"BrowserPool|method:{Method}|contextsBefore:{ContextsBefore}|contextsAfter:{ContextsAfter}|elapsed:{Elapsed}ms",
			nameof(AcquirePageAsync),
			contextsBefore,
			_browser.Contexts.Count,
			sw.ElapsedMilliseconds);

		return page;
	}

	/// <summary>
	/// Disposes the shared Chromium browser and the Playwright driver.
	/// Called once by the host at application shutdown.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_browser is not null)
		{
			// Unsubscribe first: an intentional shutdown also raises Disconnected,
			// which would otherwise log a false Critical on every clean stop.
			_browser.Disconnected -= OnBrowserDisconnected;
			await _browser.DisposeAsync();
		}

		_playwright?.Dispose();
	}

	private void OnBrowserDisconnected(object? sender, IBrowser browser)
	{
		_logger.LogCritical(
			"BrowserPool|method:{Method}|reason:{Reason}",
			nameof(OnBrowserDisconnected),
			"BrowserDisconnected");
	}
}
