using System.Diagnostics;

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

		string renderId = Guid.NewGuid().ToString("N")[..8];
		Stopwatch total = Stopwatch.StartNew();
		Stopwatch step = new();

		LogRequestReceived(renderId, request);

		IPage? page = null;

		try
		{
			step.Restart();
			page = await _browserPool.AcquirePageAsync(cancellationToken);
			AttachPageDiagnostics(page, renderId);
			LogStep(renderId, "AcquirePage", step);

			cancellationToken.ThrowIfCancellationRequested();

			bool isLandscape = request.PrintFormat.Equals("LANDSCAPE", StringComparison.OrdinalIgnoreCase);
			int targetWidth = isLandscape ? 1056 : 816;
			int targetHeight = isLandscape ? 816 : 1056;

			// Size the viewport to the target page dimensions before content loads,
			// so percentage/viewport-relative CSS resolves against the same
			// dimensions the PDF will actually be captured at.
			step.Restart();
			await page.SetViewportSizeAsync(targetWidth, targetHeight);
			LogStep(renderId, "SetViewport", step);

			step.Restart();
			await page.SetContentAsync(request.HtmlPayload, new PageSetContentOptions
			{
				WaitUntil = WaitUntilState.NetworkIdle
			});
			LogStep(renderId, "SetContent", step);

			cancellationToken.ThrowIfCancellationRequested();

			// Force screen-media layout instead of Chromium's default print-media
			// layout pass for page.PdfAsync() — gives more predictable, "what you'd
			// see in a browser tab" sizing for tables/percentage widths.
			step.Restart();
			await page.EmulateMediaAsync(new PageEmulateMediaOptions
			{
				Media = Media.Screen
			});
			LogStep(renderId, "EmulateMedia", step);

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

			LogPageState(renderId, "PrePrint", page, LogLevel.Debug);

			step.Restart();
			byte[] pdfBytes = await page.PdfAsync(options);
			LogStep(renderId, "Pdf", step);

			string base64 = Convert.ToBase64String(pdfBytes);

			_logger.LogInformation(
				"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|pdfBytes:{PdfBytes}|elapsed:{Elapsed}ms",
				nameof(RenderAsync),
				renderId,
				pdfBytes.Length,
				total.ElapsedMilliseconds);

			return base64;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|reason:{Reason}|elapsed:{Elapsed}ms",
				nameof(RenderAsync),
				renderId,
				"RenderFailed",
				total.ElapsedMilliseconds);

			if (page is not null)
			{
				LogPageState(renderId, "Failed", page, LogLevel.Error);
			}

			throw;
		}
		finally
		{
			if (page is not null)
			{
				await ReleasePageAsync(renderId, page);
			}
		}
	}

	private async Task ReleasePageAsync(string renderId, IPage page)
	{
		try
		{
			await page.CloseAsync();
			LogPageState(renderId, "Released", page, LogLevel.Debug);
		}
		catch (PlaywrightException ex)
		{
			// Swallowed deliberately: a close failure (e.g. dead browser) must not
			// replace the original render exception propagating from RenderAsync.
			_logger.LogWarning(
				ex,
				"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|reason:{Reason}",
				nameof(ReleasePageAsync),
				renderId,
				"PageCloseFailed");
		}
		finally
		{
			_browserPool.ReleasePage();
		}
	}

	private void LogRequestReceived(string renderId, PrintRequest request)
	{
		_logger.LogDebug(
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|printFormat:{PrintFormat}|htmlLength:{HtmlLength}|headerLength:{HeaderLength}|footerLength:{FooterLength}|margins:{Top}/{Bottom}/{Left}/{Right}",
			nameof(RenderAsync),
			renderId,
			request.PrintFormat,
			request.HtmlPayload.Length,
			request.HeaderHtml?.Length ?? 0,
			request.FooterHtml?.Length ?? 0,
			request.MarginTop,
			request.MarginBottom,
			request.MarginLeft,
			request.MarginRight);
	}

	private void LogStep(string renderId, string stepName, Stopwatch step)
	{
		_logger.LogDebug(
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|step:{Step}|elapsed:{Elapsed}ms",
			nameof(RenderAsync),
			renderId,
			stepName,
			step.ElapsedMilliseconds);
	}

	private void LogPageState(string renderId, string stage, IPage page, LogLevel level)
	{
		IBrowser? browser = page.Context.Browser;

		_logger.Log(
			level,
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|stage:{Stage}|url:{Url}|isClosed:{IsClosed}|browserConnected:{BrowserConnected}|openContexts:{OpenContexts}",
			nameof(RenderAsync),
			renderId,
			stage,
			page.Url,
			page.IsClosed,
			browser?.IsConnected,
			browser?.Contexts.Count);
	}

	private void AttachPageDiagnostics(IPage page, string renderId)
	{
		page.Crash += (_, _) => OnPageCrashed(renderId);
		page.FrameNavigated += (_, frame) => OnFrameNavigated(renderId, frame);
		page.PageError += (_, message) => OnPageScriptError(renderId, message);
		page.RequestFailed += (_, failedRequest) => OnRequestFailed(renderId, failedRequest);
	}

	private void OnPageCrashed(string renderId)
	{
		_logger.LogError(
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|reason:{Reason}",
			nameof(OnPageCrashed),
			renderId,
			"PageCrashed");
	}

	private void OnFrameNavigated(string renderId, IFrame frame)
	{
		if (frame.ParentFrame is not null)
		{
			return;
		}

		LogLevel level = frame.Url == "about:blank" ? LogLevel.Debug : LogLevel.Warning;

		_logger.Log(
			level,
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|mainFrameUrl:{Url}",
			nameof(OnFrameNavigated),
			renderId,
			frame.Url);
	}

	private void OnPageScriptError(string renderId, string message)
	{
		_logger.LogWarning(
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|reason:{Reason}|message:{Message}",
			nameof(OnPageScriptError),
			renderId,
			"PageScriptError",
			message);
	}

	private void OnRequestFailed(string renderId, IRequest failedRequest)
	{
		string url = failedRequest.Url.Length > 200 ? failedRequest.Url[..200] : failedRequest.Url;

		_logger.LogWarning(
			"PlaywrightPrintRenderer|method:{Method}|renderId:{RenderId}|reason:{Reason}|url:{Url}|failure:{Failure}",
			nameof(OnRequestFailed),
			renderId,
			"ResourceRequestFailed",
			url,
			failedRequest.Failure);
	}
}
