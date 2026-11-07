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
            return new AskResponse(NoAnswerMessage, false, []);

        // Number each reference the first time a citation uses it, so the list follows reading order.
        var numbers = new Dictionary<int, int>();
        var citations = new List<Citation>();
        var markers = new List<(int ByteIndex, string Text)>();

        foreach (var citation in answer.Citations)
        {
            var cited = new SortedSet<int>();
            foreach (var source in citation.Sources)
            {
                if (!int.TryParse(source.ReferenceId, out var index) || index < 0 || index >= answer.References.Count)
                    continue;

                if (!numbers.TryGetValue(index, out var number))
                {
                    number = citations.Count + 1;
                    numbers[index] = number;
                    citations.Add(ToCitation(number, answer.References[index]));
                }
                cited.Add(number);
            }

            if (cited.Count > 0)
                markers.Add(((int)citation.EndIndex, string.Concat(cited.Select(n => $"[{n}]"))));
        }

        return new AskResponse(InsertMarkers(answer.AnswerText, markers), true, citations);
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

    /// <summary>Citation indexes are UTF-8 byte offsets, so markers are inserted on the byte form of the text.</summary>
    internal static string InsertMarkers(string text, List<(int ByteIndex, string Text)> markers)
    {
        if (markers.Count == 0) return text;

        var bytes = Encoding.UTF8.GetBytes(text);
        var output = new StringBuilder(text.Length + markers.Count * 4);
        var position = 0;

        foreach (var (byteIndex, marker) in markers.OrderBy(m => m.ByteIndex))
        {
            var end = SnapToCharBoundary(bytes, Math.Clamp(byteIndex, position, bytes.Length));
            output.Append(Encoding.UTF8.GetString(bytes, position, end - position)).Append(marker);
            position = end;
        }

        output.Append(Encoding.UTF8.GetString(bytes, position, bytes.Length - position));
        return output.ToString();
    }

    // Moves forward past UTF-8 continuation bytes (10xxxxxx) so a marker never splits a character.
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
