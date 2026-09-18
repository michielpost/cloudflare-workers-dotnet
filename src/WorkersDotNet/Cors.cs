using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Cross-origin resource sharing helpers used by the worker entrypoint.
    /// </summary>
    public static class Cors
    {
        // Echo the caller's Origin so the Aspire frontend (dynamic port) is
        // allowed; fall back to "*" for requests without an Origin (e.g. curl).
        public static string AllowedOrigin(string? origin)
        {
            return origin is null || origin.Length == 0 ? "*" : origin;
        }

        public static Response Preflight(string? origin)
        {
            return Response.Empty(204)
                .WithHeader("access-control-allow-origin", AllowedOrigin(origin))
                .WithHeader("access-control-allow-methods", "GET, POST, OPTIONS")
                .WithHeader("access-control-allow-headers", "content-type");
        }

        public static Response Apply(Response response, string? origin)
        {
            return response
                .WithHeader("access-control-allow-origin", AllowedOrigin(origin))
                .WithHeader("access-control-expose-headers", "location");
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
