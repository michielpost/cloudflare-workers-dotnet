# Cloudflare Workers .NET

Write your Cloudflare Worker in **C#**, ship it as JavaScript.

This project compiles C# directly to a Cloudflare Worker module — no interpreter and no .NET
runtime on the edge. The worker is authored in plain C# (`net10.0`), compiled to
`src/WorkersDotNet/dist/worker.js` at build time, and served by Wrangler. A **Blazor WebAssembly**
frontend calls the worker's API and doubles as a live demo of every endpoint.

```mermaid
flowchart LR
    A[Blazor WebAssembly app] -->|fetch, cross origin| B[Cloudflare Worker written in C#]
    B --> C[ASSETS binding]
    C --> D[Static Blazor files]
    B --> E[Remote HTTP APIs]
```

## Highlights

- **C# on the edge** — worker logic is ordinary C#: `async` methods, records, pattern matching,
  `System.Text.RegularExpressions`, all compiled ahead of time to a single JS module.
- **ASP.NET-style routing** — a central `switch` router maps URLs to endpoint classes, so a new
  route is one small class plus one `case`.
- **First-class response helpers** — `Results.Ok(...)` / `Results.Error(...)` make returning JSON a
  one-liner, the main use case for an API worker.
- **A dedicated CORS helper** — preflight and response headers handled in one place.
- **Shared models** — the same `ApiResponse` record is used by the worker and the Blazor client.
- **One-command local dev** — .NET Aspire starts the frontend and the worker together.
- **Wrangler for deploy** — the standard Cloudflare workflow, unchanged.

## Project layout

```
src/
  WorkersDotNet/            The Cloudflare Worker, written in C#
    Worker.cs               Entry point: the [Fetch] handler
    Router.cs               URL -> endpoint mapping (add new URLs here)
    Results.cs              JSON response helpers
    Cors.cs                 CORS policy helpers
    Endpoints/              One class per route
      JsonEndpoint.cs       GET  /api/json
      ProxyEndpoint.cs      GET  /api/proxy
      RedirectEndpoint.cs   GET  /api/redirect
      PolicyEndpoint.cs     POST /api/policy
      AssetsEndpoint.cs     Fallback: serves the Blazor app via the ASSETS binding
  BlazorWebApp/             Blazor WebAssembly frontend that calls the worker
  Shared/Models.cs          Records shared by the worker and the frontend
  Aspire/Aspire.AppHost/    Aspire host that orchestrates local development
wrangler.toml               Deploy config, with the [build] step
wrangler.dev.toml           Local dev config, without the [build] step
cloudflarebuild.sh          CI/deploy build: publishes Blazor assets, then the worker
```

## API

| Method | Path                 | Description                                                                       |
| ------ | -------------------- | --------------------------------------------------------------------------------- |
| `GET`  | `/api/json`          | Returns a JSON `ApiResponse` — the JSON helper in action                           |
| `GET`  | `/api/proxy?url=`    | Fetches a remote URL server-side and tags the response with `x-proxied-by: Workers` |
| `GET`  | `/api/redirect?to=`  | Redirects to the given URL; cross-origin `fetch` callers receive the target in `location`       |
| `POST` | `/api/policy`        | Accepts a bounded JSON `ReadingInput` body and validates it                        |
| `*`    | anything else        | Falls through to the `ASSETS` binding, serving the Blazor app                      |

