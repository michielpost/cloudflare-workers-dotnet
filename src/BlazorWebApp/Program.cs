using BlazorWebApp;
using BlazorWebApp.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

ConfigureServices(builder.Services, builder.HostEnvironment);

await builder.Build().RunAsync();

// Extracted into a static local function so the BlazorWasmPreRendering.Build
// prerendering WebHost can call it too and resolve the same services.
static void ConfigureServices(IServiceCollection services, IWebAssemblyHostEnvironment hostEnvironment)
{
    services.AddScoped<AuthTokenStore>();
    services.AddTransient<AuthTokenHandler>();

    // The shared HttpClient carries the worker bearer token (when signed in) via
    // the AuthTokenHandler, so every request to the worker authenticates.
    services.AddScoped(sp => new HttpClient(sp.GetRequiredService<AuthTokenHandler>())
    {
        BaseAddress = new Uri(hostEnvironment.BaseAddress)
    });

    services.AddScoped<WorkerApi>();
    services.AddScoped<AuthService>();

    // Register the state provider both as its concrete type (so AuthService can
    // inject it and call NotifySignIn/NotifySignOut) and as the abstract
    // AuthenticationStateProvider the framework resolves — the factory keeps them
    // as one shared scoped instance.
    services.AddScoped<WorkerAuthStateProvider>();
    services.AddScoped<AuthenticationStateProvider>(
        sp => sp.GetRequiredService<WorkerAuthStateProvider>());

    // Native Blazor WASM auth: authorisation state, the cascading auth state and
    // the AuthorizeView / AuthorizeRouteView components. The role claims come
    // from the worker via /api/auth/me.
    services.AddAuthorizationCore();
    services.AddCascadingAuthenticationState();
}
