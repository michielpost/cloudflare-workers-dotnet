using System.Text.RegularExpressions;
using Shared;
using Workers;

namespace WorkersDotNet
{
    public static class Worker
    {
        [Fetch]
        public static async Task<Response> FetchAsync(
            Request request,
            Env environment,
            Context context)
        {
            var path = request.Path;

            // JSON response using a shared model (also used by the Blazor frontend)
            if (path == "/api/json")
                return Response.Json(new ApiResponse(
                    true,
                    "Hello from C# on Cloudflare Workers.",
                    path,
                    DateTimeOffset.UtcNow));

            // Proxy fetch
            if (path == "/api/proxy")
            {
                var target = request.QueryParameters.Get("url") ?? "https://example.com";
                var proxied = await Http.FetchAsync(target);
                return proxied.WithHeader("x-proxied-by", "Workers");
            }

            // Redirect
            if (path == "/api/redirect")
            {
                var location = request.QueryParameters.Get("to") ?? "https://example.com";
                return Response.Redirect(location, status: 302);
            }

            // Request policy: only accepts a validated JSON POST with a bounded body
            if (path == "/api/policy")
                return await HandlePolicyAsync(request);

            // Serve the BlazorWebApp as static files via the ASSETS binding.
            return await environment.Assets("ASSETS").FetchAsync(request);
        }

        private static async Task<Response> HandlePolicyAsync(Request request)
        {
            const int maxBodyBytes = 4096;

            var methods = new HashSet<string> { "POST" };
            if (!methods.Contains(request.Method))
                return Error("Method not allowed", 405).WithHeader("allow", "POST");

            if (!HasJsonBody(request, maxBodyBytes))
                return Error("A bounded JSON body is required", 400);

            ReadingInput? input;
            try
            {
                input = await request.JsonAsync<ReadingInput>();
            }
            catch (Exception)
            {
                return Error("Malformed JSON", 400);
            }

            if (!IsValid(input))
                return Error("Invalid reading", 400);

            return Response.Json(new ApiResponse(
                true,
                "Reading accepted",
                request.Path,
                DateTimeOffset.UtcNow));
        }

        private static bool HasJsonBody(Request request, int maxBodyBytes)
        {
            var contentType = request.Headers.Get("content-type");
            var contentLength = request.Headers.Get("content-length");

            if (contentType is null || !contentType.StartsWith("application/json") || contentLength is null)
                return false;

            if (!Regex.IsMatch(contentLength, "^[1-9][0-9]{0,3}$"))
                return false;

            return int.Parse(contentLength) <= maxBodyBytes
                && request.BodyStream() is not null
                && !request.BodyUsed;
        }

        private static bool IsValid(ReadingInput? input)
        {
            if (input is null || input.DeviceId.Length == 0)
                return false;
            if (input.Value < -100 || input.Value > 1000 || input.Tags.Count > 8)
                return false;

            var tags = new HashSet<string>();
            foreach (var tag in input.Tags)
                if (tag.Length == 0 || tag.Length > 24 || !tags.Add(tag))
                    return false;
            return true;
        }

        private static Response Error(string message, int status) =>
            Response.Json(new { error = message, requestId = Guid.NewGuid() }, status);
    }
}
