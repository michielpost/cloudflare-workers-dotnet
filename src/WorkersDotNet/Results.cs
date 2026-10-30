using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Helpers for returning JSON responses, the main response shape used by
    /// the API endpoints.
    /// </summary>
    /// <remarks>
    /// The Workers compiler only supports concrete parameter types, so these
    /// helpers expose overloads rather than a generic <c>Json&lt;T&gt;</c>.
    /// </remarks>
    public static class Results
    {
        public static Response Ok(ApiResponse value)
        {
            return Response.Json(value, 200);
        }

        public static Response Ok(string message, string path)
        {
            return Response.Json(new ApiResponse(true, message, path, Clock.NowIso()), 200);
        }

        public static Response Json(ApiResponse value, int status)
        {
            return Response.Json(value, status);
        }

        public static Response Error(string message, int status)
        {
            return Response.Json(new { error = message, requestId = Crypto.RandomUuid() }, status);
        }

        /// <summary>
        /// An empty response with the given status code. Replaces the removed
        /// <c>Response.Empty</c> helper: a <c>Response</c> is now also the
        /// <c>ResponseInit</c> of <c>new Response(body, init)</c>.
        /// </summary>
        public static Response Empty(int status)
        {
            var init = new Response(null) { Status = status };
            return new Response(null, init);
        }

        /// <summary>
        /// Marks a response as uncacheable and returns it. Every API response
        /// uses this so the browser never serves its own copy.
        /// </summary>
        public static Response NoStore(Response response)
        {
            response.Headers.Set("cache-control", "no-store");
            return response;
        }

        /// <summary>
        /// Rebuilds a response that came from the runtime - <c>fetch</c> or the
        /// Cache API - around a mutable copy of its headers.
        /// </summary>
        /// <remarks>
        /// Those responses have <em>immutable</em> headers, so
        /// <c>response.Headers.Set(...)</c> throws "Can't modify immutable
        /// headers". The body is handed to a new response instead, which is the
        /// only way to add a header to a proxied or cached response.
        /// </remarks>
        public static Response Copy(Response source, Headers headers)
        {
            var init = new Response(null)
            {
                Status = source.Status,
                StatusText = source.StatusText,
                Headers = headers
            };

            // A 204 or 304 must not carry a body, and the runtime rejects one.
            if (source.Status == 204 || source.Status == 304)
                return new Response(null, init);

            return new Response(source.Body, init);
        }
    }
}
