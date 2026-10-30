using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: a Durable Object used as a rate gate. One named instance
    /// (<c>RATE_GATE</c> -> "outbound-api") hands out a lease for a single
    /// outbound call at a time.
    /// </summary>
    /// <remarks>
    /// A Durable Object is the only place in a worker with real, consistent
    /// state: it runs as one instance and serialises the calls it receives. That
    /// makes it the natural home for a lease, a counter or a lock. The lease is
    /// kept in the object's storage, so it also survives an eviction.
    /// The queue consumer calls <c>reserve</c> before it calls the weather API and
    /// <c>complete</c> when it is done; the lease is never held longer than
    /// <see cref="LeaseSeconds"/> seconds, so a crashed consumer cannot block the
    /// pipeline forever.
    /// <para>
    /// Since Workers 0.6 a Durable Object is a class deriving from
    /// <see cref="DurableObject"/> rather than a class carrying a
    /// <c>[DurableObject]</c> attribute. The base class supplies
    /// <see cref="DurableObject.Ctx"/> and <see cref="DurableObject.Env"/>, so no
    /// constructor is needed, and its public methods are the RPC surface the
    /// stub invokes by name.
    /// </para>
    /// </remarks>
    public sealed class RateGate : DurableObject
    {
        const string ExpiresKey = "lease_expires_at";
        const string OwnerKey = "lease_owner";
        const string TokenKey = "lease_token";
        const int LeaseSeconds = SampleConfig.TelemetryRateLimitSeconds;

        /// <summary>
        /// Grants a lease when no other lease is active, and refuses when one is.
        /// The caller passes the id of the job it is about to run, which is stored
        /// as the lease owner and returned as the token.
        /// </summary>
        public async Task<Lease> ReserveAsync(string requestId)
        {
            var expiresAt = await Ctx.Storage.GetAsync<double>(ExpiresKey);

            if (expiresAt > Clock.NowMs())
            {
                var busyOwner = await Ctx.Storage.GetAsync<string>(OwnerKey);
                return new Lease(false, "", busyOwner is null ? "" : busyOwner, expiresAt);
            }

            var token = requestId;
            if (token is null || token.Length == 0)
                token = Crypto.RandomUuid();

            var expires = Clock.NowMs() + (LeaseSeconds * 1000.0);
            await Ctx.Storage.PutAsync(ExpiresKey, expires);
            await Ctx.Storage.PutAsync(OwnerKey, requestId);
            await Ctx.Storage.PutAsync(TokenKey, token);

            return new Lease(true, token, requestId, expires);
        }

        /// <summary>
        /// Releases the lease, but only when the token still matches the active
        /// lease, so a slow job cannot release the lease of a newer one.
        /// </summary>
        public async Task CompleteAsync(string token)
        {
            var current = await Ctx.Storage.GetAsync<string>(TokenKey);
            if (current is null || current != token)
                return;

            await Ctx.Storage.DeleteAsync(ExpiresKey);
            await Ctx.Storage.DeleteAsync(OwnerKey);
            await Ctx.Storage.DeleteAsync(TokenKey);
        }

        /// <summary>Reports the current lease without taking one; the UI reads this.</summary>
        public async Task<Lease> PeekAsync()
        {
            var expiresAt = await Ctx.Storage.GetAsync<double>(ExpiresKey);
            if (expiresAt <= Clock.NowMs())
                return new Lease(false, "", "", 0);

            var owner = await Ctx.Storage.GetAsync<string>(OwnerKey);
            return new Lease(true, "", owner is null ? "" : owner, expiresAt);
        }
    }
}
