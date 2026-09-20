using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: the producer half of a Cloudflare queue (<c>env.Queue("QUEUE")</c>,
    /// queue <c>dotnet-queue</c>). Thin controller: reads the request and maps the
    /// <see cref="QueueSampleService"/> result to a response.
    /// </summary>
    public static class QueueEndpoint
    {
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            if (request.Method == "POST")
                return await ProduceAsync(request, environment.Queue("QUEUE"));

            var status = await QueueSampleService.ReadStatusAsync(environment.Kv("KV"));
            return Response.Json(status, 200)
                .WithHeader("cache-control", "no-store");
        }

        static async Task<Response> ProduceAsync(Request request, IQueueProducer queue)
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

            var result = await QueueSampleService.ProduceAsync(queue, input.Text.Trim());
            if (result.Error is not null)
                return Results.Error(result.Error, result.Status);

            return Response.Json(result.Result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
