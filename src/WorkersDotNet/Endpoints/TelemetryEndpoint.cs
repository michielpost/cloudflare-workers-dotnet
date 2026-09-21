using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: an end-to-end pipeline that uses almost every binding at once -
    /// cron trigger, D1, queues, a Durable Object as a rate gate and outbound
    /// HTTP - to poll three weather sensors. Thin controller: reads the request
    /// and maps the <see cref="TelemetryService"/> result to a response. All the
    /// pipeline logic lives in <see cref="TelemetryService"/>.
    /// </summary>
    public sealed class TelemetryEndpoint
    {
        private readonly TelemetryService _telemetry;

        public TelemetryEndpoint(TelemetryService telemetry)
        {
            _telemetry = telemetry;
        }

        /// <summary>GET returns the pipeline state, POST runs the cron work now.</summary>
        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method == "POST")
                return await RunAsync();

            if (request.Method != "GET")
                return Results.Error("Only GET and POST are supported on /api/telemetry", 405);

            return Response.Json(await _telemetry.ReadStatusAsync(), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// Runs what the cron trigger does - queue every due sensor - and answers
        /// with the state of the pipeline. Local development has no cron
        /// scheduler, so the UI calls this to start a run by hand.
        /// </summary>
        public async Task<Response> RunAsync()
        {
            var sensorIds = await _telemetry.EnqueueDueAsync();
            var status = await _telemetry.ReadStatusAsync();

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
        public async Task<Response> ResetAsync()
        {
            await _telemetry.ResetAsync();
            var status = await _telemetry.ReadStatusAsync();
            var message = $"{status.Stats.Sensors} sensor(s) set to active and due now.";

            var result = new TelemetryRunResult(true, message, 0, new List<string>(), status);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
