using System.Threading.Tasks;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// Thin wrapper around the KV binding that provides null-safe JSON reads, so
    /// services do not repeat the try/catch around <c>GetJsonAsync</c>.
    /// </summary>
    public sealed class KvStore
    {
        private readonly IKvNamespace _kv;

        public KvStore(IKvNamespace kv)
        {
            _kv = kv;
        }

        /// <summary>Reads a key's value, or null when the key is absent.</summary>
        public async Task<string?> GetTextAsync(string key)
        {
            return await _kv.GetTextAsync(key);
        }

        public async Task PutTextAsync(string key, string value)
        {
            await _kv.PutTextAsync(key, value);
        }

        /// <summary>
        /// Reads a key and deserialises it as <typeparamref name="T"/>, or null
        /// when the key is absent or does not parse. Never throws.
        /// </summary>
        public async Task<T?> GetJsonAsync<T>(string key) where T : class
        {
            try
            {
                return await _kv.GetJsonAsync<T>(key);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        public async Task PutJsonAsync<T>(string key, T value)
        {
            await _kv.PutJsonAsync(key, value);
        }
    }
}
