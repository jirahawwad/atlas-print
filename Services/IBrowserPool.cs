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
	/// Acquires a new <see cref="IPage"/> from a fresh, isolated, page-owned browser context.
	/// Caller is responsible for closing the page after use.
	/// Concurrency is internally gated — this call may await until a slot is free.
	/// </summary>
	public Task<IPage> AcquirePageAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Releases the concurrency slot held for a page acquired via <see cref="AcquirePageAsync"/>.
	/// Must be called exactly once per successful acquisition, after the page has been closed.
	/// </summary>
	public void ReleasePage();
}
