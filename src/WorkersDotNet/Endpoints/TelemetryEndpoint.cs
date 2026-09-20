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
    public static class TelemetryEndpoint
    {
        /// <summary>GET returns the pipeline state, POST runs the cron work now.</summary>
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            if (request.Method == "POST")
                return await RunAsync(environment);

            if (request.Method != "GET")
                return Results.Error("Only GET and POST are supported on /api/telemetry", 405);

            return Response.Json(await ReadStatusAsync(environment), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// Runs what the cron trigger does - queue every due sensor - and answers
        /// with the state of the pipeline. Local development has no cron
        /// scheduler, so the UI calls this to start a run by hand.
        /// </summary>
        public static async Task<Response> RunAsync(Env environment)
        {
            var sensorIds = await TelemetryService.EnqueueDueAsync(
                environment.D1("DB"),
                environment.Queue("TELEMETRY"));
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
            await TelemetryService.ResetAsync(environment.D1("DB"));
            var status = await ReadStatusAsync(environment);
            var message = $"{status.Stats.Sensors} sensor(s) set to active and due now.";

            var result = new TelemetryRunResult(true, message, 0, new List<string>(), status);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        static async Task<TelemetryStatus> ReadStatusAsync(Env environment)
        {
            return await TelemetryService.ReadStatusAsync(
                environment.D1("DB"),
                environment.DurableObject("RATE_GATE"));
        }
    }
}
