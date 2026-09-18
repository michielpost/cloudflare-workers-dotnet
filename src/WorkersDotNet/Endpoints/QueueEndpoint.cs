using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: the producer half of a Cloudflare queue (<c>env.Queue("QUEUE")</c>,
    /// queue <c>dotnet-queue</c>).
    /// </summary>
    /// <remarks>
    /// POST puts a message of at most <see cref="MaxTextLength"/> characters on the
    /// queue, together with the date it was queued. GET reads the results the
    /// consumer (<see cref="WorkerEvents"/>) wrote to KV, so the frontend can show
    /// the queue text plus the queued and processed dates.
    /// Locally wrangler runs both the producer and the consumer, and the messages
    /// stay on this machine.
    /// </remarks>
    public static class QueueEndpoint
    {
        const string Binding = "QUEUE";
        const string KvBinding = "KV";

        const int MaxTextLength = SampleConfig.QueueMaxTextLength;
        const string QueueName = SampleConfig.QueueName;
        const string NamespaceName = SampleConfig.KvNamespaceName;
        const string ResultKey = SampleConfig.QueueResultKey;
        const string HistoryKey = SampleConfig.QueueHistoryKey;

        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            if (request.Method == "POST")
                return await ProduceAsync(request, environment);

            var status = await ReadStatusAsync(environment);
            return Response.Json(status, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>Puts one message on the queue and reports when it was queued.</summary>
        static async Task<Response> ProduceAsync(Request request, Env environment)
        {
            QueueMessageInput? input;
            try
            {
                input = await request.JsonAsync<QueueMessageInput>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null || input.Text is null)
                return Results.Error("A JSON body with \"text\" is required", 400);

            var text = input.Text.Trim();

            if (text.Length == 0)
                return Results.Error("The message must not be empty", 400);

            if (text.Length > MaxTextLength)
                return Results.Error(
                    $"The message is {text.Length} characters, the limit is {MaxTextLength}.",
                    400);

            var queuedAt = DateTimeOffset.UtcNow.ToString("O");
            await environment.Queue(Binding)
                .SendJsonAsync(new QueuedMessage(text, queuedAt, "", 0));

            var result = new QueueSendResult(
                true,
                $"Message added to {QueueName}.",
                text,
                queuedAt);

            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>Reads what the queue consumer wrote to KV.</summary>
        static async Task<QueueStatus> ReadStatusAsync(Env environment)
        {
            var kv = environment.Kv(KvBinding);

            QueueRecord? latest = null;
            try
            {
                latest = await kv.GetJsonAsync<QueueRecord>(ResultKey);
            }
            catch (Exception)
            {
                latest = null;
            }

            // The history is only read when there is a latest result, otherwise
            // the very first call would always log a parse failure.
            var items = new List<QueueRecord>();
            if (latest is not null)
            {
                var history = await kv.GetJsonAsync<QueueHistory>(HistoryKey);
                if (history is not null && history.Items is not null)
                {
                    foreach (var item in history.Items)
                        items.Add(item);
                }
            }

            return new QueueStatus(latest, items, QueueName, NamespaceName, ResultKey, MaxTextLength);
        }
    }
}
