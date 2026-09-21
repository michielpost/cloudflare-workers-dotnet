using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class ProxyEndpoint
    {
        public async Task<Response> HandleAsync(Request request)
        {
            var target = request.QueryParameters.Get("url") ?? "https://example.com";
            var proxied = await Http.FetchAsync(target);
            return proxied.WithHeader("x-proxied-by", "Workers");
        }
    }
}
