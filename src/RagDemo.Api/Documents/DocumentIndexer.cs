using Google.Cloud.DiscoveryEngine.V1;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using RagDemo.Api.Options;

namespace RagDemo.Api.Documents;

public sealed record UploadResult(string GcsUri, string OperationName);

public sealed record IndexedDocument(string Id, string Uri, string FileName);

public interface IDocumentIndexer
{
    Task<UploadResult> UploadAndImportAsync(string fileName, Stream content, CancellationToken ct);
    Task<IReadOnlyList<IndexedDocument>> ListAsync(CancellationToken ct);
}

/// <summary>Stores files in Cloud Storage and imports them into the Vertex AI Search data store.</summary>
public sealed class DocumentIndexer(
    Lazy<StorageClient> storageClient,
    Lazy<DocumentServiceClient> documentClient,
    IOptions<GcpSettings> options,
    ILogger<DocumentIndexer> logger) : IDocumentIndexer
{
    private const int MaxListed = 200;
    private readonly GcpSettings _gcp = options.Value;
    private StorageClient Storage => storageClient.Value;
    private DocumentServiceClient Documents => documentClient.Value;

    private BranchName DefaultBranch =>
        BranchName.FromProjectLocationDataStoreBranch(_gcp.ProjectId, _gcp.Location, _gcp.DataStoreId, "default_branch");

    public async Task<UploadResult> UploadAndImportAsync(string fileName, Stream content, CancellationToken ct)
    {
        var objectName = $"{_gcp.UploadPrefix.Trim('/')}/{Guid.NewGuid():N}-{UploadValidator.SafeName(fileName)}";
        await Storage.UploadObjectAsync(_gcp.Bucket, objectName, UploadValidator.ContentTypeFor(fileName), content, cancellationToken: ct);
        var gcsUri = $"gs://{_gcp.Bucket}/{objectName}";
        logger.LogInformation("Uploaded {FileName} to {GcsUri}", fileName, gcsUri);

        var request = new ImportDocumentsRequest
        {
            ParentAsBranchName = DefaultBranch,
            GcsSource = new GcsSource { InputUris = { gcsUri }, DataSchema = "content" },
            ReconciliationMode = ImportDocumentsRequest.Types.ReconciliationMode.Incremental,
        };

        // Indexing takes minutes, so return the operation name instead of waiting for it.
        var operation = await Documents.ImportDocumentsAsync(request, cancellationToken: ct);
        logger.LogInformation("Started import {Operation} for {GcsUri}", operation.Name, gcsUri);
        return new UploadResult(gcsUri, operation.Name);
    }

    public async Task<IReadOnlyList<IndexedDocument>> ListAsync(CancellationToken ct)
    {
        var request = new ListDocumentsRequest { ParentAsBranchName = DefaultBranch };
        var page = await Documents.ListDocumentsAsync(request).ReadPageAsync(MaxListed, ct);

        return page
            .Select(d => new IndexedDocument(d.Id, d.Content?.Uri ?? "", FileNameFromUri(d.Content?.Uri)))
            .ToList();
    }

    /// <summary>Turns gs://bucket/prefix/{guid}-name.pdf into name.pdf.</summary>
    internal static string FileNameFromUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return "";
        var last = uri[(uri.LastIndexOf('/') + 1)..];
        var dash = last.IndexOf('-');
        return dash == 32 ? last[(dash + 1)..] : last;
    }
}
