using Shared;
using Workers;
using WorkersDotNet.Auth;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// The composition root. It reads the environment's bindings once and builds
    /// every service and endpoint, wiring each service into the endpoints that
    /// use it, then registers the routes on the <see cref="Workers.Router"/>.
    /// The worker entrypoints just build one of these from <c>Env</c> and hand
    /// the request to <see cref="Routes"/>.
    /// </summary>
    public sealed class AppServices
    {
        public Router Routes { get; }

        // Services, used directly by the non-HTTP handlers (queue/scheduled).
        public ItemsService ItemsService { get; }
        public KvSampleService KvSample { get; }
        public R2SampleService R2Sample { get; }
        public QueueSampleService QueueSample { get; }
        public ScheduledSampleService ScheduledSample { get; }
        public TelemetryService TelemetryService { get; }
        public AuthService AuthService { get; }

        public JsonEndpoint Json { get; }
        public ProxyEndpoint Proxy { get; }
        public RedirectEndpoint Redirect { get; }
        public PolicyEndpoint Policy { get; }
        public CacheEndpoint Cache { get; }
        public KvEndpoint Kv { get; }
        public R2Endpoint R2 { get; }
        public QueueEndpoint Queue { get; }
        public ScheduledEndpoint Scheduled { get; }
        public ItemsEndpoint Items { get; }
        public AuthEndpoint Auth { get; }
        public TelemetryEndpoint Telemetry { get; }
        public AssetsEndpoint Assets { get; }

        public AppServices(Env environment)
        {
            var db = new D1Store(environment.D1("DB"));
            var kv = new KvStore(environment.KV("KV"));
            var queue = environment.Queue("QUEUE");
            var telemetryQueue = environment.Queue("TELEMETRY");
            var gateNs = environment.DurableObject("RATE_GATE");
            var r2 = environment.R2("R2");
            var assets = environment.Assets("ASSETS");
            var defaultAdminEmail = environment.Variable(SampleConfig.DefaultAdminEmailVariable);

            var itemsService = new ItemsService(db);
            var kvService = new KvSampleService(kv);
            var r2Service = new R2SampleService(r2);
            var queueService = new QueueSampleService(queue, kv);
            var scheduledService = new ScheduledSampleService(kv);
            var telemetryService = new TelemetryService(db, telemetryQueue, gateNs);
            var authService = new AuthService(db, defaultAdminEmail);

            ItemsService = itemsService;
            KvSample = kvService;
            R2Sample = r2Service;
            QueueSample = queueService;
            ScheduledSample = scheduledService;
            TelemetryService = telemetryService;
            AuthService = authService;

            Json = new JsonEndpoint();
            Proxy = new ProxyEndpoint();
            Redirect = new RedirectEndpoint();
            Policy = new PolicyEndpoint();
            Cache = new CacheEndpoint();
            Kv = new KvEndpoint(kvService);
            R2 = new R2Endpoint(r2Service);
            Queue = new QueueEndpoint(queueService);
            Scheduled = new ScheduledEndpoint(scheduledService);
            Items = new ItemsEndpoint(itemsService);
            Auth = new AuthEndpoint(authService);
            Telemetry = new TelemetryEndpoint(telemetryService);
            Assets = new AssetsEndpoint(assets);

            var router = new Router();

            // CORS is middleware, so it answers the browser preflight before any
            // route runs and decorates every response - including the error ones
            // produced by OnError below.
            router.Use(Cors.Allow(
                CorsPolicy.ParseOrigins(environment.Variable(CorsPolicy.AllowedOriginsVariable)),
                CorsPolicy.Methods(),
                false,
                CorsPolicy.RequestHeaders(),
                CorsPolicy.ExposedHeaders(),
                CorsPolicy.MaxAgeSeconds));

            // Turns an unhandled exception into the same JSON error shape the
            // endpoints use, instead of the router's empty default response.
            router.OnError((error, request, route) => Results.Error(error.Message, error.Status));

            // Every route is registered with Any: the endpoints validate the HTTP
            // method themselves and answer with a structured ApiResponse error.
            router.Any("/api/json", (request, route) => Json.HandleAsync(request));
            router.Any("/api/proxy", (request, route) => Proxy.HandleAsync(request));
            router.Any("/api/redirect", (request, route) => Redirect.HandleAsync(request));
            router.Any("/api/policy", (request, route) => Policy.HandleAsync(request));
            router.Any("/api/cache", (request, route) => Cache.HandleAsync(request, route.Ctx));
            router.Any("/api/kv", (request, route) => Kv.HandleAsync(request));
            router.Any("/api/r2", (request, route) => R2.HandleAsync(request));
            router.Any("/api/r2/download", (request, route) => R2.DownloadAsync(request));
            router.Any("/api/r2/delete", (request, route) => R2.DeleteAsync(request));
            router.Any("/api/queue", (request, route) => Queue.HandleAsync(request));
            router.Any("/api/scheduled", (request, route) => Scheduled.HandleAsync(request));
            router.Any("/api/items", (request, route) => Items.HandleAsync(request));
            router.Any("/api/items/update", (request, route) => Items.UpdateAsync(request));
            router.Any("/api/items/delete", (request, route) => Items.DeleteAsync(request));
            router.Any("/api/auth/register", (request, route) => Auth.RegisterAsync(request));
            router.Any("/api/auth/login", (request, route) => Auth.LoginAsync(request));
            router.Any("/api/auth/logout", (request, route) => Auth.LogoutAsync(request));
            router.Any("/api/auth/me", (request, route) => Auth.MeAsync(request));
            router.Any("/api/auth/profile", (request, route) => Auth.UpdateProfileAsync(request));
            router.Any("/api/auth/roles", (request, route) => Auth.AssignRolesAsync(request));
            router.Any("/api/auth/users", (request, route) => Auth.UsersAsync(request));
            router.Any("/api/telemetry", (request, route) => Telemetry.HandleAsync(request));
            router.Any("/api/telemetry/run", (request, route) => Telemetry.RunAsync());
            router.Any("/api/telemetry/reset", (request, route) => Telemetry.ResetAsync());

            // Anything else is the BlazorWebApp, served as static files through
            // the ASSETS binding.
            router.Fallback((request, route) => Assets.HandleAsync(request));

            Routes = router;
        }
    }
}
