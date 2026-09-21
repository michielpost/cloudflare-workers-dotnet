using System.Threading.Tasks;
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
    public sealed class QueueEndpoint
    {
        private readonly QueueSampleService _queue;

        public QueueEndpoint(QueueSampleService queue)
        {
            _queue = queue;
        }

        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method == "POST")
                return await ProduceAsync(request);

            var status = await _queue.ReadStatusAsync();
            return Response.Json(status, 200)
                .WithHeader("cache-control", "no-store");
        }

        async Task<Response> ProduceAsync(Request request)
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

            var result = await _queue.ProduceAsync(input.Text.Trim());
            if (result.Error is not null)
                return Results.Error(result.Error, result.Status);

            return Response.Json(result.Result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
