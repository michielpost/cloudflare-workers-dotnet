using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class AssetsEndpoint
    {
        // Serve the BlazorWebApp as static files via the ASSETS binding.
        private readonly IFetcherBinding _assets;

        public AssetsEndpoint(IFetcherBinding assets)
        {
            _assets = assets;
        }

        public async Task<Response> HandleAsync(Request request)
        {
            return await _assets.FetchAsync(request);
        }
    }
}
