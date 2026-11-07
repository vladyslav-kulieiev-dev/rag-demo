using Grpc.Core;
using Microsoft.AspNetCore.Diagnostics;

namespace RagDemo.Api;

/// <summary>Turns a failed Google API call into a 502 ProblemDetails and logs the full error.</summary>
public sealed class GcpErrorHandler(IProblemDetailsService problemDetails, ILogger<GcpErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var rpc = exception as RpcException
                  ?? (exception as Google.GoogleApiException)?.InnerException as RpcException;

        if (exception is GcpCredentialsException)
        {
            logger.LogError(exception, "Google Cloud clients could not be created");
            return await WriteAsync(context, exception, StatusCodes.Status503ServiceUnavailable,
                "The search backend is not configured.", exception.Message);
        }

        string detail;
        if (rpc is not null)
        {
            logger.LogError(rpc, "Google Cloud call failed with {StatusCode}: {Detail}", rpc.StatusCode, rpc.Status.Detail);
            detail = $"Google Cloud returned {rpc.StatusCode}.";
        }
        else if (exception is Google.GoogleApiException api)
        {
            // Cloud Storage uses REST, so its errors arrive as GoogleApiException.
            logger.LogError(api, "Google Cloud Storage call failed with {StatusCode}", api.HttpStatusCode);
            detail = $"Cloud Storage returned {(int)api.HttpStatusCode}.";
        }
        else
        {
            return false; // Let the default handler log it and return 500.
        }

        return await WriteAsync(context, exception, StatusCodes.Status502BadGateway,
            "The search backend is unavailable.", detail);
    }

    private async ValueTask<bool> WriteAsync(HttpContext context, Exception exception, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }
}
