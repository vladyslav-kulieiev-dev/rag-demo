using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RagDemo.Api;

public static class RateLimits
{
    public const string PerIp = "per-ip";

    /// <summary>Fixed window per client IP. Each call costs money on GCP, so keep this tight.</summary>
    public static IServiceCollection AddRagDemoRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var permitLimit = config.GetValue("RateLimit:PermitLimit", 10);
        var window = TimeSpan.FromSeconds(config.GetValue("RateLimit:WindowSeconds", 60));

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PerIp, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 }));
        });
    }
}
