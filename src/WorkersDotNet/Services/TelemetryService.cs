using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The telemetry pipeline's business logic: queuing due sensors, consuming
    /// jobs (rate-gated weather calls), recording jobs and readings in D1, and
    /// reading the whole pipeline state for the UI. The endpoint stays a thin
    /// controller; each method takes the binding it needs. Generic operations
    /// (D1 queries, queue send, the Durable Object) call the SDK directly.
    /// </summary>
    public static class TelemetryService
    {
        const string LiveSource = "live";
        const string SimulatedSource = "simulated";

        /// <summary>The name of the RateGate Durable Object this pipeline uses.</summary>
        public const string GateName = "outbound-api";

        const int BatchSize = SampleConfig.TelemetryBatchSize;
        const int ReadingLimit = SampleConfig.TelemetryReadingLimit;
        const int JobLimit = 10;
        const int TimeoutSeconds = SampleConfig.TelemetryFetchTimeoutSeconds;

        /// <summary>
        /// Queues one job for every sensor that is due. This is the whole of the
        /// cron trigger's work, which is why the trigger and the button share it.
        /// </summary>
        public static async Task<List<string>> EnqueueDueAsync(ID1Database db, IQueueProducer queue)
        {
            var now = DateTimeOffset.UtcNow;

            var due = await AllDueAsync(
                db,
                $"SELECT sensor_id AS sensorId, interval_minutes AS intervalMinutes FROM sensors WHERE active = 1 AND next_read_at <= ? ORDER BY next_read_at LIMIT {BatchSize}",
                now.ToUnixTimeMilliseconds());

            var sensorIds = new List<string>();
            var messages = new List<QueuedMessage>();

            foreach (var sensor in due)
            {
                var jobId = Guid.NewGuid().ToString();

                await db.Prepare(
                    "INSERT OR REPLACE INTO jobs (id, sensor_id, status, attempts, last_error, queued_at, processed_at) VALUES (?, ?, ?, 0, '', ?, 0)")
                    .Bind(jobId, sensor.SensorId, "queued", now.ToUnixTimeMilliseconds())
                    .RunAsync();

                await db.Prepare("UPDATE sensors SET next_read_at = ? WHERE sensor_id = ?")
                    .Bind(now.AddSeconds(sensor.IntervalMinutes * 60).ToUnixTimeMilliseconds(), sensor.SensorId)
                    .RunAsync();

                messages.Add(new QueuedMessage(sensor.SensorId, "", jobId, now.ToUnixTimeMilliseconds()));
                sensorIds.Add(sensor.SensorId);
            }

            if (messages.Count != 0)
                await queue.SendJsonBatchAsync(messages);

            Console.WriteLine($"Telemetry: queued {messages.Count} sensor job(s)");
            return sensorIds;
        }

        /// <summary>
        /// The consumer's work for one job: take the rate gate lease, call the
        /// weather API with a timeout, store the reading and release the lease.
        /// Throwing hands the message back to the queue for a retry.
        /// </summary>
        public static async Task ProcessJobAsync(ID1Database db, IDurableObjectNamespace gateNs, QueuedMessage message, int attempts)
        {
            var lease = await gateNs.GetByName(GateName)
                .InvokeAsync<Lease>("reserve", new List<object> { message.JobId });
            if (lease is null || !lease.Allowed)
            {
                var owner = lease is null ? "" : lease.Owner;
                throw new InvalidOperationException($"the rate gate is held by job {owner}");
            }

            var location = await FirstLocationAsync(db, message.Text);

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

            await db.Prepare(
                "INSERT OR REPLACE INTO readings (id, sensor_id, temperature, air_quality, condition, source, reading_at, processed_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)")
                .Bind(message.JobId, message.Text, temperature, airQuality, condition, source, readingAt, processedAt)
                .RunAsync();

            await MarkJobAsync(db, message.JobId, "done", attempts, "");

            await gateNs.GetByName(GateName)
                .InvokeVoidAsync("complete", new List<object> { lease.Token });

            Console.WriteLine($"Telemetry: sensor {message.Text} stored a {source} reading ({condition})");
        }

        /// <summary>Records how a job ended, so the UI can show its state.</summary>
        public static async Task MarkJobAsync(ID1Database db, string jobId, string status, int attempts, string error)
        {
            await db.Prepare("UPDATE jobs SET status = ?, attempts = ?, last_error = ?, processed_at = ? WHERE id = ?")
                .Bind(status, attempts, error, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), jobId)
                .RunAsync();
        }

        /// <summary>Makes every sensor due again (POST /api/telemetry/reset).</summary>
        public static async Task ResetAsync(ID1Database db)
        {
            await db.Prepare("UPDATE sensors SET active = 1, next_read_at = 0").RunAsync();
        }

        /// <summary>Everything GET /api/telemetry returns.</summary>
        public static async Task<TelemetryStatus> ReadStatusAsync(ID1Database db, IDurableObjectNamespace gateNs)
        {
            var sensors = await AllSensorsAsync(
                db,
                "SELECT s.sensor_id AS sensorId, s.name, s.latitude, s.longitude, s.interval_minutes AS intervalMinutes, s.next_read_at AS nextReadAtMs, s.active,"
                + " (SELECT COUNT(*) FROM readings r WHERE r.sensor_id = s.sensor_id) AS reads,"
                + " (SELECT COUNT(*) FROM jobs j WHERE j.sensor_id = s.sensor_id AND j.status <> 'done' AND j.status <> 'failed') AS pending"
                + " FROM sensors s ORDER BY s.sensor_id");

            var readings = await AllReadingsAsync(
                db,
                $"SELECT id, sensor_id AS sensorId, temperature, air_quality AS airQuality, condition, source, reading_at AS readingAtMs, processed_at AS processedAtMs FROM readings ORDER BY reading_at DESC, id LIMIT {ReadingLimit}");

            var jobs = await AllJobsAsync(
                db,
                $"SELECT id, sensor_id AS sensorId, status, attempts, last_error AS lastError, queued_at AS queuedAtMs, processed_at AS processedAtMs FROM jobs ORDER BY queued_at DESC LIMIT {JobLimit}");

            var stats = new PipelineStats(
                sensors.Count,
                await CountAsync(db, "SELECT COUNT(*) AS value FROM readings"),
                await CountAsync(db, "SELECT COUNT(*) AS value FROM jobs WHERE status = 'queued' OR status = 'retrying'"),
                await CountAsync(db, "SELECT COUNT(*) AS value FROM jobs WHERE status = 'done'"),
                await CountAsync(db, "SELECT COUNT(*) AS value FROM jobs WHERE status = 'failed'"),
                await CountAsync(db, "SELECT COALESCE(SUM(attempts), 0) AS value FROM jobs"));

            var gate = await ReadGateAsync(gateNs);

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
        public static async Task<GateInfo> ReadGateAsync(IDurableObjectNamespace gateNs)
        {
            try
            {
                var lease = await gateNs.GetByName(GateName).InvokeAsync<Lease>("peek", new List<object>());

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

        static async Task<int> CountAsync(ID1Database db, string sql)
        {
            var row = await db.Prepare(sql).FirstAsync<CountRow>();
            return row is null ? 0 : row.Value;
        }

        static async Task<List<DueSensor>> AllDueAsync(ID1Database db, string sql, long nowMs)
        {
            var result = await db.Prepare(sql).Bind(nowMs).AllAsync<DueSensor>();
            var list = new List<DueSensor>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row);
            }
            return list;
        }

        static async Task<List<SensorInfo>> AllSensorsAsync(ID1Database db, string sql)
        {
            var result = await db.Prepare(sql).AllAsync<SensorInfo>();
            var list = new List<SensorInfo>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row);
            }
            return list;
        }

        static async Task<List<ReadingInfo>> AllReadingsAsync(ID1Database db, string sql)
        {
            var result = await db.Prepare(sql).AllAsync<ReadingInfo>();
            var list = new List<ReadingInfo>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row);
            }
            return list;
        }

        static async Task<List<JobInfo>> AllJobsAsync(ID1Database db, string sql)
        {
            var result = await db.Prepare(sql).AllAsync<JobInfo>();
            var list = new List<JobInfo>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row);
            }
            return list;
        }

        static async Task<SensorLocation?> FirstLocationAsync(ID1Database db, string sensorId)
        {
            try
            {
                return await db.Prepare("SELECT latitude, longitude FROM sensors WHERE sensor_id = ?")
                    .Bind(sensorId)
                    .FirstAsync<SensorLocation>();
            }
            catch (Exception)
            {
                return null;
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
