using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RagDemo.Api.Options;

namespace RagDemo.Api.Documents;

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents").RequireRateLimiting(RateLimits.PerIp);

        group.MapPost("/", UploadAsync)
            .DisableAntiforgery() // API called by our own page and by curl; no cookie auth to protect.
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadResult>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/", async (IDocumentIndexer indexer, CancellationToken ct) =>
            TypedResults.Ok(await indexer.ListAsync(ct)));

        return group;
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IDocumentIndexer indexer,
        IOptions<UploadSettings> uploadOptions,
        CancellationToken ct)
    {
        if (!HasValidApiKey(request, uploadOptions.Value.ApiKey))
            return TypedResults.Problem("A valid X-Api-Key header is required to upload.", statusCode: StatusCodes.Status401Unauthorized);

        if (!request.HasFormContentType)
            return Invalid("Send the file as multipart/form-data in a field named \"file\".");

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null)
            return Invalid("Send the file as multipart/form-data in a field named \"file\".");

        var error = UploadValidator.Validate(file.FileName, file.Length);
        if (error is not null)
            return Invalid(error);

        await using var stream = file.OpenReadStream();
        var result = await indexer.UploadAndImportAsync(file.FileName, stream, ct);
        return TypedResults.Accepted((string?)null, result);
    }

    private static IResult Invalid(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

    private static bool HasValidApiKey(HttpRequest request, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        var given = request.Headers["X-Api-Key"].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }
}
