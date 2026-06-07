namespace Atlas.Print.Domain;

/// <summary>
/// Response from <c>POST /print/generate</c>.
/// </summary>
public sealed class PrintResponse
{
	public required string Base64Document { get; init; }
}
