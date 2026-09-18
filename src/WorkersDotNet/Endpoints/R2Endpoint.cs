using System.Text.RegularExpressions;
using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: an R2 bucket (<c>env.R2("R2")</c>) holding a single object.
    /// </summary>
    /// <remarks>
    /// Uploads are capped at <see cref="MaxBytes"/> bytes and are always stored
    /// under <see cref="ObjectKey"/>, so a new upload overwrites the previous
    /// file. GET returns the metadata of the stored object, GET
    /// <c>/api/r2/download</c> streams the file back and POST
    /// <c>/api/r2/delete</c> removes it. Locally the object lives in the
    /// wrangler R2 simulator under <c>.wrangler/state/v3/r2</c>.
    /// </remarks>
    public static class R2Endpoint
    {
        const int MaxBytes = SampleConfig.R2MaxBytes;
        const string Binding = "R2";
        const string BucketName = SampleConfig.R2BucketName;
        const string ObjectKey = SampleConfig.R2ObjectKey;

        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            var bucket = environment.R2(Binding);

            if (request.Method == "POST")
                return await UploadAsync(request, bucket);

            var info = await DescribeAsync(bucket);
            return Response.Json(info, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>Streams the stored object back to the client.</summary>
        public static async Task<Response> DownloadAsync(Request request, Env environment)
        {
            var bucket = environment.R2(Binding);
            var item = await bucket.GetObjectAsync(ObjectKey);

            if (item is null)
                return Results.Error($"{ObjectKey} has not been uploaded yet.", 404);

            var headers = new Headers();
            item.WriteHttpMetadata(headers);
            headers.Set("content-disposition", $"attachment; filename=\"{ObjectKey}\"");
            headers.Set("cache-control", "no-store");

            return Response.FromStream(item.Body, headers)
                .WithHeader("etag", item.HttpEtag)
                .WithHeader("x-r2-bucket", BucketName)
                .WithHeader("x-r2-key", item.Key);
        }

        public static async Task<Response> DeleteAsync(Request request, Env environment)
        {
            var bucket = environment.R2(Binding);
            await bucket.DeleteAsync(ObjectKey);

            var info = await DescribeAsync(bucket);
            return Response.Json(
                    new R2MutationResult(true, $"Deleted {ObjectKey} from {BucketName}.", info),
                    200)
                .WithHeader("cache-control", "no-store");
        }

        static async Task<Response> UploadAsync(Request request, IR2Bucket bucket)
        {
            // Content-Length is only a pre-check: the size of the stored object
            // is verified below, because the header cannot be trusted.
            var declared = request.Headers.Get("content-length");
            if (declared is not null && Regex.IsMatch(declared, "^[0-9]{1,9}$") && int.Parse(declared) > MaxBytes)
                return Results.Error($"The upload is {declared} bytes, the limit is {MaxBytes} bytes.", 413);

            var body = request.BodyStream();
            if (body is null)
                return Results.Error("The request body is empty.", 400);

            var contentType = request.Headers.Get("content-type");
            if (contentType is null || contentType.Length == 0)
                contentType = "application/octet-stream";

            var stored = await bucket.PutObjectAsync(
                ObjectKey,
                body,
                new R2PutOptions { HttpMetadata = new R2HttpMetadata(ContentType: contentType) });

            if (stored is null)
                return Results.Error("The upload could not be stored in R2.", 500);

            if (stored.Size > MaxBytes)
            {
                await bucket.DeleteAsync(ObjectKey);
                return Results.Error($"The upload is {stored.Size} bytes, the limit is {MaxBytes} bytes.", 413);
            }

            return Response.Json(
                    new R2MutationResult(true, $"Stored {stored.Size} bytes as {stored.Key}.", InfoOf(stored)),
                    200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>Metadata of the stored object, or an empty description when nothing is stored.</summary>
        static async Task<R2FileInfo> DescribeAsync(IR2Bucket bucket)
        {
            var item = await bucket.HeadAsync(ObjectKey);
            return InfoOf(item);
        }

        static R2FileInfo InfoOf(R2Object? item)
        {
            if (item is null)
                return new R2FileInfo(false, ObjectKey, BucketName, 0, MaxBytes, "", "", "");

            var contentType = "";
            if (item.HttpMetadata is not null && item.HttpMetadata.ContentType is not null)
                contentType = item.HttpMetadata.ContentType;

            return new R2FileInfo(
                true,
                item.Key,
                BucketName,
                item.Size,
                MaxBytes,
                contentType,
                item.Uploaded.ToString("O"),
                item.HttpEtag);
        }
    }
}
