using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: an R2 bucket (<c>env.R2("R2")</c>) holding a single object. Thin
    /// controller: reads the request (method, body stream, headers) and maps the
    /// <see cref="R2SampleService"/> result to a response.
    /// </summary>
    public sealed class R2Endpoint
    {
        private readonly R2SampleService _r2;

        public R2Endpoint(R2SampleService r2)
        {
            _r2 = r2;
        }

        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method == "POST")
                return await UploadAsync(request);

            var info = await _r2.DescribeAsync();
            return Results.NoStore(Response.Json(info, 200));
        }

        /// <summary>Streams the stored object back to the client.</summary>
        public async Task<Response> DownloadAsync(Request request)
        {
            var item = await _r2.GetObjectAsync();

            if (item is null)
                return Results.Error($"{_r2.ObjectName} has not been uploaded yet.", 404);

            var headers = new Headers();
            item.WriteHttpMetadata(headers);
            headers.Set("content-disposition", $"attachment; filename=\"{_r2.ObjectName}\"");
            headers.Set("cache-control", "no-store");
            headers.Set("etag", item.HttpEtag);
            headers.Set("x-r2-bucket", SampleConfig.R2BucketName);
            headers.Set("x-r2-key", item.Key);

            // A Response is now also the ResponseInit of the next Response, so the
            // headers are attached by building the init first.
            var init = new Response(null) { Headers = headers };
            return new Response(item.Body, init);
        }

        public async Task<Response> DeleteAsync(Request request)
        {
            await _r2.DeleteAsync();

            var info = await _r2.DescribeAsync();
            return Results.NoStore(Response.Json(
                new R2MutationResult(true, $"Deleted {_r2.ObjectName} from {SampleConfig.R2BucketName}.", info),
                200));
        }

        async Task<Response> UploadAsync(Request request)
        {
            // Content-Length is only a pre-check: the size of the stored object
            // is verified in the service, because the header cannot be trusted.
            var declared = request.Headers.Get("content-length");
            if (declared is not null && Text.IsDigits(declared, 1, 9) && int.Parse(declared) > SampleConfig.R2MaxBytes)
                return Results.Error($"The upload is {declared} bytes, the limit is {SampleConfig.R2MaxBytes} bytes.", 413);

            var body = request.Body;
            if (body is null)
                return Results.Error("The request body is empty.", 400);

            var contentType = request.Headers.Get("content-type");
            if (contentType is null || contentType.Length == 0)
                contentType = "application/octet-stream";

            var result = await _r2.UploadAsync(body, contentType);
            if (result.Error is not null)
                return Results.Error(result.Error, result.Status);

            return Results.NoStore(Response.Json(result.Result, 200));
        }
    }
}
