using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public static class Worker
    {
        [Fetch]
        public static async Task<Response> FetchAsync(
            Request request,
            Env environment,
            ExecutionContext context)
        {
            var app = new AppServices(environment);

            // CORS (including the browser preflight) is a router middleware, so
            // every response - including the error ones - carries the headers.
            return await app.Routes.HandleAsync(request, environment, context);
        }
    }
}
