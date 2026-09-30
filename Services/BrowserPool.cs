using System.Diagnostics;

using Microsoft.Playwright;

namespace Atlas.Print.Services;

/// <summary>
/// Manages a single shared, headless Chromium browser instance for the lifetime of
/// the application. Started once at host startup and disposed at shutdown; each
/// print request acquires its own isolated <see cref="IBrowserContext"/>/<see cref="IPage"/>
/// from the running browser rather than launching a new browser process per request.
/// </summary>
public sealed class BrowserPool(ILogger<BrowserPool> logger, IConfiguration configuration) : IBrowserPool, IHostedService, IAsyncDisposable
{
	private static readonly string[] _chromiumArgs =
	[
		"--no-sandbox",
		"--disable-setuid-sandbox",
		"--disable-dev-shm-usage",
		"--disable-gpu"
	];

	private readonly ILogger<BrowserPool> _logger = logger;
	private const int DefaultMaxConcurrentPages = 4;

	// Bounds how many Chromium pages can be open at once — mirrors Atlas.Report.Print.
	private readonly SemaphoreSlim _pageSemaphore = CreatePageSemaphore(configuration);

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
			"BrowserPool|method:{Method}|browserVersion:{BrowserVersion}|connected:{Connected}|maxConcurrentPages:{MaxConcurrentPages}",
			nameof(StartAsync),
			_browser.Version,
			_browser.IsConnected,
			_pageSemaphore.CurrentCount);
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
	public async Task<IPage> AcquirePageAsync(CancellationToken cancellationToken = default)
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

		await _pageSemaphore.WaitAsync(cancellationToken);

		long waitedMs = sw.ElapsedMilliseconds;

		try
		{
			int contextsBefore = _browser.Contexts.Count;

			// Page-owned context: closes automatically when the page closes.
			// NewContextAsync + context.NewPageAsync leaked one context per request,
			// because callers only close the page (per the IBrowserPool contract).
			// DeviceScaleFactor intentionally left unset (defaults to 1): an explicit
			// scale factor here was forcing Chromium's PDF rasterizer to round hairline
			// (1px) borders up, making them render visibly thicker than declared.
			IPage page = await _browser.NewPageAsync();

			_logger.LogDebug(
				"BrowserPool|method:{Method}|contextsBefore:{ContextsBefore}|contextsAfter:{ContextsAfter}|waited:{Waited}ms|freeSlots:{FreeSlots}|elapsed:{Elapsed}ms",
				nameof(AcquirePageAsync),
				contextsBefore,
				_browser.Contexts.Count,
				waitedMs,
				_pageSemaphore.CurrentCount,
				sw.ElapsedMilliseconds);

			return page;
		}
		catch
		{
			// Release only on failure to acquire — on success the caller owns the
			// slot and returns it via ReleasePage after closing the page.
			_pageSemaphore.Release();
			throw;
		}
	}

	/// <summary>
	/// Releases a concurrency slot after the caller has closed its page.
	/// Must be called exactly once per successful <see cref="AcquirePageAsync"/> call.
	/// </summary>
	public void ReleasePage()
	{
		_pageSemaphore.Release();
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

	private static SemaphoreSlim CreatePageSemaphore(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		int maxConcurrentPages = configuration.GetValue<int?>("Playwright:MaxConcurrentPages")
			?? DefaultMaxConcurrentPages;

		if (maxConcurrentPages < 1)
		{
			throw new InvalidOperationException(
				$"Playwright:MaxConcurrentPages must be at least 1 (was {maxConcurrentPages}).");
		}

		return new SemaphoreSlim(maxConcurrentPages, maxConcurrentPages);
	}

	private void OnBrowserDisconnected(object? sender, IBrowser browser)
	{
		_logger.LogCritical(
			"BrowserPool|method:{Method}|reason:{Reason}",
			nameof(OnBrowserDisconnected),
			"BrowserDisconnected");
	}

}
