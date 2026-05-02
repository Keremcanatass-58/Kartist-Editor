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
            var path = context.Request.Path.Value ?? "";

            // Apply nosniff everywhere so an attacker can't trick a browser into
            // re-interpreting an uploaded image as HTML/JS.
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // OAuth callbacks need to redirect to the provider unconstrained.
            if (path.Contains("/signin-") || path.Contains("/ExternalLogin"))
            {
                await _next(context);
                return;
            }

            // User-uploaded files: even if InputValidator/magic-byte checks miss
            // an HTML or SVG payload, this CSP+sandbox prevents it from running
            // scripts, submitting forms, or pulling resources from same origin.
            if (path.StartsWith("/uploads/"))
            {
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'; sandbox";
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
                await _next(context);
                return;
            }

            // First-party static assets (we control the bytes) skip the full CSP for perf.
            if (path.StartsWith("/lib/") || path.StartsWith("/css/") || path.StartsWith("/js/") ||
                path.StartsWith("/img/") || path.EndsWith(".ico"))
            {
                await _next(context);
                return;
            }

            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

            string permissionsPolicy = "geolocation=(), microphone=(self), camera=(self), payment=(), usb=(), magnetometer=(), accelerometer=(), gyroscope=()";
            context.Response.Headers.Append("Permissions-Policy", permissionsPolicy);

            string connectSrc = "'self' https://api.groq.com https://open.spotify.com https://www.youtube.com https://cdn.jsdelivr.net https://accounts.google.com https://image.pollinations.ai https://pollinations.ai https://api.airforce https://*.unsplash.com https://cdnjs.cloudflare.com";
            if (_environment.IsDevelopment())
            {
                connectSrc += " ws://localhost:* wss://localhost:* http://localhost:*";
            }

            string scriptSrc = "'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://code.jquery.com https://unpkg.com https://api.groq.com https://cdn.tailwindcss.com";
            if (_environment.IsDevelopment())
            {
                scriptSrc += " http://localhost:*";
            }

            string csp = "default-src 'self'; " +
                "frame-src 'self' https://open.spotify.com https://www.youtube.com https://www.google.com https://www.google.com.tr https://www.openstreetmap.org https://accounts.google.com; " +
                "script-src " + scriptSrc + "; " +
                "worker-src 'self' blob: https://cdn.jsdelivr.net https://unpkg.com; " +
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; " +
                "connect-src " + connectSrc + " https://kartistt.com.tr wss://kartistt.com.tr https://*.pollinations.ai https://*.bing.com https://unpkg.com https://*.imgly.com; " +
                "img-src 'self' data: blob: https://* http://* https://images.unsplash.com https://ui-avatars.com; " +
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






