using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: a scheduled (cron) task that runs every hour and writes its result
    /// to a KV key. Thin controller: reads the request and maps the
    /// <see cref="ScheduledSampleService"/> result to a response.
    /// </summary>
    public static class ScheduledEndpoint
    {
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            var kv = environment.Kv("KV");

            if (request.Method == "POST")
            {
                await ScheduledSampleService.WriteRunAsync(
                    kv,
                    SampleConfig.ScheduledCron,
                    DateTimeOffset.UtcNow.ToString("O"),
                    true);
            }

            return Response.Json(await ScheduledSampleService.ReadStatusAsync(kv), 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
