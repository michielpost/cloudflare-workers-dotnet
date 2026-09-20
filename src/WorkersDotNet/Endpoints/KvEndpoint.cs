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
    public static class KvEndpoint
    {
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            var kv = environment.Kv("KV");

            if (request.Method == "POST")
                return await WriteAsync(request, kv);

            var snapshot = await KvSampleService.ReadAsync(kv);
            return Response.Json(snapshot, 200)
                .WithHeader("cache-control", "no-store");
        }

        static async Task<Response> WriteAsync(Request request, IKvNamespace kv)
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

            var result = await KvSampleService.WriteAsync(kv, input);
            if (result.Error is not null)
                return Results.Error(result.Error, result.Status);

            return Response.Json(result.Result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
