using Google.Cloud.DiscoveryEngine.V1;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using RagDemo.Api.Options;

namespace RagDemo.Api;

/// <summary>Thrown when the Google clients can't be created, usually because credentials are missing.</summary>
public sealed class GcpCredentialsException(Exception inner)
    : Exception("Google Cloud credentials were not found. Locally, run 'gcloud auth application-default login'.", inner);

public static class GcpClients
{
    /// <summary>
    /// Registers the Google clients as Lazy singletons. They authenticate with Application Default
    /// Credentials, which are only looked up on first use, so request validation runs before any GCP call.
    /// </summary>
    public static IServiceCollection AddGcpClients(this IServiceCollection services)
    {
        services.AddSingleton(_ => CreateLazy(() => StorageClient.Create()));
        services.AddSingleton(sp => CreateLazy(() => new DocumentServiceClientBuilder
        {
            Endpoint = Endpoint(sp),
        }.Build()));
        services.AddSingleton(sp => CreateLazy(() => new ConversationalSearchServiceClientBuilder
        {
            Endpoint = Endpoint(sp),
        }.Build()));
        return services;
    }

    private static string Endpoint(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<GcpSettings>>().Value.DiscoveryEngineEndpoint;

    private static Lazy<T> CreateLazy<T>(Func<T> create) => new(() =>
    {
        try
        {
            return create();
        }
        catch (InvalidOperationException ex)
        {
            // Gax and Storage throw InvalidOperationException when no default credentials exist.
            throw new GcpCredentialsException(ex);
        }
    });
}
