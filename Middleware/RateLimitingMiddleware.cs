using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Kartist.Middleware
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ConcurrentDictionary<string, RateLimitInfo> _requestCounts = new();
        private readonly int _maxRequests;
        private readonly TimeSpan _timeWindow;

        public RateLimitingMiddleware(RequestDelegate next, int maxRequests = 100, int timeWindowSeconds = 60)
        {
            _next = next;
            _maxRequests = maxRequests;
            _timeWindow = TimeSpan.FromSeconds(timeWindowSeconds);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Method == "POST" || context.Request.Method == "PUT" || context.Request.Method == "DELETE")
            {
                var clientIp = GetClientIp(context);
                var key = $"{clientIp}:{context.Request.Path}";

                var now = DateTime.UtcNow;
                var rateLimitInfo = _requestCounts.GetOrAdd(key, _ => new RateLimitInfo { FirstRequest = now, Count = 0 });

                if (now - rateLimitInfo.FirstRequest > _timeWindow)
                {
                    rateLimitInfo.FirstRequest = now;
                    rateLimitInfo.Count = 0;
                }

                rateLimitInfo.Count++;

                if (rateLimitInfo.Count > _maxRequests)
                {
                    context.Response.StatusCode = 429;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Çok fazla istek gönderdiniz. Lütfen bir süre bekleyin.\"}");
                    return;
                }

                context.Response.Headers.Append("X-RateLimit-Limit", _maxRequests.ToString());
                context.Response.Headers.Append("X-RateLimit-Remaining", (_maxRequests - rateLimitInfo.Count).ToString());
            }

            await _next(context);
        }

        private string GetClientIp(HttpContext context)
        {
            var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ip))
            {
                return ip.Split(',')[0].Trim();
            }
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private class RateLimitInfo
        {
            public DateTime FirstRequest { get; set; }
            public int Count { get; set; }
        }
    }
}


