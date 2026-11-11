using System.Text;
using Google.Cloud.DiscoveryEngine.V1;

namespace RagDemo.Api.Ask;

/// <summary>Turns an Answer API result into the AskResponse the browser shows. No I/O, so it's unit-tested.</summary>
public static class CitationMapper
{
    public const string NoAnswerMessage = "No answer was found in your documents.";
    private const int MaxSnippetLength = 300;

    public static AskResponse Map(Answer answer)
    {
        if (answer.AnswerSkippedReasons.Count > 0 || string.IsNullOrWhiteSpace(answer.AnswerText))
            return new AskResponse(NoAnswerMessage, false, [], [new AnswerSegment(NoAnswerMessage, [])]);

        // Citation indexes are UTF-8 byte offsets, so all slicing happens on the byte form of the text.
        var bytes = Encoding.UTF8.GetBytes(answer.AnswerText);

        // Number each reference the first time it's cited, so the source list follows reading order.
        var numbers = new Dictionary<int, int>();
        var citations = new List<Citation>();
        var segments = new List<(string Text, List<SegmentSource> Sources)>();
        var position = 0;

        foreach (var citation in answer.Citations.OrderBy(c => c.EndIndex))
        {
            var referenceIndexes = citation.Sources
                .Select(s => int.TryParse(s.ReferenceId, out var i) ? i : -1)
                .Where(i => i >= 0 && i < answer.References.Count)
                .Distinct()
                .ToList();
            if (referenceIndexes.Count == 0) continue;

            var end = SnapToCharBoundary(bytes, Math.Clamp((int)citation.EndIndex, position, bytes.Length));
            var start = SnapToCharBoundary(bytes, Math.Clamp((int)citation.StartIndex, 0, end));
            var claim = Encoding.UTF8.GetString(bytes, start, end - start);

            var sources = new List<SegmentSource>();
            foreach (var index in referenceIndexes)
            {
                var reference = answer.References[index];
                if (!numbers.TryGetValue(index, out var number))
                {
                    number = citations.Count + 1;
                    numbers[index] = number;
                    citations.Add(ToCitation(number, reference));
                }
                sources.Add(new SegmentSource(number, PassageMatcher.FindBestPassage(FullText(reference), claim)));
            }

            if (end == position && segments.Count > 0)
            {
                // Another citation ending at the same place: add its sources to the previous piece.
                var previous = segments[^1].Sources;
                previous.AddRange(sources.Where(s => previous.All(p => p.Number != s.Number)));
            }
            else
            {
                segments.Add((Encoding.UTF8.GetString(bytes, position, end - position), sources));
            }
            position = end;
        }

        if (position < bytes.Length)
            segments.Add((Encoding.UTF8.GetString(bytes, position, bytes.Length - position), []));

        var answerSegments = segments
            .Select(s => new AnswerSegment(s.Text, s.Sources.OrderBy(x => x.Number).ToList()))
            .ToList();
        var text = string.Concat(answerSegments.Select(s => s.Text + string.Concat(s.Sources.Select(x => $"[{x.Number}]"))));

        return new AskResponse(text, true, citations, answerSegments);
    }

    private static Citation ToCitation(int number, Answer.Types.Reference reference)
    {
        switch (reference.ContentCase)
        {
            case Answer.Types.Reference.ContentOneofCase.UnstructuredDocumentInfo:
                var doc = reference.UnstructuredDocumentInfo;
                return new Citation(number, TitleOr(doc.Title, doc.Uri), doc.Uri,
                    Trim(doc.ChunkContents.FirstOrDefault()?.Content));

            case Answer.Types.Reference.ContentOneofCase.ChunkInfo:
                var chunk = reference.ChunkInfo;
                var meta = chunk.DocumentMetadata;
                return new Citation(number, TitleOr(meta?.Title, meta?.Uri), meta?.Uri ?? "", Trim(chunk.Content));

            case Answer.Types.Reference.ContentOneofCase.StructuredDocumentInfo:
                var structured = reference.StructuredDocumentInfo;
                return new Citation(number, TitleOr(structured.Title, structured.Uri), structured.Uri, "");

            default:
                return new Citation(number, "Unknown source", "", "");
        }
    }

    /// <summary>The whole cited text, untrimmed, for passage matching.</summary>
    private static string FullText(Answer.Types.Reference reference) => reference.ContentCase switch
    {
        Answer.Types.Reference.ContentOneofCase.UnstructuredDocumentInfo =>
            string.Join('\n', reference.UnstructuredDocumentInfo.ChunkContents.Select(c => c.Content)),
        Answer.Types.Reference.ContentOneofCase.ChunkInfo => reference.ChunkInfo.Content,
        _ => "",
    };

    // Moves forward past UTF-8 continuation bytes (10xxxxxx) so a slice never splits a character.
    private static int SnapToCharBoundary(byte[] bytes, int index)
    {
        while (index < bytes.Length && (bytes[index] & 0xC0) == 0x80) index++;
        return index;
    }

    private static string TitleOr(string? title, string? uri) =>
        !string.IsNullOrWhiteSpace(title) ? title
        : !string.IsNullOrWhiteSpace(uri) ? uri[(uri.LastIndexOf('/') + 1)..]
        : "Untitled document";

    private static string Trim(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return "";
        var clean = string.Join(' ', snippet.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= MaxSnippetLength ? clean : clean[..MaxSnippetLength].TrimEnd() + "…";
    }
}
