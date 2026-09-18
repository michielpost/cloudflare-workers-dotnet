using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Maps request paths to endpoint handlers. To add a URL, create an endpoint
    /// class with a <c>HandleAsync</c> method and add the route below.
    /// </summary>
    public static class Router
    {
        public static async Task<Response> HandleAsync(Request request, Env environment, Context context)
        {
            switch (request.Path)
            {
                case "/api/json":
                    return await JsonEndpoint.HandleAsync(request);
                case "/api/proxy":
                    return await ProxyEndpoint.HandleAsync(request);
                case "/api/redirect":
                    return await RedirectEndpoint.HandleAsync(request);
                case "/api/policy":
                    return await PolicyEndpoint.HandleAsync(request);
                case "/api/cache":
                    return await CacheEndpoint.HandleAsync(request, context);
                case "/api/kv":
                    return await KvEndpoint.HandleAsync(request, environment);
                case "/api/r2":
                    return await R2Endpoint.HandleAsync(request, environment);
                case "/api/r2/download":
                    return await R2Endpoint.DownloadAsync(request, environment);
                case "/api/r2/delete":
                    return await R2Endpoint.DeleteAsync(request, environment);
                case "/api/queue":
                    return await QueueEndpoint.HandleAsync(request, environment);
                case "/api/scheduled":
                    return await ScheduledEndpoint.HandleAsync(request, environment);
                default:
                    // Serve the BlazorWebApp as static files via the ASSETS binding.
                    return await AssetsEndpoint.HandleAsync(request, environment);
            }
        }
    }
}
