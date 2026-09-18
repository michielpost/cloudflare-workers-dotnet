var builder = DistributedApplication.CreateBuilder(args);

// Blazor WebAssembly frontend. Runs on its own port via the WASM dev server.
// It reads the worker API base URL from wwwroot/appsettings.Development.json
// (separate local dev port) or wwwroot/appsettings.json (same domain when
// deployed), so no env var is injected here.
builder.AddProject<Projects.BlazorWebApp>("blazor");

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
