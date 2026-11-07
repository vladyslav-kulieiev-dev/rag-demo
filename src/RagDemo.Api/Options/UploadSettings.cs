namespace RagDemo.Api.Options;

/// <summary>Upload settings, bound from the "Upload" config section.</summary>
public sealed class UploadSettings
{
    public const string SectionName = "Upload";

    /// <summary>When set, POST /api/documents requires a matching X-Api-Key header.</summary>
    public string? ApiKey { get; set; }
}
