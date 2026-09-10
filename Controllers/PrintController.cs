using Atlas.Print.Domain;
using Atlas.Print.Services;

using Microsoft.AspNetCore.Mvc;

namespace Atlas.Print.Controllers;

/// <summary>
/// Print rendering endpoints. <see cref="Generate"/> is the production PDF-generation
/// path; <see cref="Preview"/> is a development-only diagnostic aid, gated at runtime
/// so it is unreachable outside the Development environment regardless of what
/// network calls it.
/// </summary>
[ApiController]
[Route("print")]
public sealed class PrintController(
	PlaywrightPrintRenderer renderer,
	IWebHostEnvironment environment,
	ILogger<PrintController> logger) : ControllerBase
{
	private static readonly string[] _previewAllowedEnvironments = ["LOCAL", "DEV"];

	private readonly PlaywrightPrintRenderer _renderer = renderer;
	private readonly IWebHostEnvironment _environment = environment;
	private readonly ILogger<PrintController> _logger = logger;

	/// <summary>
	/// Renders the supplied HTML to a PDF and returns it as a Base64 string.
	/// </summary>
	/// <returns>
	/// 200 with a <see cref="PrintResponse"/> on success; 400 if
	/// <see cref="PrintRequest.HtmlPayload"/> is missing/blank; 500 if rendering fails.
	/// </returns>
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
	/// DEV only — returns the raw HTML payload for inspection in a browser, so a
	/// template's layout can be checked without generating a PDF. Returns 404 outside
	/// the Development environment; not intended to ever be reachable in QA/PROD.
	/// </summary>
	[HttpPost("preview")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public IActionResult Preview([FromBody] PrintRequest request)
	{
		if (!_previewAllowedEnvironments.Contains(_environment.EnvironmentName, StringComparer.OrdinalIgnoreCase))
		{
			return NotFound();
		}

		return Content(request.HtmlPayload, "text/html");
	}
}
