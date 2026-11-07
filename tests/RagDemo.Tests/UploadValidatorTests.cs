using RagDemo.Api.Documents;

namespace RagDemo.Tests;

public class UploadValidatorTests
{
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("notes.TXT")]
    [InlineData("readme.md")]
    [InlineData("page.html")]
    [InlineData("letter.docx")]
    public void Allowed_types_pass(string name) => Assert.Null(UploadValidator.Validate(name, 1234));

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void Other_types_are_rejected(string name) => Assert.NotNull(UploadValidator.Validate(name, 1234));

    [Fact]
    public void Empty_file_is_rejected() => Assert.NotNull(UploadValidator.Validate("a.pdf", 0));

    [Fact]
    public void Missing_name_is_rejected() => Assert.NotNull(UploadValidator.Validate("", 10));

    [Fact]
    public void File_over_limit_is_rejected() =>
        Assert.NotNull(UploadValidator.Validate("big.pdf", UploadValidator.MaxBytes + 1));

    [Fact]
    public void File_at_limit_passes() => Assert.Null(UploadValidator.Validate("big.pdf", UploadValidator.MaxBytes));

    [Theory]
    [InlineData("Annual Report 2025.PDF", "Annual_Report_2025.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData(@"C:\Users\me\résumé.docx", "r_sum.docx")]
    [InlineData("???.md", "file.md")]
    public void SafeName_strips_paths_and_odd_characters(string input, string expected) =>
        Assert.Equal(expected, UploadValidator.SafeName(input));

    [Fact]
    public void Content_type_matches_extension()
    {
        Assert.Equal("application/pdf", UploadValidator.ContentTypeFor("a.pdf"));
        Assert.Equal("text/plain", UploadValidator.ContentTypeFor("a.md"));
    }

    [Theory]
    [InlineData("gs://b/uploads/0123456789abcdef0123456789abcdef-report.pdf", "report.pdf")]
    [InlineData("gs://b/other/report.pdf", "report.pdf")]
    [InlineData(null, "")]
    public void File_name_is_read_back_from_uri(string? uri, string expected) =>
        Assert.Equal(expected, DocumentIndexer.FileNameFromUri(uri));
}
