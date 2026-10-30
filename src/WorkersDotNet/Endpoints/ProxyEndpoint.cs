using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: the worker fetches a remote URL itself and tags the result.
    /// </summary>
    /// <remarks>
    /// The response from <c>Http.FetchAsync</c> has immutable headers, so the tag
    /// cannot be added to it directly; <see cref="Results.Copy"/> rebuilds it
    /// around a mutable copy of the headers.
    /// </remarks>
    public sealed class ProxyEndpoint
    {
        const string DefaultTarget = "https://example.com";

        public async Task<Response> HandleAsync(Request request)
        {
            var target = new Url(request.Url).SearchParams.Get("url");
            if (target is null || target.Length == 0)
                target = DefaultTarget;

            if (!IsHttpUrl(target))
                return Results.Error("The url parameter must be an absolute http(s) URL", 400);

            Response proxied;
            try
            {
                proxied = await Http.FetchAsync(target);
            }
            catch (System.Exception error)
            {
                return Results.Error("Could not fetch " + target + ": " + error.Message, 502);
            }

            var headers = new Headers(proxied.Headers);
            headers.Set("x-proxied-by", "Workers");

            return Results.Copy(proxied, headers);
        }

        static bool IsHttpUrl(string value)
            => value.StartsWith("https://") || value.StartsWith("http://");
    }
}
