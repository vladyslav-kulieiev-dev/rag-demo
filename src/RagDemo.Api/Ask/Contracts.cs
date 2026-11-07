namespace RagDemo.Api.Ask;

public sealed record AskRequest(string? Question);

/// <param name="Answer">Answer text with [n] markers that point into <paramref name="Citations"/>.</param>
/// <param name="Found">False when Vertex AI Search found nothing in the documents to answer from.</param>
public sealed record AskResponse(string Answer, bool Found, IReadOnlyList<Citation> Citations);

/// <param name="Number">The n used in the [n] markers, starting at 1.</param>
public sealed record Citation(int Number, string Title, string Uri, string Snippet);
