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
            // Statik dosyalar iÃƒÂ§in CSP header eklemeye gerek yok (performans)
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/lib/") || path.StartsWith("/css/") || path.StartsWith("/js/") ||
                path.StartsWith("/uploads/") || path.StartsWith("/img/") || path.EndsWith(".ico"))
            {
                await _next(context);
                return;
            }

            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

            string permissionsPolicy = "geolocation=(), microphone=(), camera=(), payment=(), usb=(), magnetometer=(), accelerometer=(), gyroscope=()";
            context.Response.Headers.Append("Permissions-Policy", permissionsPolicy);

            string connectSrc = "'self' https://api.groq.com https://open.spotify.com https://www.youtube.com https://cdn.jsdelivr.net https://accounts.google.com https://image.pollinations.ai https://pollinations.ai https://api.airforce https://*.unsplash.com https://cdnjs.cloudflare.com";
            if (_environment.IsDevelopment())
            {
                connectSrc += " ws://localhost:* wss://localhost:* http://localhost:*";
            }

            string scriptSrc = "'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://code.jquery.com https://unpkg.com https://api.groq.com https://accounts.google.com";
            if (_environment.IsDevelopment())
            {
                scriptSrc += " http://localhost:*";
            }

            string csp = "default-src 'self'; " +
                "frame-src 'self' https://open.spotify.com https://www.youtube.com https://www.google.com https://www.google.com.tr https://www.openstreetmap.org; " +
                "script-src " + scriptSrc + "; " +
                "worker-src 'self' blob: https://cdn.jsdelivr.net https://unpkg.com; " +
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; " +
                "connect-src " + connectSrc + " https://kartistt.com.tr wss://kartistt.com.tr https://*.pollinations.ai https://*.bing.com https://unpkg.com https://*.imgly.com; " +
                "img-src 'self' data: blob: https://* http://*; " +
                "media-src 'self' data: https:; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self' https://accounts.google.com; " +
                "frame-ancestors 'self'";

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






