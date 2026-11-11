using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RagDemo.Api;

public static class RateLimits
{
    public const string PerIp = "per-ip";

    public const string PerIpMessage = "Too many requests. Wait a minute and try again.";
    public const string DailyMessage = "The daily question limit for this demo has been reached. Try again tomorrow.";

    /// <summary>
    /// Two limits keep GCP costs down:
    /// - a fixed window per client IP on the API endpoints (policy <see cref="PerIp"/>);
    /// - one daily window for /api/ask shared by all visitors.
    /// Both are in memory, so each instance counts on its own and a restart resets the counts.
    /// </summary>
    public static IServiceCollection AddRagDemoRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var permitLimit = config.GetValue("RateLimit:PermitLimit", 10);
        var window = TimeSpan.FromSeconds(config.GetValue("RateLimit:WindowSeconds", 60));
        var dailyAskLimit = config.GetValue("RateLimit:DailyAskLimit", 200);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(PerIp, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 }));

            // The global limiter runs before the endpoint policy, for every request.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                dailyAskLimit > 0 && context.Request.Path.StartsWithSegments("/api/ask")
                    ? RateLimitPartition.GetFixedWindowLimiter("ask-daily",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = dailyAskLimit, Window = TimeSpan.FromDays(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("unlimited"));

            options.OnRejected = async (context, ct) =>
            {
                // A wait longer than the per-IP window means the daily limit rejected the request.
                var daily = false;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    daily = retryAfter > window;
                }

                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                        Detail = daily ? DailyMessage : PerIpMessage,
                    },
                });
            };
        });
    }
}
