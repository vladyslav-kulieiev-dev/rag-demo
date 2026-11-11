using RagDemo.Api.Ask;

namespace RagDemo.Tests;

public class PassageMatcherTests
{
    // A chunk as the layout parser returns it for the Polish Labour Code (Kodeks pracy).
    private const string LabourCodeChunk = """
        ## Urlopy wypoczynkowe
        Art. 152. § 1. Pracownikowi przysługuje prawo do corocznego, nieprzerwanego, płatnego urlopu wypoczynkowego, zwanego dalej „urlopem”. § 2. Pracownik nie może zrzec się prawa do urlopu.
        Art. 153. § 1. Pracownik podejmujący pracę po raz pierwszy, w roku kalendarzowym, w którym podjął pracę, uzyskuje prawo do urlopu z upływem każdego miesiąca pracy, w wymiarze 1/12 wymiaru urlopu przysługującego mu po przepracowaniu roku. § 2. Prawo do kolejnych urlopów pracownik nabywa w każdym następnym roku kalendarzowym.
        """;

    [Fact]
    public void Picks_the_article_that_supports_the_claim_not_the_start_of_the_chunk()
    {
        const string claim = "Pracownik, który podejmuje pracę po raz pierwszy w swoim życiu, w roku kalendarzowym, " +
                             "w którym rozpoczął zatrudnienie, uzyskuje prawo do urlopu z upływem każdego przepracowanego miesiąca.";

        var passage = PassageMatcher.FindBestPassage(LabourCodeChunk, claim);

        Assert.NotNull(passage);
        Assert.StartsWith("Art. 153. § 1. Pracownik podejmujący pracę po raz pierwszy", passage);
    }

    [Fact]
    public void Handles_the_single_line_format_the_answer_api_returns()
    {
        // Copied from a real Answer API response: one line, heading first, a word split across PDF lines.
        const string chunk = "## Urlopy wypoczynkowe Art. 152. § 1. Pracownikowi przysługuje prawo do corocznego, nieprzerwanego, " +
                             "płatnego urlopu wypoczynkowego, zwanego dalej „urlopem\". § 2. Pracownik nie może zrzec się prawa do urlopu. " +
                             "Art. 153. § 1. Pracownik podejmujący pracę po raz pierwszy, w roku kalendarzowym, w którym podjął pracę, " +
                             "uzyskuje prawo do urlopu z upływem każdego miesiąca pracy, w wymiarze 1/12 wymiaru urlopu przysługującego " +
                             "mu po przepraco- waniu roku. § 2. Prawo do kolejnych urlopów pracownik nabywa w każdym następnym roku kalendarzowym.";

        var first = PassageMatcher.FindBestPassage(chunk,
            "Pracownik podejmujący pracę po raz pierwszy uzyskuje prawo do urlopu w roku kalendarzowym, w którym podjął pracę, z upływem każdego miesiąca pracy.");
        var second = PassageMatcher.FindBestPassage(chunk,
            "Prawo do kolejnych urlopów pracownik ten zaczyna nabywać w każdym następnym roku kalendarzowym.");

        Assert.Equal("Art. 153. § 1. Pracownik podejmujący pracę po raz pierwszy, w roku kalendarzowym, w którym podjął pracę, " +
                     "uzyskuje prawo do urlopu z upływem każdego miesiąca pracy, w wymiarze 1/12 wymiaru urlopu przysługującego " +
                     "mu po przepracowaniu roku.", first);
        Assert.Equal("§ 2. Prawo do kolejnych urlopów pracownik nabywa w każdym następnym roku kalendarzowym.", second);
        Assert.DoesNotContain(PassageMatcher.SplitPassages(chunk), p => p.Contains("Urlopy wypoczynkowe"));
    }

    [Fact]
    public void Matches_a_claim_about_the_first_article()
    {
        var passage = PassageMatcher.FindBestPassage(LabourCodeChunk, "Pracownik nie może zrzec się prawa do urlopu.");

        Assert.Equal("§ 2. Pracownik nie może zrzec się prawa do urlopu.", passage);
    }

    [Fact]
    public void Works_for_english_text()
    {
        const string chunk = "In the case of a personal data breach, the controller shall notify the supervisory authority " +
                             "without undue delay and, where feasible, not later than 72 hours after having become aware of it. " +
                             "The processor shall notify the controller without undue delay after becoming aware of a personal data breach.";

        var passage = PassageMatcher.FindBestPassage(chunk, "The controller must notify the supervisory authority within 72 hours.");

        Assert.StartsWith("In the case of a personal data breach", passage);
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        Assert.Null(PassageMatcher.FindBestPassage(LabourCodeChunk, "The boiling point of mercury is 357 degrees."));
        Assert.Null(PassageMatcher.FindBestPassage("", "Anything at all here."));
    }

    [Fact]
    public void Splits_into_sentences_without_headings_or_markdown()
    {
        var passages = PassageMatcher.SplitPassages(LabourCodeChunk);

        Assert.DoesNotContain(passages, p => p.Contains("Urlopy wypoczynkowe"));
        Assert.DoesNotContain(passages, p => p.Contains('#'));
        // Short pieces like "Art. 153." and "§ 1." stay attached to their sentence.
        Assert.Contains(passages, p => p.StartsWith("Art. 152. § 1. Pracownikowi przysługuje"));
        Assert.Contains(passages, p => p.StartsWith("§ 2. Prawo do kolejnych urlopów"));
        Assert.All(passages, p => Assert.True(p.Split(' ').Length >= 4, p));
    }
}
