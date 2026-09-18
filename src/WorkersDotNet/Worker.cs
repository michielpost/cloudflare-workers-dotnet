using Workers;

namespace WorkersDotNet
{
    public static class Worker
    {
        [Fetch]
        public static async Task<Response> FetchAsync(
            Request request,
            Env environment,
            Context context)
        {
            var origin = request.Headers.Get("origin");

            // Browser CORS preflight for cross-origin calls from the frontend
            // (e.g. the Blazor app served by Aspire on a separate port).
            if (request.Method == "OPTIONS")
                return Cors.Preflight(origin, environment);

            var response = await Router.HandleAsync(request, environment, context);

            // Allow the originating frontend domain (when it is on the
            // ALLOWED_ORIGINS list) to read the response.
            return Cors.Apply(response, origin, environment);
        }
    }
}
