using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Maps request paths to the endpoints it was handed at construction. To add
    /// a URL, create an endpoint class with a <c>HandleAsync</c> method and add
    /// the route below. The endpoints come from <see cref="AppServices"/>.
    /// </summary>
    public sealed class Router
    {
        private readonly AppServices _app;

        public Router(AppServices app)
        {
            _app = app;
        }

        public async Task<Response> HandleAsync(Request request, Context context)
        {
            switch (request.Path)
            {
                case "/api/json":
                    return await _app.Json.HandleAsync(request);
                case "/api/proxy":
                    return await _app.Proxy.HandleAsync(request);
                case "/api/redirect":
                    return await _app.Redirect.HandleAsync(request);
                case "/api/policy":
                    return await _app.Policy.HandleAsync(request);
                case "/api/cache":
                    return await _app.Cache.HandleAsync(request, context);
                case "/api/kv":
                    return await _app.Kv.HandleAsync(request);
                case "/api/r2":
                    return await _app.R2.HandleAsync(request);
                case "/api/r2/download":
                    return await _app.R2.DownloadAsync(request);
                case "/api/r2/delete":
                    return await _app.R2.DeleteAsync(request);
                case "/api/queue":
                    return await _app.Queue.HandleAsync(request);
                case "/api/scheduled":
                    return await _app.Scheduled.HandleAsync(request);
                case "/api/items":
                    return await _app.Items.HandleAsync(request);
                case "/api/items/update":
                    return await _app.Items.UpdateAsync(request);
                case "/api/items/delete":
                    return await _app.Items.DeleteAsync(request);
                case "/api/auth/register":
                    return await _app.Auth.RegisterAsync(request);
                case "/api/auth/login":
                    return await _app.Auth.LoginAsync(request);
                case "/api/auth/logout":
                    return await _app.Auth.LogoutAsync(request);
                case "/api/auth/me":
                    return await _app.Auth.MeAsync(request);
                case "/api/auth/profile":
                    return await _app.Auth.UpdateProfileAsync(request);
                case "/api/auth/roles":
                    return await _app.Auth.AssignRolesAsync(request);
                case "/api/auth/users":
                    return await _app.Auth.UsersAsync(request);
                case "/api/telemetry":
                    return await _app.Telemetry.HandleAsync(request);
                case "/api/telemetry/run":
                    return await _app.Telemetry.RunAsync();
                case "/api/telemetry/reset":
                    return await _app.Telemetry.ResetAsync();
                default:
                    // Serve the BlazorWebApp as static files via the ASSETS binding.
                    return await _app.Assets.HandleAsync(request);
            }
        }
    }
}
