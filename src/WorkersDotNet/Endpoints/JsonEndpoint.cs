using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class JsonEndpoint
    {
        // JSON response using a shared model (also used by the Blazor frontend)
        public Task<Response> HandleAsync(Request request)
        {
            Console.WriteLine($"Handling request for {request.Path}");

            return Task.FromResult(Results.Ok(
                "Hello from C# on Cloudflare Workers. 456",
                request.Path));
        }
    }
}
