using Atlas.Print.Domain;

using Microsoft.Playwright;

namespace Atlas.Print.Services;

/// <summary>
/// Renders HTML to PDF using Playwright/Chromium.
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

			await page.SetContentAsync(request.HtmlPayload, new PageSetContentOptions
			{
				WaitUntil = WaitUntilState.NetworkIdle
			});

			cancellationToken.ThrowIfCancellationRequested();

			bool isLandscape = request.PrintFormat.Equals("LANDSCAPE", StringComparison.OrdinalIgnoreCase);

			PagePdfOptions options = new()
			{
				Format = "Letter",
				Landscape = isLandscape,
				PrintBackground = true,
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
