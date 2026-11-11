namespace RagDemo.Api.Ask;

public sealed record AskRequest(string? Question);

/// <param name="Answer">Answer text with [n] markers that point into <paramref name="Citations"/>.</param>
/// <param name="Found">False when Vertex AI Search found nothing in the documents to answer from.</param>
/// <param name="Segments">The same answer split at each citation, so every marker can link to its own passage.</param>
public sealed record AskResponse(
    string Answer,
    bool Found,
    IReadOnlyList<Citation> Citations,
    IReadOnlyList<AnswerSegment> Segments);

/// <param name="Number">The n used in the [n] markers, starting at 1.</param>
/// <param name="Snippet">The start of the cited chunk, shortened for display.</param>
/// <param name="Chunk">The whole cited chunk, used to highlight it in the document.</param>
public sealed record Citation(int Number, string Title, string Uri, string Snippet, string Chunk);

/// <param name="Text">A piece of the answer text, without markers.</param>
/// <param name="Sources">The citations at the end of this piece, in number order.</param>
public sealed record AnswerSegment(string Text, IReadOnlyList<SegmentSource> Sources);

/// <param name="Passage">The sentence in the cited chunk that best supports this piece; null when none matched well.</param>
public sealed record SegmentSource(int Number, string? Passage);
