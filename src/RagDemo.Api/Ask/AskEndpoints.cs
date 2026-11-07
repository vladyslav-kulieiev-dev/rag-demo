namespace RagDemo.Api.Ask;

public static class AskEndpoints
{
    public const int MaxQuestionLength = 1000;

    public static RouteHandlerBuilder MapAskEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/ask", AskAsync)
            .WithTags("Ask")
            .RequireRateLimiting(RateLimits.PerIp)
            .Produces<AskResponse>()
            .ProducesValidationProblem();

    private static async Task<IResult> AskAsync(AskRequest request, IAnswerClient answers, CancellationToken ct)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrEmpty(question))
            return Invalid("Enter a question.");
        if (question.Length > MaxQuestionLength)
            return Invalid($"Keep the question under {MaxQuestionLength} characters.");

        var answer = await answers.AnswerAsync(question, ct);
        return TypedResults.Ok(CitationMapper.Map(answer));
    }

    private static IResult Invalid(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["question"] = [message] });
}
