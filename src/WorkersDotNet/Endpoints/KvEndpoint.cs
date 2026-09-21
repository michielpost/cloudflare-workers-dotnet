using System.Threading.Tasks;
using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: a KV namespace (<c>env.Kv("KV")</c>, namespace title
    /// <c>dotnet_test</c>). Thin controller: reads the request and maps the
    /// <see cref="KvSampleService"/> result to a response.
    /// </summary>
    public sealed class KvEndpoint
    {
        private readonly KvSampleService _kv;

        public KvEndpoint(KvSampleService kv)
        {
            _kv = kv;
        }

        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method == "POST")
                return await WriteAsync(request);

            var snapshot = await _kv.ReadAsync();
            return Response.Json(snapshot, 200)
                .WithHeader("cache-control", "no-store");
        }

        async Task<Response> WriteAsync(Request request)
        {
            KvWriteRequest? input;
            try
            {
                input = await request.JsonAsync<KvWriteRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"key\" and \"value\" is required", 400);

            var result = await _kv.WriteAsync(input);
            if (result.Error is not null)
                return Results.Error(result.Error, result.Status);

            return Response.Json(result.Result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
