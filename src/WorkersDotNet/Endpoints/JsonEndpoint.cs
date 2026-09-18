using Workers;

namespace WorkersDotNet
{
    public static class JsonEndpoint
    {
        // JSON response using a shared model (also used by the Blazor frontend)
        public static Task<Response> HandleAsync(Request request)
        {
            Console.WriteLine($"Handling request for {request.Path}");

            return Task.FromResult(Results.Ok(
                "Hello from C# on Cloudflare Workers. 123",
                request.Path));
        }
    }
}
