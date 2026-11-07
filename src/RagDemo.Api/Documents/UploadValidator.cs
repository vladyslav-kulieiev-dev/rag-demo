using System.Text;

namespace RagDemo.Api.Documents;

/// <summary>Checks an uploaded file before it goes to Cloud Storage.</summary>
public static class UploadValidator
{
    public const long MaxBytes = 10 * 1024 * 1024;

    // Extension → MIME type that Vertex AI Search accepts for unstructured content.
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".md"] = "text/plain",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypes.Keys;

    /// <summary>Returns null when the file is fine, otherwise a message for the caller.</summary>
    public static string? Validate(string? fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "The file has no name.";
        if (length <= 0)
            return "The file is empty.";
        if (length > MaxBytes)
            return $"The file is larger than {MaxBytes / (1024 * 1024)} MB.";
        if (!ContentTypes.ContainsKey(Path.GetExtension(fileName)))
            return $"Only these file types are allowed: {string.Join(", ", AllowedExtensions)}.";
        return null;
    }

    public static string ContentTypeFor(string fileName) =>
        ContentTypes.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

    /// <summary>
    /// Makes a file name safe for an object path: keeps letters, digits, '.', '-' and '_',
    /// replaces anything else with '_', and caps the length.
    /// </summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(name);

        var builder = new StringBuilder(stem.Length);
        foreach (var c in stem)
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');

        var safeStem = builder.ToString().Trim('.', '_');
        if (safeStem.Length == 0) safeStem = "file";
        if (safeStem.Length > 80) safeStem = safeStem[..80];
        return safeStem + extension;
    }
}
