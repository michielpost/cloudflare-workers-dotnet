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
            // Server-side logic: handle API routes here.
            if (request.Path.StartsWith("/api/"))
            {
                return Response.Json(new { message = "Hello from C# on Cloudflare Workers.", path = request.Path });
            }

            // Serve the BlazorWebApp as static files via the ASSETS binding.
            // not_found_handling = "single-page-application" in wrangler.toml
            // makes unknown asset paths fall back to index.html for SPA routing.
            return await environment.Assets("ASSETS").FetchAsync(request);
        }
    }
}
