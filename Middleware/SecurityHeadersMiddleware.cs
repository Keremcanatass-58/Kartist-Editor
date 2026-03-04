using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Kartist.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IWebHostEnvironment _environment;

        public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
        {
            _next = next;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            
            string permissionsPolicy = "geolocation=(), microphone=(), camera=(), payment=(), usb=(), magnetometer=(), accelerometer=(), gyroscope=()";
            context.Response.Headers.Append("Permissions-Policy", permissionsPolicy);
            
            string connectSrc = "'self' https://api.groq.com https://open.spotify.com https://www.youtube.com";
            if (_environment.IsDevelopment())
            {
                connectSrc += " ws://localhost:* wss://localhost:* http://localhost:*";
            }
            
            string scriptSrc = "'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://code.jquery.com https://unpkg.com https://api.groq.com";
            if (_environment.IsDevelopment())
            {
                scriptSrc += " http://localhost:*";
            }
            
            string csp = "default-src 'self'; " +
                "script-src " + scriptSrc + "; " +
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; " +
                "img-src 'self' data: https: http:; " +
                "connect-src " + connectSrc + "; " +
                "frame-src 'self' https://open.spotify.com https://www.youtube.com; " +
                "media-src 'self' data: https:; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'none'";
            
            if (!_environment.IsDevelopment())
            {
                csp += "; upgrade-insecure-requests";
            }
            
            context.Response.Headers.Append("Content-Security-Policy", csp);
            
            context.Response.Headers.Remove("Server");
            context.Response.Headers.Remove("X-Powered-By");
            
            await _next(context);
        }
    }
}


