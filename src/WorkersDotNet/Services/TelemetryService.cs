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

        private readonly D1Store _db;
        private readonly Queue _queue;
        private readonly DurableObjectNamespace _gateNs;

        public TelemetryService(D1Store db, Queue queue, DurableObjectNamespace gateNs)
        {
            _db = db;
            _queue = queue;
            _gateNs = gateNs;
        }

        /// <summary>
        /// Queues one job for every sensor that is due. This is the whole of the
        /// cron trigger's work, which is why the trigger and the button share it.
        /// </summary>
        /// <param name="scheduledTimeMs">
        /// The cron trigger's scheduled time, in Unix milliseconds. The next read
        /// is measured from this instead of from the wall clock, so a trigger that
        /// fires a few milliseconds early cannot push the sensor's schedule past
        /// the following trigger. Without it the interval drifts: a 5-minute
        /// sensor whose trigger lands 1ms early is skipped on every other run and
        /// ends up reading every 10 minutes.
        /// </param>
        public async Task<List<string>> EnqueueDueAsync(double? scheduledTimeMs = null)
        {
            var now = Clock.NowMs();
            var anchor = scheduledTimeMs ?? now;

            // A small tolerance keeps a trigger that fires a few milliseconds
            // early from skipping a sensor that is due on the same second.
            var due = await _db.AllAsync<DueSensor>(
                _db.Prepare($"SELECT sensor_id AS sensorId, interval_minutes AS intervalMinutes FROM sensors WHERE active = 1 AND next_read_at <= ? ORDER BY next_read_at LIMIT {BatchSize}")
                    .Bind(now + 5000.0));

            var sensorIds = new List<string>();
            var messages = new List<MessageSendRequest<QueuedMessage>>();

            foreach (var sensor in due)
            {
                var jobId = Crypto.RandomUuid();

                await _db.ExecuteAsync(
                    _db.Prepare("INSERT OR REPLACE INTO jobs (id, sensor_id, status, attempts, last_error, queued_at, processed_at) VALUES (?, ?, ?, 0, '', ?, 0)")
                        .Bind(jobId, sensor.SensorId, "queued", now));

                await _db.ExecuteAsync(
                    _db.Prepare("UPDATE sensors SET next_read_at = ? WHERE sensor_id = ?")
                        .Bind(anchor + (sensor.IntervalMinutes * 60.0 * 1000.0), sensor.SensorId));

                var request = new MessageSendRequest<QueuedMessage>
                {
                    Body = new QueuedMessage(sensor.SensorId, "", jobId, now),
                    ContentType = QueueContentType.Json
                };
                messages.Add(request);
                sensorIds.Add(sensor.SensorId);
            }

            if (messages.Count != 0)
                await _queue.SendBatchAsync(messages);

            Log.Info($"Telemetry: queued {messages.Count} sensor job(s)");
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
                .InvokeAsync<Lease>("reserve", message.JobId);
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
                TimeoutSeconds * 1000.0);

            var readingAt = Clock.NowMs();
            var temperature = 0.0;
            var airQuality = 0.0;
            var condition = "unknown";
            var source = LiveSource;

            try
            {
                var options = new RequestInit { Signal = controller.Signal };
                var responses = await Task.WhenAll(
                    Http.FetchAsync(ForecastUrl(latitude, longitude), options),
                    Http.FetchAsync(AirQualityUrl(latitude, longitude), options));

                if (!responses[0].Ok || !responses[1].Ok)
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
                Log.Error($"Weather API unavailable ({exception.Message}), storing a simulated reading instead");
                temperature = 12 + (RandomUnit() * 10);
                airQuality = 4 + (RandomUnit() * 18);
                condition = RandomCondition();
                source = SimulatedSource;
            }
            finally
            {
                Timers.ClearTimeout(timeout);
            }

            var processedAt = Clock.NowMs();

            await _db.ExecuteAsync(
                _db.Prepare("INSERT OR REPLACE INTO readings (id, sensor_id, temperature, air_quality, condition, source, reading_at, processed_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)")
                    .Bind(message.JobId, message.Text, temperature, airQuality, condition, source, readingAt, processedAt));

            await MarkJobAsync(message.JobId, message.Text, "done", attempts, "");

            await _gateNs.GetByName(GateName).InvokeAsync("complete", lease.Token);

            Log.Info($"Telemetry: sensor {message.Text} stored a {source} reading ({condition})");
        }

        /// <summary>Records how a job ended, so the UI can show its state.</summary>
        public async Task MarkJobAsync(string jobId, string sensorId, string status, int attempts, string error)
        {
            var processedAt = Clock.NowMs();

            var changed = await _db.ExecuteChangesAsync(
                _db.Prepare("UPDATE jobs SET status = ?, attempts = ?, last_error = ?, processed_at = ? WHERE id = ?")
                    .Bind(status, attempts, error, processedAt, jobId));

            if (changed != 0)
                return;

            // The job row was pruned while its message was still in the queue.
            // Put it back so the reading it produced is still counted.
            await _db.ExecuteAsync(
                _db.Prepare("INSERT OR REPLACE INTO jobs (id, sensor_id, status, attempts, last_error, queued_at, processed_at) VALUES (?, ?, ?, ?, ?, ?, ?)")
                    .Bind(jobId, sensorId, status, attempts, error, processedAt, processedAt));
        }

        /// <summary>Makes every sensor due again (POST /api/telemetry/reset).</summary>
        public async Task ResetAsync()
        {
            await _db.ExecuteAsync(_db.Prepare("UPDATE sensors SET active = 1, next_read_at = 0"));
        }

        /// <summary>
        /// Deletes every reading and job from before the current UTC day, plus
        /// any job that has been stuck in the queue for longer than
        /// <see cref="SampleConfig.TelemetryStaleJobMinutes"/>. The cron trigger
        /// runs this after queuing, so the tables only ever hold today's rows and
        /// a status read can never grow with the age of the demo.
        /// </summary>
        public async Task PruneAsync()
        {
            var now = Clock.NowMs();
            var dayMs = 24.0 * 60.0 * 60.0 * 1000.0;
            var startOfDay = now - (now % dayMs);

            // The first run after a deploy can have weeks of rows to remove, so
            // delete in bounded chunks instead of one unbounded statement.
            await DeleteInChunksAsync("readings", "reading_at", startOfDay);
            await DeleteInChunksAsync("jobs", "queued_at", startOfDay);

            var staleCutoff = now - (SampleConfig.TelemetryStaleJobMinutes * 60.0 * 1000.0);
            await _db.ExecuteAsync(
                _db.Prepare("DELETE FROM jobs WHERE status IN ('queued', 'retrying') AND queued_at < ?").Bind(staleCutoff));

            Log.Info($"Telemetry: pruned rows from before {SampleConfig.TelemetryRetentionDays} day(s)");
        }

        /// <summary>Deletes rows older than <paramref name="cutoff"/> in batches.</summary>
        async Task DeleteInChunksAsync(string table, string timeColumn, double cutoff)
        {
            for (var pass = 0; pass < 20; pass++)
            {
                var removed = await _db.ExecuteChangesAsync(
                    _db.Prepare($"DELETE FROM {table} WHERE rowid IN (SELECT rowid FROM {table} WHERE {timeColumn} < ? LIMIT 5000)")
                        .Bind(cutoff));

                if (removed < 5000)
                    return;
            }
        }

        /// <summary>Everything GET /api/telemetry returns.</summary>
        /// <remarks>
        /// One aggregate query and two bounded lists, so the cost depends on the
        /// retention window rather than on how many readings have ever been
        /// stored. The previous version ran a correlated COUNT per sensor plus
        /// five separate COUNT(*) queries, which made one read cost
        /// O(sensors x readings) rows - the reason a few hundred requests could
        /// exhaust the D1 free tier.
        /// </remarks>
        public async Task<TelemetryStatus> ReadStatusAsync()
        {
            var sensorRows = await _db.AllAsync<SensorRow>(
                _db.Prepare("SELECT sensor_id AS sensorId, name, latitude, longitude, interval_minutes AS intervalMinutes, next_read_at AS nextReadAtMs, active FROM sensors ORDER BY sensor_id"));

            // Every stored reading is written by a job that then moves to 'done',
            // so the done count is the reading count. Counting jobs instead of
            // readings keeps this to one aggregate over one table.
            var jobRows = await _db.AllAsync<JobStatusCount>(
                _db.Prepare("SELECT sensor_id AS sensorId, status, COUNT(*) AS jobCount, COALESCE(SUM(attempts), 0) AS attempts FROM jobs GROUP BY sensor_id, status"));

            var sensors = new List<SensorInfo>();
            var totalReadings = 0;
            foreach (var row in sensorRows)
            {
                var reads = 0;
                var pending = 0;
                foreach (var job in jobRows)
                {
                    if (job.SensorId != row.SensorId)
                        continue;

                    if (job.Status == "done")
                        reads += job.JobCount;
                    else if (job.Status != "failed")
                        pending += job.JobCount;
                }

                sensors.Add(new SensorInfo(
                    row.SensorId,
                    row.Name,
                    row.Latitude,
                    row.Longitude,
                    row.IntervalMinutes,
                    row.NextReadAtMs,
                    row.Active,
                    reads,
                    pending));
                totalReadings += reads;
            }

            var queued = 0;
            var done = 0;
            var failed = 0;
            var attempts = 0;
            foreach (var job in jobRows)
            {
                attempts += job.Attempts;

                if (job.Status == "done")
                    done += job.JobCount;
                else if (job.Status == "failed")
                    failed += job.JobCount;
                else
                    queued += job.JobCount;
            }

            var readings = await _db.AllAsync<ReadingInfo>(
                _db.Prepare($"SELECT id, sensor_id AS sensorId, temperature, air_quality AS airQuality, condition, source, reading_at AS readingAtMs, processed_at AS processedAtMs FROM readings ORDER BY reading_at DESC, id LIMIT {ReadingLimit}"));

            var jobs = await _db.AllAsync<JobInfo>(
                _db.Prepare($"SELECT id, sensor_id AS sensorId, status, attempts, last_error AS lastError, queued_at AS queuedAtMs, processed_at AS processedAtMs FROM jobs ORDER BY queued_at DESC LIMIT {JobLimit}"));

            var stats = new PipelineStats(sensors.Count, totalReadings, queued, done, failed, attempts);

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
                var lease = await _gateNs.GetByName(GateName).InvokeAsync<Lease>("peek");

                if (lease is null || !lease.Allowed)
                    return new GateInfo(false, "", SampleConfig.TelemetryRateLimitSeconds, 0);

                return new GateInfo(true, lease.Owner, SampleConfig.TelemetryRateLimitSeconds, lease.ExpiresAtMs);
            }
            catch (Exception exception)
            {
                Log.Error($"Could not read the rate gate ({exception.Message})");
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

        /// <summary>
        /// A random number in [0, 1). The Workers C# profile has no
        /// <c>Random</c>, so the bytes come from the runtime's crypto source.
        /// </summary>
        static double RandomUnit()
        {
            var bytes = Crypto.GetRandomValues(new byte[4]);
            var value = (bytes[0] * 16777216.0) + (bytes[1] * 65536.0) + (bytes[2] * 256.0) + bytes[3];
            return value / 4294967296.0;
        }

        /// <summary>Picks a plausible condition for a simulated reading.</summary>
        static string RandomCondition()
        {
            var roll = RandomUnit();

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

    /// <summary>One sensor, from the status query.</summary>
    public sealed record SensorRow(
        string SensorId,
        string Name,
        double Latitude,
        double Longitude,
        int IntervalMinutes,
        double NextReadAtMs,
        int Active);

    /// <summary>One (sensor, status) bucket of the job rollup.</summary>
    public sealed record JobStatusCount(string SensorId, string Status, int JobCount, int Attempts);
}
