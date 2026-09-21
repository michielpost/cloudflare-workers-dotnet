using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class RedirectEndpoint
    {
        public Task<Response> HandleAsync(Request request)
        {
            var location = request.QueryParameters.Get("to") ?? "https://example.com";

            // A browser fetch cannot follow a 302 to another origin unless that
            // origin opts into CORS, so hand the target back instead. Top-level
            // navigations (no Origin header) still get a real redirect.
            if (Cors.IsCorsFetch(request))
                return Task.FromResult(Response.Empty(200).WithHeader("location", location));

            return Task.FromResult(Response.Redirect(location, 302));
        }
    }
}
