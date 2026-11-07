using System.ComponentModel.DataAnnotations;

namespace RagDemo.Api.Options;

/// <summary>Google Cloud settings, bound from the "Gcp" config section.</summary>
public sealed class GcpSettings
{
    public const string SectionName = "Gcp";

    [Required] public string ProjectId { get; set; } = "";

    /// <summary>Vertex AI Search location: "global", "us" or "eu".</summary>
    [Required] public string Location { get; set; } = "global";

    [Required] public string DataStoreId { get; set; } = "";

    /// <summary>The Vertex AI Search app (engine) id.</summary>
    [Required] public string EngineId { get; set; } = "";

    [Required] public string Bucket { get; set; } = "";

    [Required] public string UploadPrefix { get; set; } = "uploads";

    /// <summary>The regional host for non-global locations, as the Discovery Engine docs require.</summary>
    public string DiscoveryEngineEndpoint =>
        string.Equals(Location, "global", StringComparison.OrdinalIgnoreCase)
            ? "discoveryengine.googleapis.com"
            : $"{Location}-discoveryengine.googleapis.com";
}
