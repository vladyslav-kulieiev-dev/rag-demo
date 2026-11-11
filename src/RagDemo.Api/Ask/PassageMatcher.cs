using System.Text.RegularExpressions;

namespace RagDemo.Api.Ask;

/// <summary>
/// Finds the sentence in a cited chunk that best supports one sentence of the answer.
/// The Answer API only says which chunk a sentence came from, and a chunk can span several articles,
/// so without this the highlight would always land on the start of the chunk.
/// </summary>
public static partial class PassageMatcher
{
    // Words are compared by their first letters, which copes with Polish (and English) inflection:
    // "podejmuje" and "podejmujący" both become "podej".
    private const int StemLength = 5;
    private const int MinSharedWords = 2;
    private const int MinPassageWords = 4;

    private static readonly HashSet<string> StopWords =
    [
        // Polish
        "się", "nie", "jest", "są", "oraz", "lub", "albo", "dla", "jak", "przez", "przy", "pod", "nad", "bez",
        "który", "która", "które", "którego", "której", "którym", "których", "tym", "ten", "tego", "temu",
        "jego", "jej", "ich", "być", "może", "też", "także", "czy", "już", "tak", "ale", "gdy", "jeżeli", "jeśli",
        "swój", "swoim", "swoje", "swojej", "lecz", "tylko", "żeby", "aby", "tej", "tych",
        // English
        "the", "and", "for", "that", "with", "this", "are", "was", "from", "any", "has", "have", "which",
        "shall", "not", "such", "their", "its", "other", "may", "been", "into", "where", "when", "under",
    ];

    /// <returns>The best matching passage, or null when no passage shares enough words with the claim.</returns>
    public static string? FindBestPassage(string chunkText, string claim)
    {
        var claimStems = Stems(claim);
        if (claimStems.Count == 0) return null;

        string? best = null;
        var bestScore = 0.0;
        foreach (var passage in SplitPassages(chunkText))
        {
            var stems = Stems(passage);
            var shared = stems.Count(claimStems.Contains);
            if (shared < MinSharedWords) continue;

            // Dividing by the square root favours focused sentences over long ones that share words by chance.
            var score = shared / Math.Sqrt(stems.Count);
            if (score > bestScore)
            {
                best = passage;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Splits chunk text from the layout parser into sentences, without markdown.
    /// Headings are skipped, and short pieces such as "Art. 153." or "§ 1." are joined to the sentence after them.
    /// </summary>
    internal static List<string> SplitPassages(string chunkText)
    {
        var passages = new List<string>();
        var pending = "";

        foreach (var rawLine in chunkText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#'))
            {
                // A markdown heading. The parser often puts the text after it on the same line
                // ("## Urlopy wypoczynkowe Art. 152. § 1. …"), so drop only the heading words.
                line = line.TrimStart('#').Trim();
                if (!SentenceEnd().IsMatch(line)) continue; // the whole line is a heading
                var heading = HeadingPrefix().Match(line);
                if (heading.Success) line = line[heading.Length..];
            }

            line = Whitespace().Replace(MarkdownSymbols().Replace(line, " "), " ").Trim();
            // Rejoin words split across PDF lines: "przepraco- waniu" → "przepracowaniu".
            line = LineBreakHyphen().Replace(line, "$1$2");
            if (line.Length == 0) continue;

            foreach (var piece in SentenceBreak().Split(line))
            {
                pending = pending.Length == 0 ? piece : $"{pending} {piece}";
                if (WordCount(pending) >= MinPassageWords)
                {
                    passages.Add(pending);
                    pending = "";
                }
            }
        }

        if (pending.Length > 0)
        {
            if (passages.Count > 0) passages[^1] = $"{passages[^1]} {pending}";
            else passages.Add(pending);
        }
        return passages;
    }

    private static HashSet<string> Stems(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.Length > StemLength ? w[..StemLength] : w)
            .ToHashSet();

    // Only real words count, so "Art. 153. § 1." (one word) stays attached to the sentence after it.
    private static int WordCount(string text) => Word().Matches(text).Count(m => m.Length >= 2);

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    [GeneratedRegex(@"[#*_`>|]")]
    private static partial Regex MarkdownSymbols();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[.!?;]")]
    private static partial Regex SentenceEnd();

    // Heading words up to the first word that starts with a capital letter or "§".
    [GeneratedRegex(@"^.{1,80}?\s+(?=[\p{Lu}§])")]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"(\p{Ll})- (\p{Ll})")]
    private static partial Regex LineBreakHyphen();

    // A sentence ends with . ! ? or ; followed by a capital letter, "§" or an opening quote or bracket.
    // "Art. 153" doesn't split, because a digit follows the dot.
    [GeneratedRegex(@"(?<=[.!?;])\s+(?=[\p{Lu}§„""(])")]
    private static partial Regex SentenceBreak();
}
