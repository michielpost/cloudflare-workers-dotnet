using BlazorWebApp;
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
    services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(hostEnvironment.BaseAddress) });
    services.AddScoped<BlazorWebApp.Services.WorkerApi>();
}
