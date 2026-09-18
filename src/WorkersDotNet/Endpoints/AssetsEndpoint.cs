using Workers;

namespace WorkersDotNet
{
    public static class AssetsEndpoint
    {
        // Serve the BlazorWebApp as static files via the ASSETS binding.
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            return await environment.Assets("ASSETS").FetchAsync(request);
        }
    }
}
