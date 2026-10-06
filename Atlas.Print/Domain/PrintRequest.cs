namespace Atlas.Print.Domain;

/// <summary>
/// Request body for <c>POST /print/generate</c>.
/// </summary>
public sealed class PrintRequest
{
	/// <summary>Full self-contained HTML document to render.</summary>
	public required string HtmlPayload { get; init; }

	/// <summary>
	/// Optional header HTML — injected into every page header.
	/// Supports Playwright template variables:
	/// <c>&lt;span class='pageNumber'/&gt;</c>,
	/// <c>&lt;span class='totalPages'/&gt;</c>,
	/// <c>&lt;span class='date'/&gt;</c>.
	/// </summary>
	public string? HeaderHtml { get; init; }

	/// <summary>Optional footer HTML — injected into every page footer.</summary>
	public string? FooterHtml { get; init; }

	/// <summary>Page orientation: <c>PORTRAIT</c> or <c>LANDSCAPE</c>. Defaults to PORTRAIT.</summary>
	public string PrintFormat { get; init; } = "PORTRAIT";

	/// <summary>Top margin. Defaults to "1.10in".</summary>
	public string MarginTop { get; init; } = "1.10in";

	/// <summary>Bottom margin. Defaults to "0.50in".</summary>
	public string MarginBottom { get; init; } = "0.50in";

	/// <summary>Left margin. Defaults to "0.20in".</summary>
	public string MarginLeft { get; init; } = "0.20in";

	/// <summary>Right margin. Defaults to "0.20in".</summary>
	public string MarginRight { get; init; } = "0.20in";
}
