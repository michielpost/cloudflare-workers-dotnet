using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Maps request paths to endpoint handlers. To add a URL, create an endpoint
    /// class with a <c>HandleAsync</c> method and add the route below.
    /// </summary>
    public static class Router
    {
        public static async Task<Response> HandleAsync(Request request, Env environment)
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
                default:
                    // Serve the BlazorWebApp as static files via the ASSETS binding.
                    return await AssetsEndpoint.HandleAsync(request, environment);
            }
        }
    }
}
