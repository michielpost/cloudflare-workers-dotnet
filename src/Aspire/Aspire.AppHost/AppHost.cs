var builder = DistributedApplication.CreateBuilder(args);

// Port the Blazor frontend is served on in local development. It is pinned here
// rather than left to the launch profile, because Aspire starts the project with
// --no-launch-profile: the applicationUrl in
// src/BlazorWebApp/Properties/launchSettings.json is ignored and the WASM dev
// server would bind a random free port. That port then has to be added to the
// worker's CORS allowlist, which is why the frontend must always come up on one
// known origin - see ALLOWED_ORIGINS in wrangler.dev.toml, which lists this port
// explicitly (there is no wildcard). isProxied: false makes the dev server bind
// the port itself, so the browser talks to the app directly and sends exactly
// this origin.
const int FrontendPort = 5217;

// Blazor WebAssembly frontend. It reads the worker API base URL from
// wwwroot/appsettings.Development.json (separate local dev port) or
// wwwroot/appsettings.json (same domain when deployed), so no env var is
// injected here.
builder.AddProject<Projects.BlazorWebApp>("blazor")
    .WithHttpEndpoint(port: FrontendPort, targetPort: FrontendPort, name: "http", isProxied: false);

// Cloudflare Worker: the C# source in src/WorkersDotNet is compiled to
// src/WorkersDotNet/dist/worker.js (see cloudflarebuild.sh + [build] in
// wrangler.toml) and served by Wrangler's dev server on port 8787.
// Local dev runs the "worker:dev" npm script, which publishes WorkersDotNet
// (so the served worker.js is always up to date) and then starts Wrangler
// with wrangler.dev.toml (no [build] step, avoiding an infinite rebuild loop).
builder.AddExecutable(
        name: "cloudflare-worker",
        command: "npm",
        // wrangler.toml and package.json live at the repo root; this path is
        // relative to the AppHost project directory (src/Aspire/Aspire.AppHost).
        workingDirectory: "../../..",
        args: new[] { "run", "worker:dev" })
    .WithEnvironment("ENVIRONMENT", "Development");

builder.Build().Run();
