using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: an end-to-end pipeline that uses almost every binding at once -
    /// cron trigger, D1, queues, a Durable Object as a rate gate and outbound
    /// HTTP - to poll three weather sensors.
    /// </summary>
    /// <remarks>
    /// The flow is the one from the reference TelemetryPipeline sample:
    /// <list type="number">
    /// <item>the cron trigger every 5 minutes picks the sensors whose
    /// <c>next_read_at</c> has passed and writes a row per sensor to the
    /// <c>jobs</c> table,</item>
    /// <item>it puts one message per job on the <c>dotnet-telemetry</c> queue and
    /// pushes the sensor's due time forward,</item>
    /// <item>the queue consumer takes a lease at the RateGate Durable Object, so
    /// only one outbound call is in flight at a time,</item>
    /// <item>it calls the weather API twice (forecast and air quality) with a
    /// timeout, and</item>
    /// <item>stores the reading in D1 and releases the lease. A failure is
    /// retried with a growing delay, and the provider itself is optional: an
    /// unreachable API results in a simulated reading instead of a broken
    /// demo.</item>
    /// </list>
    /// GET /api/telemetry returns the state of the whole pipeline, and POST
    /// /api/telemetry/run runs step 1 and 2 by hand, which is how the pipeline is
    /// started during local development (there is no cron scheduler in local dev).
    /// </remarks>
    public static class TelemetryEndpoint
    {
        const string DbBinding = "DB";
        const string QueueBinding = "TELEMETRY";
        const string GateBinding = "RATE_GATE";
        const string GateName = "outbound-api";

        const string LiveSource = "live";
        const string SimulatedSource = "simulated";

        const int BatchSize = SampleConfig.TelemetryBatchSize;
        const int ReadingLimit = SampleConfig.TelemetryReadingLimit;
        const int JobLimit = 10;
        const int TimeoutSeconds = SampleConfig.TelemetryFetchTimeoutSeconds;

        /// <summary>GET returns the pipeline state, POST runs the cron work now.</summary>
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            if (request.Method == "POST")
                return await RunAsync(environment);

            if (request.Method != "GET")
                return Results.Error("Only GET and POST are supported on /api/telemetry", 405);

            var status = await ReadStatusAsync(environment);
            return Response.Json(status, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// Runs what the cron trigger does - queue every due sensor - and answers
        /// with the state of the pipeline. Local development has no cron
        /// scheduler, so the UI calls this to start a run by hand.
        /// </summary>
        public static async Task<Response> RunAsync(Env environment)
        {
            var sensorIds = await EnqueueDueAsync(environment);
            var status = await ReadStatusAsync(environment);

            var message = sensorIds.Count == 0
                ? $"No sensor is due right now. The cron trigger {SampleConfig.TelemetryCron} queues them every 5 minutes."
                : $"{sensorIds.Count} sensor job(s) queued on {SampleConfig.TelemetryQueueName}.";

            var result = new TelemetryRunResult(true, message, sensorIds.Count, sensorIds, status);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// Makes every sensor due again (POST /api/telemetry/reset). Reading a
        /// sensor pushes its next due time minutes into the future, so the demo
        /// presses this to get work for the next run without waiting.
        /// </summary>
        public static async Task<Response> ResetAsync(Env environment)
        {
            await environment.D1(DbBinding)
                .Prepare("UPDATE sensors SET active = 1, next_read_at = 0")
                .RunAsync();

            var status = await ReadStatusAsync(environment);
            var message = $"{status.Stats.Sensors} sensor(s) set to active and due now.";

            var result = new TelemetryRunResult(true, message, 0, new List<string>(), status);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// Queues one job for every sensor that is due. This is the whole of the
        /// cron trigger's work, which is why the trigger and the button share it.
        /// </summary>
        public static async Task<List<string>> EnqueueDueAsync(Env environment)
        {
            var db = environment.D1(DbBinding);
            var now = DateTimeOffset.UtcNow;

            var due = await db.Prepare(
                    $"SELECT sensor_id AS sensorId, interval_minutes AS intervalMinutes FROM sensors WHERE active = 1 AND next_read_at <= ? ORDER BY next_read_at LIMIT {BatchSize}")
                .Bind(now.ToUnixTimeMilliseconds())
                .AllAsync<DueSensor>();

            var sensorIds = new List<string>();
            var messages = new List<QueuedMessage>();

            if (due is not null && due.Results is not null)
            {
                foreach (var sensor in due.Results)
                {
                    var jobId = Guid.NewGuid().ToString();

                    await db.Prepare("INSERT OR REPLACE INTO jobs (id, sensor_id, status, attempts, last_error, queued_at, processed_at) VALUES (?, ?, ?, 0, '', ?, 0)")
                        .Bind(jobId, sensor.SensorId, "queued", now.ToUnixTimeMilliseconds())
                        .RunAsync();

                    await db.Prepare("UPDATE sensors SET next_read_at = ? WHERE sensor_id = ?")
                        .Bind(now.AddSeconds(sensor.IntervalMinutes * 60).ToUnixTimeMilliseconds(), sensor.SensorId)
                        .RunAsync();

                    messages.Add(new QueuedMessage(sensor.SensorId, "", jobId, now.ToUnixTimeMilliseconds()));
                    sensorIds.Add(sensor.SensorId);
                }
            }

            if (messages.Count != 0)
                await environment.Queue(QueueBinding).SendJsonBatchAsync(messages);

            Console.WriteLine($"Telemetry: queued {messages.Count} sensor job(s)");
            return sensorIds;
        }

        /// <summary>
        /// The consumer's work for one job: take the rate gate lease, call the
        /// weather API with a timeout, store the reading and release the lease.
        /// Throwing hands the message back to the queue for a retry.
        /// </summary>
        public static async Task ProcessJobAsync(Env environment, QueuedMessage message, int attempts)
        {
            var db = environment.D1(DbBinding);
            var gate = environment.DurableObject(GateBinding).GetByName(GateName);

            var lease = await gate.InvokeAsync<Lease>("reserve", new List<object> { message.JobId });
            if (lease is null || !lease.Allowed)
            {
                var owner = lease is null ? "" : lease.Owner;
                throw new InvalidOperationException($"the rate gate is held by job {owner}");
            }

            var location = await db.Prepare("SELECT latitude, longitude FROM sensors WHERE sensor_id = ?")
                .Bind(message.Text)
                .FirstAsync<SensorLocation>();

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

            await db.Prepare("INSERT OR REPLACE INTO readings (id, sensor_id, temperature, air_quality, condition, source, reading_at, processed_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)")
                .Bind(message.JobId, message.Text, temperature, airQuality, condition, source, readingAt, processedAt)
                .RunAsync();

            await MarkJobAsync(environment, message.JobId, "done", attempts, "");

            await gate.InvokeVoidAsync("complete", new List<object> { lease.Token });

            Console.WriteLine($"Telemetry: sensor {message.Text} stored a {source} reading ({condition})");
        }

        /// <summary>Records how a job ended, so the UI can show its state.</summary>
        public static async Task MarkJobAsync(Env environment, string jobId, string status, int attempts, string error)
        {
            await environment.D1(DbBinding)
                .Prepare("UPDATE jobs SET status = ?, attempts = ?, last_error = ?, processed_at = ? WHERE id = ?")
                .Bind(status, attempts, error, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), jobId)
                .RunAsync();
        }

        /// <summary>Everything GET /api/telemetry returns.</summary>
        static async Task<TelemetryStatus> ReadStatusAsync(Env environment)
        {
            var db = environment.D1(DbBinding);

            var sensors = new List<SensorInfo>();
            var sensorRows = await db.Prepare(
                    "SELECT s.sensor_id AS sensorId, s.name, s.latitude, s.longitude, s.interval_minutes AS intervalMinutes, s.next_read_at AS nextReadAtMs, s.active,"
                    + " (SELECT COUNT(*) FROM readings r WHERE r.sensor_id = s.sensor_id) AS reads,"
                    + " (SELECT COUNT(*) FROM jobs j WHERE j.sensor_id = s.sensor_id AND j.status <> 'done' AND j.status <> 'failed') AS pending"
                    + " FROM sensors s ORDER BY s.sensor_id")
                .AllAsync<SensorInfo>();

            if (sensorRows is not null && sensorRows.Results is not null)
            {
                foreach (var row in sensorRows.Results)
                    sensors.Add(row);
            }

            var readings = new List<ReadingInfo>();
            var readingRows = await db.Prepare(
                    $"SELECT id, sensor_id AS sensorId, temperature, air_quality AS airQuality, condition, source, reading_at AS readingAtMs, processed_at AS processedAtMs FROM readings ORDER BY reading_at DESC, id LIMIT {ReadingLimit}")
                .AllAsync<ReadingInfo>();

            if (readingRows is not null && readingRows.Results is not null)
            {
                foreach (var row in readingRows.Results)
                    readings.Add(row);
            }

            var jobs = new List<JobInfo>();
            var jobRows = await db.Prepare(
                    $"SELECT id, sensor_id AS sensorId, status, attempts, last_error AS lastError, queued_at AS queuedAtMs, processed_at AS processedAtMs FROM jobs ORDER BY queued_at DESC LIMIT {JobLimit}")
                .AllAsync<JobInfo>();

            if (jobRows is not null && jobRows.Results is not null)
            {
                foreach (var row in jobRows.Results)
                    jobs.Add(row);
            }

            var stats = new PipelineStats(
                sensors.Count,
                await CountAsync(db, "SELECT COUNT(*) AS value FROM readings"),
                await CountAsync(db, "SELECT COUNT(*) AS value FROM jobs WHERE status = 'queued' OR status = 'retrying'"),
                await CountAsync(db, "SELECT COUNT(*) AS value FROM jobs WHERE status = 'done'"),
                await CountAsync(db, "SELECT COUNT(*) AS value FROM jobs WHERE status = 'failed'"),
                await CountAsync(db, "SELECT COALESCE(SUM(attempts), 0) AS value FROM jobs"));

            var gate = await ReadGateAsync(environment);

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
        static async Task<GateInfo> ReadGateAsync(Env environment)
        {
            try
            {
                var gate = environment.DurableObject(GateBinding).GetByName(GateName);
                var lease = await gate.InvokeAsync<Lease>("peek", new List<object>());

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
