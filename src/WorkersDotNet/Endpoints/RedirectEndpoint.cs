using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class RedirectEndpoint
    {
        public Task<Response> HandleAsync(Request request)
        {
            var location = new Url(request.Url).SearchParams.Get("to") ?? "https://example.com";

            // A browser fetch cannot follow a 302 to another origin unless that
            // origin opts into CORS, so hand the target back instead. Top-level
            // navigations (no Origin header) still get a real redirect.
            if (CorsPolicy.IsCorsFetch(request))
            {
                var response = Results.Empty(200);
                response.Headers.Set("location", location);
                return Task.FromResult(response);
            }

            return Task.FromResult(Response.Redirect(location, 302));
        }
    }
}
