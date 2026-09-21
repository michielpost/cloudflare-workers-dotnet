using System.Threading.Tasks;
using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// The handlers that are not HTTP requests: the queue consumer and the
    /// scheduled (cron) task.
    /// </summary>
    /// <remarks>
    /// A worker can register exactly one queue handler and one scheduled handler,
    /// but several samples in this repo use them, so both handlers here are
    /// dispatchers: the queue one looks at <c>batch.Queue</c> and the scheduled one
    /// at <c>scheduled.Cron</c>, then forwards to the right sample's service.
    /// Everything the handlers produce is written to a binding - KV or D1 - which
    /// is the only way to hand a result back to a later HTTP request, because a
    /// worker keeps no state between invocations.
    /// </remarks>
    public static class WorkerEvents
    {
        /// <summary>
        /// Consumes the messages of both queues: <c>dotnet-queue</c> for the queue
        /// sample (the text plus its dates into KV) and <c>dotnet-telemetry</c> for
        /// the telemetry pipeline (one reading per message into D1).
        /// </summary>
        [Queue]
        public static async Task ConsumeAsync(
            QueueMessageBatch<QueuedMessage> batch,
            Env environment,
            Context context)
        {
            var app = new AppServices(environment);

            Console.WriteLine($"Queue consumer received {batch.Count} message(s) from {batch.Queue}");

            if (batch.Queue == SampleConfig.TelemetryQueueName)
            {
                await ConsumeTelemetryAsync(batch, app);
                return;
            }

            foreach (var message in batch)
            {
                var record = new QueueRecord(
                    message.Body.Text,
                    message.Body.QueuedAtUtc,
                    DateTimeOffset.UtcNow.ToString("O"),
                    message.Id);

                await app.QueueSample.WriteQueueResultAsync(record);
                message.Ack();

                Console.WriteLine($"Queue consumer stored \"{record.Text}\" ({record.MessageId})");
            }
        }

        /// <summary>
        /// The telemetry pipeline's consumer. Every message is handled on its own:
        /// a job that fails is retried with a delay that grows per attempt, and it
        /// is only acked once it either succeeded or ran out of attempts. One bad
        /// sensor therefore never blocks the rest of the batch.
        /// </summary>
        static async Task ConsumeTelemetryAsync(QueueMessageBatch<QueuedMessage> batch, AppServices app)
        {
            foreach (var message in batch)
            {
                var attempts = message.Attempts;

                try
                {
                    await app.TelemetryService.ProcessJobAsync(message.Body, attempts);
                    message.Ack();
                }
                catch (Exception exception)
                {
                    var delay = attempts * 10;
                    if (delay < 10)
                        delay = 10;

                    if (attempts < SampleConfig.TelemetryMaxAttempts)
                    {
                        Console.Error.WriteLine($"Telemetry job {message.Body.JobId} failed on attempt {attempts}, retrying in {delay}s: {exception.Message}");
                        await app.TelemetryService.MarkJobAsync(message.Body.JobId, "retrying", attempts, exception.Message);
                        message.Retry(new QueueRetryOptions { DelaySeconds = delay });
                    }
                    else
                    {
                        Console.Error.WriteLine($"Telemetry job {message.Body.JobId} failed {attempts} times and is given up: {exception.Message}");
                        await app.TelemetryService.MarkJobAsync(message.Body.JobId, "failed", attempts, exception.Message);
                        message.Ack();
                    }
                }
            }
        }

        /// <summary>
        /// Runs the cron triggers: the telemetry pipeline every 5 minutes, the
        /// scheduled-task sample every hour. The work happens after the event
        /// returns, so it is handed to <c>context.WaitUntil</c>.
        /// </summary>
        [Scheduled]
        public static void OnSchedule(ScheduledEvent scheduled, Env environment, Context context)
        {
            var app = new AppServices(environment);

            Console.WriteLine($"Scheduled task {scheduled.Cron} fired for {scheduled.ScheduledTime:O}");

            if (scheduled.Cron == SampleConfig.TelemetryCron)
            {
                context.WaitUntil(app.TelemetryService.EnqueueDueAsync());
                return;
            }

            context.WaitUntil(
                app.ScheduledSample.WriteRunAsync(
                    scheduled.Cron,
                    scheduled.ScheduledTime.ToString("O"),
                    false));
        }
    }
}
