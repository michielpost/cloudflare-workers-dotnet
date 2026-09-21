using Shared;
using Workers;
using WorkersDotNet.Auth;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// The composition root. It reads the environment's bindings once and builds
    /// every service and endpoint, wiring each service into the endpoints that
    /// use it. The worker entrypoints just build one of these from <c>Env</c> and
    /// hand the request to <see cref="Router"/>.
    /// </summary>
    public sealed class AppServices
    {
        public Router Router { get; }

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
            var db = new D1Database(environment.D1("DB"));
            var kv = new KvStore(environment.Kv("KV"));
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

            Router = new Router(this);
        }
    }
}
