var builder = DistributedApplication.CreateBuilder(args);

// Blazor WebAssembly frontend. Runs on its own port via the WASM dev server.
// It reads the worker API base URL from wwwroot/appsettings.Development.json
// (separate local dev port) or wwwroot/appsettings.json (same domain when
// deployed), so no env var is injected here.
builder.AddProject<Projects.BlazorWebApp>("blazor");

// Cloudflare Worker: the C# source in src/WorkersDotNet is compiled to
// src/WorkersDotNet/dist/worker.js (see cloudflarebuild.sh + [build] in
// wrangler.toml) and served by Wrangler's dev server on port 8787.
// Local dev uses wrangler.dev.toml (no [build] step) so the pre-built dist is
// served without wrangler triggering an infinite dotnet rebuild loop.
builder.AddExecutable(
        name: "cloudflare-worker",
        command: "npx",
        // wrangler.toml lives at the repo root; this path is relative to the
        // AppHost project directory (src/Aspire/Aspire.AppHost).
        workingDirectory: "../../..",
        args: new[] { "wrangler", "dev", "-c", "wrangler.dev.toml", "--port", "8787" })
    .WithEnvironment("ENVIRONMENT", "Development");

builder.Build().Run();
