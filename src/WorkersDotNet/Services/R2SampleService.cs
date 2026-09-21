using System.Threading.Tasks;
using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The R2 sample's business logic: describing, downloading, uploading and
    /// deleting the single stored object. The endpoint stays a thin controller;
    /// this service calls the injected R2 bucket binding directly.
    /// </summary>
    /// <summary>The result of a mutation: the new file info, or an error to surface.</summary>
    public sealed record R2Outcome(R2MutationResult? Result, string? Error, int Status);

    public sealed class R2SampleService
    {

        readonly ulong MaxBytes = SampleConfig.R2MaxBytes;
        readonly string BucketName = SampleConfig.R2BucketName;
        readonly string ObjectKey = SampleConfig.R2ObjectKey;

        /// <summary>The name of the stored object, for messages and the download name.</summary>
        public string ObjectName { get; }

        private readonly IR2Bucket _r2;

        public R2SampleService(IR2Bucket r2)
        {
            _r2 = r2;
            ObjectName = SampleConfig.R2ObjectKey;
        }

        public Task<R2ObjectBody?> GetObjectAsync() => _r2.GetObjectAsync(ObjectKey);

        public Task DeleteAsync() => _r2.DeleteAsync(ObjectKey);

        /// <summary>
        /// Stores the uploaded stream under the fixed key and verifies the size.
        /// Content-Length is only a pre-check done by the endpoint; the stored
        /// size is what actually enforces the limit here.
        /// </summary>
        public async Task<R2Outcome> UploadAsync(ReadableStream body, string contentType)
        {
            var stored = await _r2.PutObjectAsync(
                ObjectKey,
                body,
                new R2PutOptions { HttpMetadata = new R2HttpMetadata(ContentType: contentType) });

            if (stored is null)
                return new R2Outcome(null, "The upload could not be stored in R2.", 500);

            if (stored.Size > MaxBytes)
            {
                await _r2.DeleteAsync(ObjectKey);
                return new R2Outcome(null, $"The upload is {stored.Size} bytes, the limit is {MaxBytes} bytes.", 413);
            }

            return new R2Outcome(
                new R2MutationResult(true, $"Stored {stored.Size} bytes as {stored.Key}.", InfoOf(stored)),
                null,
                200);
        }

        /// <summary>Metadata of the stored object, or an empty description when nothing is stored.</summary>
        public async Task<R2FileInfo> DescribeAsync()
        {
            var item = await _r2.HeadAsync(ObjectKey);
            return InfoOf(item);
        }

        R2FileInfo InfoOf(R2Object? item)
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
