using Atlas.Print.Domain;

using Microsoft.Playwright;

namespace Atlas.Print.Services;

/// <summary>
/// Renders HTML to PDF using Playwright/Chromium. Acquires a page from the shared
/// <see cref="IBrowserPool"/> per request, lays out and captures the given HTML at
/// the target page size, and returns the result as a Base64-encoded PDF.
/// </summary>
public sealed class PlaywrightPrintRenderer(
	IBrowserPool browserPool,
	ILogger<PlaywrightPrintRenderer> logger)
{
	private readonly IBrowserPool _browserPool = browserPool;
	private readonly ILogger<PlaywrightPrintRenderer> _logger = logger;

	/// <summary>
	/// Renders the given <see cref="PrintRequest"/> to a Base64-encoded PDF.
	/// </summary>
	/// <remarks>
	/// The page's viewport is sized to the target print format and Chromium's screen
	/// (not print) media is explicitly emulated before capture — this makes layout-
	/// sensitive CSS (percentage widths, viewport units, table auto-layout) behave the
	/// same way it would in a normal browser tab, rather than however Chromium's
	/// internal print-media layout pass would compute it.
	/// </remarks>
	/// <exception cref="OperationCanceledException">
	/// Thrown if <paramref name="cancellationToken"/> is cancelled before or during rendering.
	/// </exception>
	public async Task<string> RenderAsync(
		PrintRequest request,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		IPage? page = null;

		try
		{
			page = await _browserPool.AcquirePageAsync();

			cancellationToken.ThrowIfCancellationRequested();

			bool isLandscape = request.PrintFormat.Equals("LANDSCAPE", StringComparison.OrdinalIgnoreCase);
			int targetWidth = isLandscape ? 1056 : 816;
			int targetHeight = isLandscape ? 816 : 1056;

			// Size the viewport to the target page dimensions before content loads,
			// so percentage/viewport-relative CSS resolves against the same
			// dimensions the PDF will actually be captured at.
			await page.SetViewportSizeAsync(targetWidth, targetHeight);

			await page.SetContentAsync(request.HtmlPayload, new PageSetContentOptions
			{
				WaitUntil = WaitUntilState.NetworkIdle
			});

			cancellationToken.ThrowIfCancellationRequested();

			// Force screen-media layout instead of Chromium's default print-media
			// layout pass for page.PdfAsync() — gives more predictable, "what you'd
			// see in a browser tab" sizing for tables/percentage widths.
			await page.EmulateMediaAsync(new PageEmulateMediaOptions
			{
				Media = Media.Screen
			});

			PagePdfOptions options = new()
			{
				Format = "Letter",
				Landscape = isLandscape,
				Scale = 1.0f,
				PrintBackground = true,
				// False so Playwright always honors our explicit Format/Margin values
				// below rather than deferring to any (possibly absent or conflicting)
				// CSS @page rule in the rendered HTML — avoids margin/clipping
				// mismatches when the HTML doesn't declare @page explicitly.
				PreferCSSPageSize = false,
				DisplayHeaderFooter = true,
				HeaderTemplate = request.HeaderHtml ?? "<span/>",
				FooterTemplate = request.FooterHtml ?? "<span/>",
				Margin = new Margin
				{
					Top = request.MarginTop,
					Bottom = request.MarginBottom,
					Left = request.MarginLeft,
					Right = request.MarginRight
				}
			};

			byte[] pdfBytes = await page.PdfAsync(options);
			string base64 = Convert.ToBase64String(pdfBytes);

			_logger.LogInformation(
				"PlaywrightPrintRenderer|method:{Method}|pdfBytes:{PdfBytes}",
				nameof(RenderAsync),
				pdfBytes.Length);

			return base64;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"PlaywrightPrintRenderer|method:{Method}|reason:{Reason}",
				nameof(RenderAsync),
				"RenderFailed");

			throw;
		}
		finally
		{
			if (page is not null)
			{
				await page.CloseAsync();
			}
		}
	}
}
