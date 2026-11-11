namespace RagDemo.Api.Documents;

public static class DocumentNames
{
    private const int UploadIdLength = 32;

    /// <summary>
    /// Turns a stored name into one for people: "gs://b/uploads/88146533…-Kodeks_pracy.pdf" or
    /// "88146533…-Kodeks_pracy" (the title Vertex AI Search gives it) both become "Kodeks pracy".
    /// Drops the folder, the 32-character id added at upload, the extension, and underscores.
    /// </summary>
    public static string Readable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var result = name[(name.LastIndexOf('/') + 1)..];
        if (result.Length > UploadIdLength + 1 && result[UploadIdLength] == '-'
            && result[..UploadIdLength].All(char.IsAsciiHexDigitLower))
        {
            result = result[(UploadIdLength + 1)..];
        }

        var extension = Path.GetExtension(result);
        if (UploadValidator.AllowedExtensions.Contains(extension))
            result = result[..^extension.Length];

        return result.Replace('_', ' ').Trim();
    }
}
