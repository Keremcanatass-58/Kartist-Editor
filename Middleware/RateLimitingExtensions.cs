using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Kartist.Middleware
{
    public static class RateLimitingExtensions
    {
        public static IApplicationBuilder UseRateLimiting(
            this IApplicationBuilder app, 
            int maxRequests = 100, 
            int timeWindowSeconds = 60)
        {
            return app.Use(async (context, next) =>
            {
                var middleware = new RateLimitingMiddleware(next, maxRequests, timeWindowSeconds);
                await middleware.InvokeAsync(context);
            });
        }
    }
}

