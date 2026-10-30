using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// The worker's clock. The Workers C# profile has no <c>DateTimeOffset</c>, so
    /// time comes from the runtime's <see cref="Date"/> and is expressed either as
    /// unix milliseconds (a JavaScript number) or as an ISO 8601 string.
    /// </summary>
    public static class Clock
    {
        /// <summary>Now, as unix milliseconds.</summary>
        public static double NowMs()
        {
            return Date.Now();
        }

        /// <summary>
        /// Now, as an ISO 8601 string. This is the shape
        /// <c>DateTimeOffset.UtcNow.ToString("O")</c> used to produce, so the
        /// values already stored in D1 and KV keep sorting the same way.
        /// </summary>
        public static string NowIso()
        {
            return new Date(Date.Now()).ToIsoString();
        }
    }
}
