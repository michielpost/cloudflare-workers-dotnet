using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Cross-origin resource sharing helpers used by the worker entrypoint.
    /// </summary>
    /// <remarks>
    /// Allowed origins are read from the comma-separated <c>ALLOWED_ORIGINS</c>
    /// variable (see <c>[vars]</c> in wrangler.toml / wrangler.dev.toml), for
    /// example <c>"http://localhost:5217,https://app.example.com"</c>. There is
    /// no wildcard: an origin has to match the list, and an empty list grants no
    /// cross-origin access at all.
    /// </remarks>
    public static class Cors
    {
        /// <summary>Variable holding the comma-separated allowlist of origins.</summary>
        public const string AllowedOriginsVariable = "ALLOWED_ORIGINS";

        /// <summary>
        /// The request origin when it is on the allowlist, otherwise <c>null</c>
        /// (meaning "send no access-control-allow-origin header", which makes the
        /// browser block the cross-origin response).
        /// </summary>
        public static string? AllowedOrigin(string? origin, Env environment)
        {
            if (origin is null || origin.Length == 0)
                return null;

            if (!IsAllowed(origin, environment.Variable(AllowedOriginsVariable)))
                return null;

            return origin;
        }

        public static bool IsAllowed(string origin, string allowedOrigins)
        {
            if (allowedOrigins is null || allowedOrigins.Length == 0)
                return false;

            // The configured list is comma-separated and anchored: an origin has
            // to match one entry exactly (dots are literal, spaces are ignored).
            var start = 0;
            for (var i = 0; i <= allowedOrigins.Length; i++)
            {
                if (i == allowedOrigins.Length || allowedOrigins[i] == ',')
                {
                    if (origin == allowedOrigins.Substring(start, i - start).Trim())
                        return true;
                    start = i + 1;
                }
            }

            return false;
        }

        public static Response Preflight(string? origin, Env environment)
        {
            var response = Response.Empty(204)
                .WithHeader("access-control-allow-methods", "GET, POST, OPTIONS")
                .WithHeader("access-control-allow-headers", "content-type, authorization");

            var allowed = AllowedOrigin(origin, environment);
            if (allowed is null)
                return response;

            return response.WithHeader("access-control-allow-origin", allowed);
        }

        public static Response Apply(Response response, string? origin, Env environment)
        {
            // None of these are CORS-safelisted response headers, so they have to
            // be exposed explicitly to be readable from JavaScript: "location" for
            // the redirect endpoint and the sample headers that report where a
            // response came from (cache hit/miss, the stored R2 object).
            var withExposed = response.WithHeader(
                "access-control-expose-headers",
                "location, x-cache, x-cache-key, x-cache-note, etag, x-r2-bucket, x-r2-key");

            var allowed = AllowedOrigin(origin, environment);
            if (allowed is null)
                return withExposed;

            return withExposed.WithHeader("access-control-allow-origin", allowed);
        }

        /// <summary>
        /// True when the request comes from a browser cross-origin <c>fetch</c>,
        /// which always sends an <c>Origin</c> header.
        /// </summary>
        public static bool IsCorsFetch(Request request)
        {
            var origin = request.Headers.Get("origin");
            if (origin is null || origin.Length == 0)
                return false;
            return true;
        }
    }
}
