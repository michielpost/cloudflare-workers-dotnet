using System.Threading.Tasks;
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
    public sealed class ScheduledEndpoint
    {
        private readonly ScheduledSampleService _scheduled;

        public ScheduledEndpoint(ScheduledSampleService scheduled)
        {
            _scheduled = scheduled;
        }

        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method == "POST")
            {
                await _scheduled.WriteRunAsync(
                    SampleConfig.ScheduledCron,
                    DateTimeOffset.UtcNow.ToString("O"),
                    true);
            }

            return Response.Json(await _scheduled.ReadStatusAsync(), 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
