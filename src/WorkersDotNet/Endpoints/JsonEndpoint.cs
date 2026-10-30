using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class JsonEndpoint
    {
        // JSON response using a shared model (also used by the Blazor frontend)
        public Task<Response> HandleAsync(Request request)
        {
            var path = new Url(request.Url).Pathname;

            Log.Info($"Handling request for {path}");

            return Task.FromResult(Results.Ok(
                "Hello from C# on Cloudflare Workers. 456",
                path));
        }
    }
}
