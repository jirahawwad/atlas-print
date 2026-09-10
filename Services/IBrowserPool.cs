using Microsoft.Playwright;

namespace Atlas.Print.Services;

/// <summary>
/// Manages a shared Playwright browser instance.
/// The browser is created once at startup and reused across requests.
/// Each request gets a fresh <see cref="IPage"/> from its own isolated
/// <see cref="IBrowserContext"/> — no state (cookies, cache, etc.) is shared
/// between concurrent callers.
/// </summary>
public interface IBrowserPool
{
	/// <summary>
	/// Acquires a new <see cref="IPage"/> from a fresh, isolated browser context.
	/// Caller is responsible for closing the page after use.
	/// </summary>
	public Task<IPage> AcquirePageAsync();
}