`OPTIONS` requests are answered by the CORS preflight helper for every path, but only advertised to
origins on the allow-list (see [CORS](#cors)).

## Adding a URL

Adding an endpoint takes two small steps.

**1. Create the endpoint** in `src/WorkersDotNet/Endpoints/`:

```csharp
using Workers;

namespace WorkersDotNet
{
    public static class HelloEndpoint
    {
        public static Task<Response> HandleAsync(Request request)
        {
            return Task.FromResult(Results.Ok("Hello!", request.Path));
        }
    }
}
```

**2. Register it** in `Router.cs`:

```csharp
case "/api/hello":
    return await HelloEndpoint.HandleAsync(request);
```

That is all, the route is now live, CORS headers included for allowed origins.

## Response helpers

`Results` keeps endpoint bodies short and consistent:

```csharp
return Results.Ok("Reading accepted", request.Path);              // 200 + ApiResponse
return Results.Error("Method not allowed", 405);                  // error body with a requestId
return Results.Json(new ApiResponse(...), 201);                   // explicit status code
```

`Results.Error` returns `{ "error": "...", "requestId": "..." }`, which makes failures easy to
correlate with logs.

## CORS

Cross-origin access is an explicit allow-list; there is no wildcard. Allowed origins come from the
comma-separated `ALLOWED_ORIGINS` variable:

```toml
[vars]
ALLOWED_ORIGINS = "http://localhost:5217,https://localhost:7000,https://app.example.com"
```

The list is compiled into an anchored regular expression (`^(?:a|b)$`), so an origin has to match an
entry exactly and an empty or missing list grants no cross-origin access at all. `Cors.Apply` and
`Cors.Preflight` only emit `access-control-allow-origin` when `Cors.AllowedOrigin` accepts the
requesting origin, which means everything else is left to the browser to block.

Where the value is set:

| File             | Value                                     | Why                                                                                                                                          |
| ---------------- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `wrangler.dev.toml` | `http://localhost:5217,https://localhost:7000` | Under Aspire the Blazor frontend runs on its own port (`src/BlazorWebApp/Properties/launchSettings.json`) while the worker listens on `:8787`, so those calls are cross-origin |
| `wrangler.toml`  | empty                                     | The deployed Blazor app is served by this same worker through the `[assets]` binding (`ApiBaseUrl` is empty in `appsettings.json`), so its calls are same-origin and need no entry |

Add origins to `wrangler.toml` only when the frontend is hosted somewhere else, such as Cloudflare
Pages or a custom domain. Spaces around entries are ignored.

> Browsing the frontend as `127.0.0.1` instead of `localhost` produces a different origin. Add
> `http://127.0.0.1:5217` to the list if you prefer that host name.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (for Wrangler)
- `npm install` at the repo root, to restore Wrangler

## Running locally

### With Aspire (recommended)

```bash
dotnet run --project src/Aspire/Aspire.AppHost
```

This starts the Blazor frontend and the worker, and opens the Aspire dashboard where you can follow
logs and endpoints for both.

### Without Aspire

Run the worker on its own — this publishes the C# worker and then starts Wrangler on port `8787`:

```bash
npm run worker:dev
```

Then run the frontend, which reads its API base URL from
`src/BlazorWebApp/wwwroot/appsettings.Development.json` (`http://localhost:8787`):

```bash
dotnet run --project src/BlazorWebApp
```

## Building

Publish the worker on its own:

```bash
dotnet publish src/WorkersDotNet -c Release
```

This writes `src/WorkersDotNet/dist/worker.js`, which is the file `wrangler.toml` points at.

To produce the complete deployable output (Blazor static files **and** the worker):

```bash
sh cloudflarebuild.sh
```

The script publishes the Blazor app, copies its `wwwroot` into `src/WorkersDotNet/dist/wwwroot/`,
then publishes the worker. Wrangler serves that directory through the `ASSETS` binding.

## Deploying

Deployment uses the standard Cloudflare workflow. `wrangler.toml` declares a `[build]` step that
runs `cloudflarebuild.sh`, so Cloudflare builds the C# for you:

```bash
npx wrangler deploy
```

## Conventions and compiler notes

The `Workers` compiler is a focused source-to-JavaScript emitter rather than a full .NET runtime.
It supports the common C# you would expect (`switch` statements, `const string` case labels, static
helper classes, anonymous objects, records, `foreach`, LINQ-free collection types, `Regex`,
`DateTimeOffset`, `Guid`), but a few constructs are intentionally out of scope.

Practical consequences for this codebase:

- **Concrete parameter types only.** The compiler resolves method symbols against a supported
  whitelist, so helpers such as `Results` use overloads instead of a generic `Json<T>()`, and
  interfaces, delegates and `object` parameters are avoided.
- **Static members.** Endpoints are static classes with a static `HandleAsync`, resolved at compile
  time. This is why routing uses a `switch` rather than a delegate registry.
- **Keep the router thin.** All logic lives in the endpoint classes, so the router stays a readable
  index of the worker's URLs.
- **`dist/` is generated.** It is gitignored; never edit it by hand.

## Credits

This project builds on the excellent **Workers** package and sample collection by
**[Iñigo Ruiz-Salinas](https://github.com/iruizsalinas)** — **<https://github.com/iruizsalinas/workers>**.

That repository provides the C#-to-Cloudflare-Workers compiler and the samples that made this
approach possible. The NuGet package `Workers` (`0.3.0`) used under `src/WorkersDotNet` comes from
there, and its description sums it up well: _"Compile a focused C# profile directly to minimal
Cloudflare Workers JavaScript."_ If you want to understand how C# is compiled for the edge, or see
many more worked examples than the handful here, start there.

The endpoint and helper structure in `src/WorkersDotNet` is a refactor of those samples into a
small, ASP.NET-style layout. Many thanks to the author for making C# on Workers possible.

## License

[MIT](LICENSE) — Copyright (c) 2026 Michiel Post.