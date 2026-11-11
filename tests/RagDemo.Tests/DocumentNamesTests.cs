using RagDemo.Api.Documents;

namespace RagDemo.Tests;

public class DocumentNamesTests
{
    [Theory]
    [InlineData("gs://bucket/uploads/88146533e9e740dda13901f4ba8c6f1b-Kodeks_pracy.pdf", "Kodeks pracy")]
    [InlineData("88146533e9e740dda13901f4ba8c6f1b-Kodeks_pracy", "Kodeks pracy")] // title from Vertex AI Search
    [InlineData("gs://bucket/uploads/GDPR.pdf", "GDPR")]
    [InlineData("Annual report", "Annual report")]
    [InlineData("notes.v2.txt", "notes.v2")]
    [InlineData("short-name", "short-name")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Readable_drops_folder_upload_id_extension_and_underscores(string? name, string expected) =>
        Assert.Equal(expected, DocumentNames.Readable(name));
}
