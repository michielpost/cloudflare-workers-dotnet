using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The R2 sample's business logic: describing, downloading, uploading and
    /// deleting the single stored object. The endpoint stays a thin controller;
    /// this service calls the R2 bucket binding directly.
    /// </summary>
    public static class R2SampleService
    {
        /// <summary>The result of a mutation: the new file info, or an error to surface.</summary>
        public sealed record Outcome(R2MutationResult? Result, string? Error, int Status);

        const int MaxBytes = SampleConfig.R2MaxBytes;
        const string BucketName = SampleConfig.R2BucketName;
        const string ObjectKey = SampleConfig.R2ObjectKey;

        public const string ObjectName = SampleConfig.R2ObjectKey;

        public static Task<R2ObjectBody?> GetObjectAsync(IR2Bucket r2) => r2.GetObjectAsync(ObjectKey);

        public static Task DeleteAsync(IR2Bucket r2) => r2.DeleteAsync(ObjectKey);

        /// <summary>
        /// Stores the uploaded stream under the fixed key and verifies the size.
        /// Content-Length is only a pre-check done by the endpoint; the stored
        /// size is what actually enforces the limit here.
        /// </summary>
        public static async Task<Outcome> UploadAsync(IR2Bucket r2, ReadableStream body, string contentType)
        {
            var stored = await r2.PutObjectAsync(
                ObjectKey,
                body,
                new R2PutOptions { HttpMetadata = new R2HttpMetadata(ContentType: contentType) });

            if (stored is null)
                return new Outcome(null, "The upload could not be stored in R2.", 500);

            if (stored.Size > MaxBytes)
            {
                await r2.DeleteAsync(ObjectKey);
                return new Outcome(null, $"The upload is {stored.Size} bytes, the limit is {MaxBytes} bytes.", 413);
            }

            return new Outcome(
                new R2MutationResult(true, $"Stored {stored.Size} bytes as {stored.Key}.", InfoOf(stored)),
                null,
                200);
        }

        /// <summary>Metadata of the stored object, or an empty description when nothing is stored.</summary>
        public static async Task<R2FileInfo> DescribeAsync(IR2Bucket r2)
        {
            var item = await r2.HeadAsync(ObjectKey);
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
