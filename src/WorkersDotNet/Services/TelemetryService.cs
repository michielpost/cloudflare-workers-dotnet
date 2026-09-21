using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The telemetry pipeline's business logic: queuing due sensors, consuming
    /// jobs (rate-gated weather calls), recording jobs and readings in D1, and
    /// reading the whole pipeline state for the UI. The endpoint stays a thin
    /// controller; the D1, queue and Durable Object bindings are injected.
    /// </summary>
    public sealed class TelemetryService
    {
        readonly string LiveSource = "live";
        readonly string SimulatedSource = "simulated";

        /// <summary>The name of the RateGate Durable Object this pipeline uses.</summary>
        readonly string GateName = "outbound-api";

        readonly int BatchSize = SampleConfig.TelemetryBatchSize;
        readonly int ReadingLimit = SampleConfig.TelemetryReadingLimit;
        readonly int JobLimit = 10;
        readonly int TimeoutSeconds = SampleConfig.TelemetryFetchTimeoutSeconds;

        private readonly D1Database _db;
        private readonly IQueueProducer _queue;
        private readonly IDurableObjectNamespace _gateNs;

        public TelemetryService(D1Database db, IQueueProducer queue, IDurableObjectNamespace gateNs)
        {
            _db = db;
            _queue = queue;
            _gateNs = gateNs;
        }

        /// <summary>
        /// Queues one job for every sensor that is due. This is the whole of the
        /// cron trigger's work, which is why the trigger and the button share it.
        /// </summary>
        public async Task<List<string>> EnqueueDueAsync()
        {
            var now = DateTimeOffset.UtcNow;

            var due = await _db.AllAsync<DueSensor>(
                _db.Prepare($"SELECT sensor_id AS sensorId, interval_minutes AS intervalMinutes FROM sensors WHERE active = 1 AND next_read_at <= ? ORDER BY next_read_at LIMIT {BatchSize}")
                    .Bind(now.ToUnixTimeMilliseconds()));

            var sensorIds = new List<string>();
            var messages = new List<QueuedMessage>();

            foreach (var sensor in due)
            {
                var jobId = Guid.NewGuid().ToString();

                await _db.ExecuteAsync(
                    _db.Prepare("INSERT OR REPLACE INTO jobs (id, sensor_id, status, attempts, last_error, queued_at, processed_at) VALUES (?, ?, ?, 0, '', ?, 0)")
                        .Bind(jobId, sensor.SensorId, "queued", now.ToUnixTimeMilliseconds()));

                await _db.ExecuteAsync(
                    _db.Prepare("UPDATE sensors SET next_read_at = ? WHERE sensor_id = ?")
                        .Bind(now.AddSeconds(sensor.IntervalMinutes * 60).ToUnixTimeMilliseconds(), sensor.SensorId));

                messages.Add(new QueuedMessage(sensor.SensorId, "", jobId, now.ToUnixTimeMilliseconds()));
                sensorIds.Add(sensor.SensorId);
            }

            if (messages.Count != 0)
                await _queue.SendJsonBatchAsync(messages);

            Console.WriteLine($"Telemetry: queued {messages.Count} sensor job(s)");
            return sensorIds;
        }

        /// <summary>
        /// The consumer's work for one job: take the rate gate lease, call the
        /// weather API with a timeout, store the reading and release the lease.
        /// Throwing hands the message back to the queue for a retry.
        /// </summary>
        public async Task ProcessJobAsync(QueuedMessage message, int attempts)
        {
            var lease = await _gateNs.GetByName(GateName)
                .InvokeAsync<Lease>("reserve", new List<object> { message.JobId });
            if (lease is null || !lease.Allowed)
            {
                var owner = lease is null ? "" : lease.Owner;
                throw new InvalidOperationException($"the rate gate is held by job {owner}");
            }

            var location = await _db.FirstAsync<SensorLocation>(
                _db.Prepare("SELECT latitude, longitude FROM sensors WHERE sensor_id = ?").Bind(message.Text));

            var latitude = location is null ? 0.0 : location.Latitude;
            var longitude = location is null ? 0.0 : location.Longitude;

            var controller = new AbortController();
            var timeout = Timers.SetTimeout(
                () => controller.Abort("the weather API did not answer in time"),
                TimeSpan.FromMilliseconds(TimeoutSeconds * 1000));

            var readingAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var temperature = 0.0;
            var airQuality = 0.0;
            var condition = "unknown";
            var source = LiveSource;

            try
            {
                var options = new FetchOptions { Signal = controller.Signal };
                var responses = await Task.WhenAll(
                    Http.FetchAsync(ForecastUrl(latitude, longitude), options),
                    Http.FetchAsync(AirQualityUrl(latitude, longitude), options));

                if (!responses[0].IsSuccessStatusCode || !responses[1].IsSuccessStatusCode)
                    throw new InvalidOperationException("the weather API did not answer with a 2xx status");

                var forecast = await responses[0].JsonAsync<ForecastResponse>();
                var air = await responses[1].JsonAsync<AirQualityResponse>();
                if (forecast is null || forecast.Current is null || air is null || air.Current is null)
                    throw new InvalidOperationException("the weather API returned an unexpected payload");

                temperature = forecast.Current.Temperature_2m;
                airQuality = air.Current.Pm2_5;
                condition = ConditionText(forecast.Current.Weather_code);
            }
            catch (Exception exception)
            {
                // The provider is a nice-to-have: when it is slow or unreachable
                // the pipeline still produces a reading, so the sample keeps
                // working offline. The UI marks those rows as "simulated".
                Console.Error.WriteLine($"Weather API unavailable ({exception.Message}), storing a simulated reading instead");
                temperature = 12 + (Random.Shared.NextDouble() * 10);
                airQuality = 4 + (Random.Shared.NextDouble() * 18);
                condition = RandomCondition();
                source = SimulatedSource;
            }
            finally
            {
                Timers.ClearTimeout(timeout);
            }

            var processedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await _db.ExecuteAsync(
                _db.Prepare("INSERT OR REPLACE INTO readings (id, sensor_id, temperature, air_quality, condition, source, reading_at, processed_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)")
                    .Bind(message.JobId, message.Text, temperature, airQuality, condition, source, readingAt, processedAt));

            await MarkJobAsync(message.JobId, "done", attempts, "");

            await _gateNs.GetByName(GateName)
                .InvokeVoidAsync("complete", new List<object> { lease.Token });

            Console.WriteLine($"Telemetry: sensor {message.Text} stored a {source} reading ({condition})");
        }

        /// <summary>Records how a job ended, so the UI can show its state.</summary>
        public async Task MarkJobAsync(string jobId, string status, int attempts, string error)
        {
            await _db.ExecuteAsync(
                _db.Prepare("UPDATE jobs SET status = ?, attempts = ?, last_error = ?, processed_at = ? WHERE id = ?")
                    .Bind(status, attempts, error, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), jobId));
        }

        /// <summary>Makes every sensor due again (POST /api/telemetry/reset).</summary>
        public async Task ResetAsync()
        {
            await _db.ExecuteAsync(_db.Prepare("UPDATE sensors SET active = 1, next_read_at = 0"));
        }

        /// <summary>Everything GET /api/telemetry returns.</summary>
        public async Task<TelemetryStatus> ReadStatusAsync()
        {
            var sensors = await _db.AllAsync<SensorInfo>(
                _db.Prepare("SELECT s.sensor_id AS sensorId, s.name, s.latitude, s.longitude, s.interval_minutes AS intervalMinutes, s.next_read_at AS nextReadAtMs, s.active,"
                + " (SELECT COUNT(*) FROM readings r WHERE r.sensor_id = s.sensor_id) AS reads,"
                + " (SELECT COUNT(*) FROM jobs j WHERE j.sensor_id = s.sensor_id AND j.status <> 'done' AND j.status <> 'failed') AS pending"
                + " FROM sensors s ORDER BY s.sensor_id"));

            var readings = await _db.AllAsync<ReadingInfo>(
                _db.Prepare($"SELECT id, sensor_id AS sensorId, temperature, air_quality AS airQuality, condition, source, reading_at AS readingAtMs, processed_at AS processedAtMs FROM readings ORDER BY reading_at DESC, id LIMIT {ReadingLimit}"));

            var jobs = await _db.AllAsync<JobInfo>(
                _db.Prepare($"SELECT id, sensor_id AS sensorId, status, attempts, last_error AS lastError, queued_at AS queuedAtMs, processed_at AS processedAtMs FROM jobs ORDER BY queued_at DESC LIMIT {JobLimit}"));

            var stats = new PipelineStats(
                sensors.Count,
                await _db.CountAsync(_db.Prepare("SELECT COUNT(*) AS value FROM readings")),
                await _db.CountAsync(_db.Prepare("SELECT COUNT(*) AS value FROM jobs WHERE status = 'queued' OR status = 'retrying'")),
                await _db.CountAsync(_db.Prepare("SELECT COUNT(*) AS value FROM jobs WHERE status = 'done'")),
                await _db.CountAsync(_db.Prepare("SELECT COUNT(*) AS value FROM jobs WHERE status = 'failed'")),
                await _db.CountAsync(_db.Prepare("SELECT COALESCE(SUM(attempts), 0) AS value FROM jobs")));

            var gate = await ReadGateAsync();

            return new TelemetryStatus(
                sensors,
                readings,
                jobs,
                stats,
                gate,
                SampleConfig.D1DatabaseName,
                SampleConfig.TelemetryQueueName,
                SampleConfig.TelemetryCron,
                SampleConfig.TelemetryRateLimitSeconds,
                SampleConfig.TelemetryMaxAttempts,
                SampleConfig.WeatherApiName);
        }

        /// <summary>Asks the Durable Object for its lease. Never fails the read.</summary>
        public async Task<GateInfo> ReadGateAsync()
        {
            try
            {
                var lease = await _gateNs.GetByName(GateName).InvokeAsync<Lease>("peek", new List<object>());

                if (lease is null || !lease.Allowed)
                    return new GateInfo(false, "", SampleConfig.TelemetryRateLimitSeconds, 0);

                return new GateInfo(true, lease.Owner, SampleConfig.TelemetryRateLimitSeconds, lease.ExpiresAtMs);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not read the rate gate ({exception.Message})");
                return new GateInfo(false, "", SampleConfig.TelemetryRateLimitSeconds, 0);
            }
        }

        static string ForecastUrl(double latitude, double longitude)
        {
            return $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code";
        }

        static string AirQualityUrl(double latitude, double longitude)
        {
            return $"https://air-quality-api.open-meteo.com/v1/air-quality?latitude={latitude}&longitude={longitude}&current=pm2_5";
        }

        /// <summary>Picks a plausible condition for a simulated reading.</summary>
        static string RandomCondition()
        {
            var roll = Random.Shared.NextDouble();

            if (roll < 0.5)
                return "clear";

            return roll < 0.8 ? "partly cloudy" : "rain";
        }

        /// <summary>Turns the WMO weather code the API returns into a word.</summary>
        static string ConditionText(int code)
        {
            if (code == 0)
                return "clear";

            if (code <= 3)
                return "partly cloudy";

            if (code == 45 || code == 48)
                return "fog";

            if (code >= 51 && code <= 67)
                return "rain";

            if (code >= 71 && code <= 77)
                return "snow";

            if (code >= 80 && code <= 82)
                return "showers";

            if (code >= 95)
                return "thunderstorm";

            return "unknown";
        }
    }
}
