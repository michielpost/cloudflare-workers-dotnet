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
        /// <remarks>
        /// Empty entries are dropped, so <c>""</c>, <c>" "</c> and a trailing
        /// comma all mean "no origins" rather than "one empty origin". Entries
        /// that are not an exact <c>http(s)</c> origin are dropped and logged at
        /// debug level instead of being passed on: <c>Cors.Allow</c> throws on a
        /// malformed origin, and that would take the whole worker down on every
        /// request rather than just denying one origin.
        /// </remarks>
        public static string[] ParseOrigins(string allowedOrigins)
        {
            if (allowedOrigins is null || allowedOrigins.Length == 0)
                return new string[0];

            var count = 0;
            var start = 0;
            for (var i = 0; i <= allowedOrigins.Length; i++)
            {
                if (i == allowedOrigins.Length || allowedOrigins[i] == ',')
                {
                    if (IsExactOrigin(allowedOrigins.Substring(start, i - start).Trim()))
                        count++;
                    start = i + 1;
                }
            }

            var origins = new string[count];
            var index = 0;
            start = 0;
            for (var i = 0; i <= allowedOrigins.Length; i++)
            {
                if (i == allowedOrigins.Length || allowedOrigins[i] == ',')
                {
                    var entry = allowedOrigins.Substring(start, i - start).Trim();
                    if (IsExactOrigin(entry))
                    {
                        origins[index] = entry;
                        index++;
                    }
                    else if (entry.Length != 0)
                    {
                        Log.Debug($"ALLOWED_ORIGINS entry \"{entry}\" is not an exact http(s) origin and is ignored.");
                    }
                    start = i + 1;
                }
            }

            return origins;
        }

        /// <summary>
        /// True when the value is something <c>Cors.Allow</c> accepts: <c>*</c>,
        /// or a scheme and host with no path, query, fragment or trailing slash.
        /// </summary>
        static bool IsExactOrigin(string value)
        {
            if (value.Length == 0)
                return false;

            if (value == "*")
                return true;

            var schemeLength = 0;
            if (value.StartsWith("https://"))
                schemeLength = 8;
            else if (value.StartsWith("http://"))
                schemeLength = 7;
            else
                return false;

            var host = value.Substring(schemeLength);
            if (host.Length == 0)
                return false;

            for (var i = 0; i < host.Length; i++)
            {
                var c = host[i];
                if (c == '/' || c == '?' || c == '#' || c == ' ')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The middleware used when the allowlist is empty. <c>Cors.Allow</c>
        /// requires at least one origin, and an empty allowlist means "no
        /// cross-origin access", so a preflight is refused and every other
        /// request is passed through untouched.
        /// </summary>
        public static Task<Response> RefusePreflightAsync(Request request, Func<Task<Response>> next)
        {
            if (request.Method == "OPTIONS" && IsCorsFetch(request))
                return Task.FromResult(Results.Empty(403));

            return next();
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
