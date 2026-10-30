# Cloudflare Workers .NET Sample

Write your Cloudflare Worker in **C#**, ship it as JavaScript.

This project compiles C# directly to a Cloudflare Worker module — no interpreter and no .NET
runtime on the edge. The worker is authored in plain C# (`net10.0`), compiled to
`src/WorkersDotNet/dist/worker.js` at build time, and served by Wrangler. A **Blazor WebAssembly**
frontend calls the worker's API and doubles as a live demo of every endpoint.

It uses the **[Workers](https://github.com/iruizsalinas/workers)**  NuGet package.

## 🌐 Live demo

**[→ Open the live demo](https://cloudflare-workers-dotnet.mailpost.workers.dev/)** — a deployed
instance of this project. The Blazor frontend runs against the real Cloudflare worker, so every
sample (JSON, proxy, cache, KV, R2, queue, scheduled task, D1 CRUD and the weather sensor pipeline)
is live and clickable.


## Highlights

- **C# on the edge** — worker logic is ordinary C#: `async` methods, records, pattern matching,
  generics, all compiled ahead of time to a single JS module.
- **ASP.NET-style routing** — the package's `Router` maps URLs to endpoint classes, so a new route
  is one small class plus one line in the route table.
- **First-class response helpers** — `Results.Ok(...)` / `Results.Error(...)` make returning JSON a
  one-liner, the main use case for an API worker.
- **CORS as middleware** — the allow-list is one `Cors.Allow(...)` registration on the router, so
  preflights and response headers are handled in one place.
- **Shared models** — the same `ApiResponse` record is used by the worker and the Blazor client.
- **Every binding, worked through** — Cache API, KV, R2, D1, queue producer *and* consumer, a
  Durable Object, and scheduled triggers, all runnable locally with no Cloudflare account.
- **An end-to-end pipeline** — the weather sensor sample wires cron → D1 → queue → Durable Object →
  external API → D1 back together, with a live diagram, readings table and job log in the UI.
- **One-command local dev** — .NET Aspire starts the frontend and the worker together.
- **Wrangler for deploy** — the standard Cloudflare workflow, unchanged.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (for Wrangler)
- `npm install` at the repo root, to restore Wrangler

## Running locally

```bash
dotnet run --project src/Aspire/Aspire.AppHost
```

This starts the Blazor frontend and the worker, and opens the Aspire dashboard where you can follow
logs and endpoints for both.

The D1 sample needs its schema once: `wrangler dev` creates an empty local database, but does not run
`migrations/`. Apply the migrations to the local copy before the first run (or whenever you change
them):

```bash
npx wrangler d1 migrations apply dotnet --local -c wrangler.dev.toml
```

That writes the tables `items`, `sensors`, `readings` and `jobs` and seeds three sensors into
`.wrangler/state/v3`, which is also where the local KV and R2 state lives. The script is idempotent,
but `CREATE TABLE IF NOT EXISTS` cannot repair a table whose columns have changed since it was
created — drop that table and re-run it if you hit `no such column`.

## Sample frontend

The Blazor app is not just a shell around the API; each sample gets a page that shows the request,
the response headers and the state it changed.

| Route | Page | What it demonstrates |
| ----- | ---- | -------------------- |
| `/` | Overview | Cards linking to every sample, plus the local-dev notes |
| `/basics` | Request basics | JSON, a server-side proxy `fetch`, a real 302, and a validated POST |
| `/cache` | Cache API | A key, a hit/miss badge driven by the `x-cache` response header, and a purge button |
| `/kv` | KV storage | The three fixed keys as tiles, a live character counter, write and read |
| `/r2` | R2 bucket | File picker with a 1 KB check, upload, download (browser *and* fetch-and-inspect) and delete |
| `/queue` | Queue | The producer form, and the consumer's result: text, queued at, processed at |
| `/scheduled` | Scheduled task | The latest KV record, the run history, and a button that runs the task now |
| `/d1` | D1 database | Full CRUD over the `items` table: create, inline-edit a row, two-step delete, and every mutation returns the fresh list |
| `/telemetry` | Weather sensors | The whole pipeline: the six stages light up per state, readings with live/simulated badges, job attempts and the rate-gate lease |

The sidebar groups the samples by theme (requests, storage, data), so the list stays readable as
more samples are added.

Every page shares the same building blocks from `src/BlazorWebApp/Shared/`
(`PageHeader`, `SampleCard`, `ApiCallLog`, `RawView`, `Notice`, `WorkerStatus`), so the
status code, reason phrase, duration and the interesting response header of each call are visible
without opening the browser's developer tools. `WorkerApi` in `src/BlazorWebApp/Services/` performs
every call, which is where the base URL, timing and JSON parsing live.

## Bindings and API

Every binding in `wrangler.toml` backs one sample, and each one also works locally, where
`wrangler dev` simulates KV, R2, D1 and queues on this machine:

| Binding | Resource | Used by |
| ------- | -------- | ------- |
| `KV` | KV namespace `dotnet_test` | The KV sample (keys `sample_key_1..3`, max 64 characters each), the queue consumer (key `queue_result`) and the scheduled task (key `scheduled_result`) |
| `R2` | bucket `dotnettest` | The R2 sample: a single object `sample_file.txt`, overwritten by each upload, capped at 1 KB |
| `QUEUE` | queue `dotnet-queue` | The producer/consumer sample (messages of max 16 characters) |
| `DB` | D1 database `dotnet` | The CRUD sample (table `items`) and the telemetry pipeline (tables `sensors`, `readings`, `jobs`) |
| `TELEMETRY` | queue `dotnet-telemetry` | The telemetry pipeline: the cron trigger enqueues a job per due sensor, the consumer processes them |
| `RATE_GATE` | Durable Object `RateGate` | Hands out a single 30 second lease, so only one outbound weather API call is in flight |
| `ASSETS` | `src/WorkersDotNet/dist/wwwroot` | Serves the published Blazor app, falling back to `index.html` for SPA routes |

`[triggers] crons = ["0 * * * *", "*/5 * * * *"]` drives two handlers: the hourly scheduled-task
sample, and the telemetry poll that enqueues the sensors whose `next_read_at` has passed.

| Route | Method | Sample |
| ----- | ------ | ------ |
| `/api/json` | GET | JSON response |
| `/api/proxy` | GET | Server-side `fetch` (the target is validated against an allow-list) |
| `/api/redirect` | GET | Real 302 |
| `/api/policy` | POST | JSON body validation, 400 with a request id on failure |
| `/api/cache` | GET, POST | Cache API: read through the cache, POST purges the key |
| `/api/kv` | GET, POST | Read all three fixed keys, write one of them |
| `/api/r2` | GET, POST | Object metadata, upload (raw body, `content-type` is stored) |
| `/api/r2/download` | GET | Stream the stored object back |
| `/api/r2/delete` | POST | Delete the object |
| `/api/queue` | GET, POST | Producer (POST) and the consumer's latest result plus history (GET) |
| `/api/scheduled` | GET, POST | Run history (GET), run the task now (POST) |
| `/api/items` | GET, POST | List the D1 rows, create one |
| `/api/items/update` | POST | Update one row |
| `/api/items/delete` | POST | Delete one row |
| `/api/telemetry` | GET | Sensors, readings, jobs, pipeline stats and the rate-gate state |
| `/api/telemetry/run` | POST | Enqueue a job for every due sensor (what the cron does) |
| `/api/telemetry/reset` | POST | Mark every sensor active and due now, so a demo run has work to do |

Mutations return the resulting **snapshot** (the fresh list plus the counts), so a page never has to
follow a write with a read.

## Adding a URL

Adding an endpoint takes two small steps.

**1. Create the endpoint** in `src/WorkersDotNet/Endpoints/`. Endpoints are
plain instance classes; they receive the services they need through their
constructor:

```csharp
using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet
{
    public sealed class HelloEndpoint
    {
        private readonly GreetingService _greeting;

        public HelloEndpoint(GreetingService greeting)
        {
            _greeting = greeting;
        }

        public Task<Response> HandleAsync(Request request)
        {
            return Task.FromResult(Results.Ok(_greeting.Message(), new Url(request.Url).Pathname));
        }
    }
}
```

**2. Register it** in `AppServices.cs`, the composition root. It reads the
environment's bindings once, builds every service, injects them into the
endpoints, and then adds one line to the route table:

```csharp
var hello = new HelloEndpoint(new GreetingService());
Hello = hello;

router.Any("/api/hello", (request, context) => Hello.HandleAsync(request));
```

That is all, the route is now live, CORS headers included for allowed origins. `router.Any` matches
every method; use `router.Get` / `router.Post` / `router.Put` / `router.Patch` / `router.Delete` /
`router.Options` when a route should only answer one. `router.Fallback` catches everything else and
hands it to the `ASSETS` binding, which is how the Blazor SPA is served.

## Response helpers

`Results` keeps endpoint bodies short and consistent:

```csharp
return Results.Ok("Reading accepted", new Url(request.Url).Pathname);  // 200 + ApiResponse
return Results.Error("Method not allowed", 405);                       // error body with a requestId
return Results.Json(new ApiResponse(...), 201);                        // explicit status code
return Results.Empty(204);                                             // no body
return Results.NoStore(Response.Json(payload, 200));                   // cache-control: no-store
```

`Results.Error` returns `{ "error": "...", "requestId": "..." }`, which makes failures easy to
correlate with logs. `Results.Empty` and `Results.NoStore` exist because `Response` lost its
`Empty` / `WithHeader` helpers in `0.6`: a `Response` is now also the init object of
`new Response(body, init)`, and its `Status` / `Headers` are init-only.

## CORS

Cross-origin access is an explicit allow-list; there is no wildcard. Allowed origins come from the
comma-separated `ALLOWED_ORIGINS` variable:

```toml
[vars]
ALLOWED_ORIGINS = "http://localhost:5217,https://localhost:7000,https://app.example.com"
```

`CorsPolicy.ParseOrigins` splits and trims that list, and `AppServices` hands the result to the
router's built-in `Cors.Allow` middleware:

```csharp
router.Use(Cors.Allow(
    CorsPolicy.ParseOrigins(environment.Variable("ALLOWED_ORIGINS")),
    CorsPolicy.Methods(),
    credentials: false,
    CorsPolicy.RequestHeaders(),
    CorsPolicy.ExposedHeaders(),
    CorsPolicy.MaxAgeSeconds));
```

An origin has to match an entry exactly, and an empty or missing list grants no cross-origin access
at all. The middleware answers preflights and only emits `access-control-allow-origin` for an
accepted origin, which means everything else is left to the browser to block. `CorsPolicy.IsCorsFetch`
tells the endpoints whether a request came from another origin, so they can skip the work a
preflight already covered.

Where the value is set:

| File             | Value                                     | Why                                                                                                                                          |
| ---------------- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `wrangler.dev.toml` | `http://localhost:5217,https://localhost:7000` | Under Aspire the Blazor frontend runs on its own port (`src/BlazorWebApp/Properties/launchSettings.json`) while the worker listens on `:8787`, so those calls are cross-origin |
| `wrangler.toml`  | empty                                     | The deployed Blazor app is served by this same worker through the `[assets]` binding (`ApiBaseUrl` is empty in `appsettings.json`), so its calls are same-origin and need no entry |

Add origins to `wrangler.toml` only when the frontend is hosted somewhere else, such as Cloudflare
Pages or a custom domain. Spaces around entries are ignored.

> Browsing the frontend as `127.0.0.1` instead of `localhost` produces a different origin. Add
> `http://127.0.0.1:5217` to the list if you prefer that host name.

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
runs `cloudflarebuild.sh`, so Cloudflare builds the C# for you.

The bindings have to exist first. The KV namespace id is already in `wrangler.toml`; the rest are
created with one command each:

```bash
npx wrangler queues create dotnet-queue
npx wrangler queues create dotnet-telemetry
npx wrangler r2 bucket create dotnettest
npx wrangler kv namespace create dotnet_test     # paste the printed id into [[kv_namespaces]]
npx wrangler d1 create dotnet                    # paste the printed id into [[d1_databases]]
```

The deploy build command applies pending remote D1 migrations automatically before publishing the
worker. `migrations_dir` tells Wrangler where the migrations are, and the `[build]` command in
`wrangler.toml` runs `npx wrangler d1 migrations apply dotnet --remote` before the normal build.
Without the migration step, every D1 and telemetry call fails. Deploy with:

```bash
npx wrangler deploy
```

The Durable Object migration (`[[migrations]] tag = "v1"` in `wrangler.toml`) is applied by the same
deploy, and the two cron triggers are registered with it. Trigger one on demand with
`npx wrangler dev --test-scheduled` (locally) or by calling `POST /api/scheduled` /
`POST /api/telemetry/run` through the deployed worker.

## Conventions and compiler notes

The `Workers` compiler is a focused source-to-JavaScript emitter rather than a full .NET runtime.
It supports the common C# you would expect (`switch` statements, `const string` case labels, static
helper classes, instance classes with constructor injection, anonymous objects, records, generic
methods, `foreach`, LINQ-free collection types), but a few constructs are intentionally out of scope.

Practical consequences for this codebase:

- **Instance services with constructor injection.** Since `0.4.0` the compiler emits instance
  classes, so endpoints and services are plain `sealed class` instances and receive their
  dependencies (a `D1Store`, a `KvStore`, a queue binding, ...) through the constructor. Only
  small pure helpers (`Results`, `AuthPassword`, `Clock`, `Text`, `CorsPolicy`) stay `static`.
  `AppServices.cs` is the composition root: it reads the environment's bindings once and builds
  every service and endpoint.
- **Helper services wrap the bindings.** `D1Store` wraps the `D1` binding (parameterised
  `QueryAsync` / `FirstAsync<T>` / `AllAsync<T>` / `ExecuteAsync`) and `KvStore` wraps the `KV`
  binding (`GetJsonAsync<T>` / `PutJsonAsync<T>`), so services never touch the raw bindings.
- **Bindings are injected, not passed per call.** A service takes the bindings it needs once in its
  constructor instead of receiving them as a parameter on every method.
- **No `static` fields, `const` is fine.** A `const string X = SampleConfig.Foo;` inlines to a
  literal; a non-constant static field is a compile error (`WRK119`/`WRK116`). Use instance
  `readonly` fields for values that are fixed per instance.
- **No casts or `default` literals.** Cast expressions (`(long)x`) and `default` / `Array.Empty`
  are out of scope, so type the fields to match and use `return null;` with a `where T : class`
  constraint instead of `default`.
- **Nested types are not allowed.** A `record` nested inside a user class is a compile error
  (`WRK119`). Declare them at namespace level instead.
- **No `params` / `ref` / `out` / `in`.** Method arguments must be plain; D1 helpers take an
  explicit `object[] args`, built with a collection expression `[a, b, c]` (the `new object[] { }`
  form is also rejected).
- **Object initializers are required for init-only properties.** `Response`, `RequestInit`,
  `QueueSendOptions`, `R2PutOptions`, `R2HttpMetadata` and `MessageSendRequest<T>` all expose
  init-only properties in `0.6`, so they are built with `new X { ... }` rather than by assigning
  afterwards. Read-only properties on your own types are still assigned in the constructor.
- **A response body can only be read once.** A `Response` is also the init object of
  `new Response(body, init)`, and reading `response.Body` twice throws
  `TypeError: This ReadableStream is disturbed`. The cache sample therefore builds a separate
  `Response` for the copy it stores and for the copy it returns.
- **Keep the router thin.** All logic lives in the endpoint classes, so the route table in
  `AppServices.cs` stays a readable index of the worker's URLs.
- **One queue and one scheduled handler per worker.** The compiler allows a single `[Queue]` and a
  single `[Scheduled]` method, so `WorkerEvents.cs` reads `batch.Queue` / `scheduled.Cron` and
  dispatches internally. That is why both crons and both queues live in one file.
- **Durable Objects derive from `DurableObject`.** Since `0.6` there is no `[DurableObject]`
  attribute: `RateGate` derives from the `DurableObject` base class, which supplies `Ctx` and `Env`,
  so it needs no constructor. Every public method becomes part of the object's RPC surface, so the
  worker-side helpers are private and only `reserve`, `complete` and `peek` are callable.
- **`dist/` is generated.** It is gitignored; never edit it by hand.

### The supported C# profile

The compiler enforces a restricted profile (`WRK105`) and rejects a surprising amount of the BCL.
These are the ones that bite in practice:

| Not available | Use instead |
| ------------- | ----------- |
| `DateTimeOffset` (all members) | `Clock.NowMs()` / `Clock.NowIso()`, built on the runtime's `Date` |
| `Guid.NewGuid()` | `Crypto.RandomUuid()` |
| `Random.Shared` | `Crypto.GetRandomValues(new byte[4])` (see `TelemetryService.RandomUnit`) |
| `Regex` | hand-written checks (see `Text.IsDigits`) |
| `Math.Max` / `Math.Min` | a comparison, or `if` |
| `char.IsDigit` | `c >= '0' && c <= '9'` |
| `string.Format` | string interpolation |
| `double.ToString("F2")` | `double.ToString()` |
| `Convert.ToInt64` | keep the value as a `double` |

`Date.Now()` returns a JavaScript number, and there is no supported way to turn it into a `long`
(no cast, no `Convert`), so every unix-millisecond value in this codebase is a `double` — including
the fields in `src/Shared` that the Blazor frontend reads. `Clock` is the single place that reads
the clock, so the rest of the worker never touches `Date` directly.

`Log.Info` / `Log.Warn` / `Log.Error` replace `Console.WriteLine` and show up in `wrangler tail`.

## Credits

This project builds on the excellent **Workers** package and sample collection by
**[Iñigo Ruiz-Salinas](https://github.com/iruizsalinas)** — **<https://github.com/iruizsalinas/workers>**.

That repository provides the C#-to-Cloudflare-Workers compiler and the samples that made this
approach possible. The NuGet package `Workers` (`0.6.3`) used under `src/WorkersDotNet` comes from
there, and its description sums it up well: _"Compile a focused C# profile directly to minimal
Cloudflare Workers JavaScript."_ If you want to understand how C# is compiled for the edge, or see
many more worked examples than the handful here, start there.

The endpoint and helper structure in `src/WorkersDotNet` is a refactor of those samples into a
small, ASP.NET-style layout. Many thanks to the author for making C# on Workers possible.

## License

[MIT](LICENSE) — Copyright (c) 2026 Michiel Post.