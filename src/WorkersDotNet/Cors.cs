using System.Text.RegularExpressions;
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

            return Regex.IsMatch(origin, Pattern(allowedOrigins));
        }

        /// <summary>
        /// Turns the configured list into an anchored regular expression, e.g.
        /// <c>"http://localhost:5217,https://a.com"</c> becomes
        /// <c>^(?:http://localhost:5217|https://a\.com)$</c>.
        /// </summary>
        static string Pattern(string allowedOrigins)
        {
            // Spaces around the entries are insignificant, and dots have to be
            // escaped so they match a literal "." instead of any character.
            var alternation = allowedOrigins.Replace(" ", "");
            alternation = alternation.Replace(".", "\\.");
            alternation = alternation.Replace(",", "|");

            return $"^(?:{alternation})$";
        }

        public static Response Preflight(string? origin, Env environment)
        {
            var response = Response.Empty(204)
                .WithHeader("access-control-allow-methods", "GET, POST, OPTIONS")
                .WithHeader("access-control-allow-headers", "content-type");

            var allowed = AllowedOrigin(origin, environment);
            if (allowed is null)
                return response;

            return response.WithHeader("access-control-allow-origin", allowed);
        }

        public static Response Apply(Response response, string? origin, Env environment)
        {
            // "location" is not a CORS-safelisted response header, so it has to
            // be exposed explicitly for the redirect endpoint to be readable.
            var withExposed = response.WithHeader("access-control-expose-headers", "location");

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
