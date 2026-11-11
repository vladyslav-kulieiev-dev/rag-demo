using Google.Cloud.DiscoveryEngine.V1;
using RagDemo.Api.Ask;
using static Google.Cloud.DiscoveryEngine.V1.Answer.Types;

namespace RagDemo.Tests;

public class CitationMapperTests
{
    private static Reference Unstructured(string title, string uri, string chunk) => new()
    {
        UnstructuredDocumentInfo = new Reference.Types.UnstructuredDocumentInfo
        {
            Title = title,
            Uri = uri,
            ChunkContents = { new Reference.Types.UnstructuredDocumentInfo.Types.ChunkContent { Content = chunk } },
        },
    };

    private static Answer.Types.Citation Cite(long start, long end, params string[] referenceIds)
    {
        var citation = new Answer.Types.Citation { StartIndex = start, EndIndex = end };
        foreach (var id in referenceIds) citation.Sources.Add(new CitationSource { ReferenceId = id });
        return citation;
    }

    [Fact]
    public void Maps_citations_to_references_and_inserts_markers()
    {
        var answer = new Answer
        {
            AnswerText = "Cats sleep a lot. Dogs bark.",
            References =
            {
                Unstructured("Cats", "gs://b/uploads/cats.pdf", "Cats sleep 16 hours a day."),
                Unstructured("Dogs", "gs://b/uploads/dogs.pdf", "Dogs bark at strangers."),
            },
            Citations = { Cite(0, 17, "0"), Cite(18, 28, "1") },
        };

        var result = CitationMapper.Map(answer);

        Assert.True(result.Found);
        Assert.Equal("Cats sleep a lot.[1] Dogs bark.[2]", result.Answer);
        Assert.Collection(result.Citations,
            c => Assert.Equal((1, "Cats", "gs://b/uploads/cats.pdf", "Cats sleep 16 hours a day."), (c.Number, c.Title, c.Uri, c.Snippet)),
            c => Assert.Equal((2, "Dogs", "gs://b/uploads/dogs.pdf"), (c.Number, c.Title, c.Uri)));
    }

    [Fact]
    public void Numbers_follow_first_use_and_repeated_references_share_a_number()
    {
        var answer = new Answer
        {
            AnswerText = "A. B. C.",
            References = { Unstructured("Zero", "gs://b/0", ""), Unstructured("One", "gs://b/1", "") },
            Citations = { Cite(0, 2, "1"), Cite(3, 5, "0", "1"), Cite(6, 8, "1") },
        };

        var result = CitationMapper.Map(answer);

        Assert.Equal("A.[1] B.[1][2] C.[1]", result.Answer);
        Assert.Equal(["One", "Zero"], result.Citations.Select(c => c.Title));
    }

    [Fact]
    public void Each_marker_gets_the_passage_that_supports_its_sentence()
    {
        // Two sentences cite the same chunk, but each is supported by a different sentence in it.
        const string first = "Data breaches must be reported within 72 hours.";
        const string second = " Processors notify the controller.";
        var answer = new Answer
        {
            AnswerText = first + second,
            References =
            {
                Unstructured("GDPR", "gs://b/uploads/gdpr.pdf",
                    "The controller shall report a personal data breach to the authority within 72 hours. " +
                    "The processor shall notify the controller without undue delay."),
            },
            Citations =
            {
                Cite(0, System.Text.Encoding.UTF8.GetByteCount(first), "0"),
                Cite(System.Text.Encoding.UTF8.GetByteCount(first) + 1, System.Text.Encoding.UTF8.GetByteCount(first + second), "0"),
            },
        };

        var result = CitationMapper.Map(answer);

        Assert.Equal(first + "[1]" + second + "[1]", result.Answer);
        Assert.Collection(result.Segments,
            s =>
            {
                Assert.Equal(first, s.Text);
                Assert.StartsWith("The controller shall report", Assert.Single(s.Sources).Passage);
            },
            s =>
            {
                Assert.Equal(second, s.Text);
                Assert.StartsWith("The processor shall notify", Assert.Single(s.Sources).Passage);
            });
    }

    [Fact]
    public void Segments_keep_text_after_the_last_citation_and_merge_markers_at_the_same_place()
    {
        var answer = new Answer
        {
            AnswerText = "Cited. Not cited.",
            References = { Unstructured("A", "gs://b/a", ""), Unstructured("B", "gs://b/b", "") },
            Citations = { Cite(0, 6, "0"), Cite(0, 6, "1") },
        };

        var result = CitationMapper.Map(answer);

        Assert.Equal("Cited.[1][2] Not cited.", result.Answer);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal([1, 2], result.Segments[0].Sources.Select(s => s.Number));
        Assert.Empty(result.Segments[1].Sources);
        Assert.Null(result.Segments[0].Sources[0].Passage); // empty chunk, so nothing to match
    }

    [Fact]
    public void Byte_offsets_are_respected_for_non_ascii_text()
    {
        // "Café" is 5 bytes in UTF-8, so "Café ok." ends at byte 9 but char 8.
        var answer = new Answer
        {
            AnswerText = "Café ok. Next.",
            References = { Unstructured("Menu", "gs://b/menu.pdf", "") },
            Citations = { Cite(0, 9, "0") },
        };

        Assert.Equal("Café ok.[1] Next.", CitationMapper.Map(answer).Answer);
    }

    [Fact]
    public void Unknown_reference_ids_are_ignored()
    {
        var answer = new Answer
        {
            AnswerText = "Text.",
            References = { Unstructured("Doc", "gs://b/doc.pdf", "") },
            Citations = { Cite(0, 5, "7", "x") },
        };

        var result = CitationMapper.Map(answer);

        Assert.Equal("Text.", result.Answer);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public void Chunk_references_use_document_metadata()
    {
        var answer = new Answer
        {
            AnswerText = "Fact.",
            References =
            {
                new Reference
                {
                    ChunkInfo = new Reference.Types.ChunkInfo
                    {
                        Content = "  a   chunk\nof text ",
                        DocumentMetadata = new Reference.Types.ChunkInfo.Types.DocumentMetadata { Uri = "gs://b/uploads/report.pdf" },
                    },
                },
            },
            Citations = { Cite(0, 5, "0") },
        };

        var citation = Assert.Single(CitationMapper.Map(answer).Citations);
        Assert.Equal("report.pdf", citation.Title); // falls back to the file name when there's no title
        Assert.Equal("a chunk of text", citation.Snippet);
        Assert.Equal("  a   chunk\nof text ", citation.Chunk); // the whole chunk, untouched, for highlighting
    }

    [Fact]
    public void Skipped_answer_returns_no_answer_message()
    {
        var answer = new Answer
        {
            AnswerText = "A summary could not be generated for your search query.",
            AnswerSkippedReasons = { AnswerSkippedReason.OutOfDomainQueryIgnored },
        };

        var result = CitationMapper.Map(answer);

        Assert.False(result.Found);
        Assert.Equal(CitationMapper.NoAnswerMessage, result.Answer);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public void Empty_answer_returns_no_answer_message()
    {
        Assert.False(CitationMapper.Map(new Answer()).Found);
    }

    [Fact]
    public void Long_snippets_are_cut()
    {
        var answer = new Answer
        {
            AnswerText = "X",
            References = { Unstructured("Doc", "gs://b/d", new string('a', 1000)) },
            Citations = { Cite(0, 1, "0") },
        };

        Assert.True(CitationMapper.Map(answer).Citations[0].Snippet.Length <= 301);
    }
}
