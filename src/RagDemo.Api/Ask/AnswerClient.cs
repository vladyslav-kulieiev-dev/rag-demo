using Google.Cloud.DiscoveryEngine.V1;
using Microsoft.Extensions.Options;
using RagDemo.Api.Options;

namespace RagDemo.Api.Ask;

public interface IAnswerClient
{
    Task<Answer> AnswerAsync(string question, CancellationToken ct);
}

/// <summary>Calls the Vertex AI Search Answer API on the app's default serving config.</summary>
public sealed class AnswerClient(Lazy<ConversationalSearchServiceClient> lazyClient, IOptions<GcpSettings> options) : IAnswerClient
{
    // v1 has no sign-in, so every caller shares one pseudo id.
    private const string UserPseudoId = "ragdemo-anonymous";

    private readonly GcpSettings _gcp = options.Value;

    public async Task<Answer> AnswerAsync(string question, CancellationToken ct)
    {
        var request = new AnswerQueryRequest
        {
            ServingConfigAsServingConfigName = ServingConfigName.FromProjectLocationCollectionEngineServingConfig(
                _gcp.ProjectId, _gcp.Location, "default_collection", _gcp.EngineId, "default_serving_config"),
            Query = new Query { Text = question },
            UserPseudoId = UserPseudoId,
            AnswerGenerationSpec = new AnswerQueryRequest.Types.AnswerGenerationSpec
            {
                IncludeCitations = true,
                IgnoreAdversarialQuery = true,
                IgnoreNonAnswerSeekingQuery = true,
                IgnoreLowRelevantContent = true,
            },
        };

        var response = await lazyClient.Value.AnswerQueryAsync(request, cancellationToken: ct);
        return response.Answer;
    }
}
