using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// The worker's cross-origin policy, expressed as the <c>Workers.Cors</c>
    /// middleware that <see cref="AppServices"/> registers on the router.
    /// </summary>
    /// <remarks>
    /// Allowed origins are read from the comma-separated <c>ALLOWED_ORIGINS</c>
    /// variable (see <c>[vars]</c> in wrangler.toml / wrangler.dev.toml), for
    /// example <c>"http://localhost:5217,https://app.example.com"</c>. There is
    /// no wildcard: an origin has to match the list, and an empty list grants no
    /// cross-origin access at all.
    /// </remarks>
    public static class CorsPolicy
    {
        /// <summary>Variable holding the comma-separated allowlist of origins.</summary>
        public const string AllowedOriginsVariable = "ALLOWED_ORIGINS";

        /// <summary>How long a browser may cache the preflight answer, in seconds.</summary>
        public const int MaxAgeSeconds = 86400;

        public static string[] Methods()
        {
            return ["GET", "POST", "OPTIONS"];
        }

        public static string[] RequestHeaders()
        {
            return ["content-type", "authorization"];
        }

        /// <summary>
        /// None of these are CORS-safelisted response headers, so they have to be
        /// exposed explicitly to be readable from JavaScript: "location" for the
        /// redirect endpoint and the sample headers that report where a response
        /// came from (cache hit/miss, the stored R2 object).
        /// </summary>
        public static string[] ExposedHeaders()
        {
            return ["location", "x-cache", "x-cache-key", "x-cache-note", "etag", "x-r2-bucket", "x-r2-key"];
        }

        /// <summary>
        /// Splits the comma-separated allowlist into the array the middleware
        /// takes. Written out by hand because the worker has no
        /// <c>string.Split</c> (the compiler only supports a fixed set of string
        /// methods).
        /// </summary>
        public static string[] ParseOrigins(string allowedOrigins)
        {
            if (allowedOrigins is null || allowedOrigins.Length == 0)
                return new string[0];

            var count = 1;
            for (var i = 0; i < allowedOrigins.Length; i++)
            {
                if (allowedOrigins[i] == ',')
                    count++;
            }

            var origins = new string[count];
            var index = 0;
            var start = 0;
            for (var i = 0; i <= allowedOrigins.Length; i++)
            {
                if (i == allowedOrigins.Length || allowedOrigins[i] == ',')
                {
                    origins[index] = allowedOrigins.Substring(start, i - start).Trim();
                    index++;
                    start = i + 1;
                }
            }

            return origins;
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
