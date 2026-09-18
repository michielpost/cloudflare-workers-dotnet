using Workers;

namespace WorkersDotNet
{
    public static class ProxyEndpoint
    {
        public static async Task<Response> HandleAsync(Request request)
        {
            var target = request.QueryParameters.Get("url") ?? "https://example.com";
            var proxied = await Http.FetchAsync(target);
            return proxied.WithHeader("x-proxied-by", "Workers");
        }
    }
}
