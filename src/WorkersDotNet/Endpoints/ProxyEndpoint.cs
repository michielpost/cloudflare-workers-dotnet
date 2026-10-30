using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class ProxyEndpoint
    {
        public async Task<Response> HandleAsync(Request request)
        {
            var target = new Url(request.Url).SearchParams.Get("url") ?? "https://example.com";
            var proxied = await Http.FetchAsync(target);
            proxied.Headers.Set("x-proxied-by", "Workers");
            return proxied;
        }
    }
}
