using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// A small wrapper around <c>caches.default</c> for the read endpoints.
    /// </summary>
    /// <remarks>
    /// The cache key is the request's origin plus a fixed path, so a caller cannot
    /// dodge the cache by appending a query string. A hit is returned with
    /// <c>x-cache: hit</c>; a miss is stored for the given number of seconds and
    /// returned with <c>x-cache: miss</c>. Mutations purge the entry, so the next
    /// read is fresh.
    /// </remarks>
    public static class ResponseCache
    {
        /// <summary>The cache key for a path on the request's origin.</summary>
        public static string KeyFor(Request request, string path)
        {
            return new Url(request.Url).Origin + path;
        }

        /// <summary>The cached response, or null when there is none.</summary>
        public static async Task<Response?> MatchAsync(string key)
        {
            var cached = await CacheStorage.Default.MatchAsync(key);
            if (cached is null)
                return null;

            // A response from the Cache API has immutable headers, so the hit
            // marker goes on a copy.
            var headers = new Headers(cached.Headers);
            headers.Set("x-cache", "hit");
            headers.Set("cache-control", "no-store");
            return Results.Copy(cached, headers);
        }

        /// <summary>
        /// Stores a response that is only used for the cache. The caller returns
        /// its own response instance, because a body can only be handed to the
        /// Response constructor once.
        /// </summary>
        public static void Store(ExecutionContext context, string key, Response stored, int maxAgeSeconds)
        {
            stored.Headers.Set("cache-control", $"max-age={maxAgeSeconds}");
            context.WaitUntil(CacheStorage.Default.PutAsync(key, stored));
        }

        /// <summary>Drops the entry so the next read is a miss.</summary>
        public static Task PurgeAsync(string key)
        {
            return CacheStorage.Default.DeleteAsync(key);
        }
    }
}
