using Atlas.Print.Domain;
using Atlas.Print.Services;

using Microsoft.AspNetCore.Mvc;

namespace Atlas.Print.Controllers;

/// <summary>
/// Single endpoint — accepts HTML payload, returns Base64 PDF.
/// </summary>
[ApiController]
[Route("print")]
public sealed class PrintController(
	PlaywrightPrintRenderer renderer,
	ILogger<PrintController> logger) : ControllerBase
{
	private readonly PlaywrightPrintRenderer _renderer = renderer;
	private readonly ILogger<PrintController> _logger = logger;

	/// <summary>
	/// Renders the supplied HTML to a PDF and returns it as a Base64 string.
	/// </summary>
	[HttpPost("generate")]
	[ProducesResponseType(typeof(PrintResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> Generate(
		[FromBody] PrintRequest request,
		CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(request.HtmlPayload))
		{
			return BadRequest("HtmlPayload is required.");
		}

		try
		{
			string base64Pdf = await _renderer.RenderAsync(request, ct);
			return Ok(new PrintResponse { Base64Document = base64Pdf });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Print generation failed.");
			return StatusCode(StatusCodes.Status500InternalServerError, "PDF generation failed.");
		}
	}

	/// <summary>
	/// DEV only — returns the raw HTML payload for inspection in a browser.
	/// </summary>
	[HttpPost("preview")]
	public IActionResult Preview([FromBody] PrintRequest request)
	{
		return Content(request.HtmlPayload, "text/html");
	}
}
