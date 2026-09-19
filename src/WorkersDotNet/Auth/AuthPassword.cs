using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Password hashing done entirely by hand through the Workers crypto binding.
    /// The worker cannot reference .NET auth NuGet packages, so instead of
    /// PBKDF2/bcrypt it uses a salted SHA-256 loop (<c>crypto.subtle.digest</c>).
    /// The stored value is <c>"&lt;saltHex&gt;:&lt;hashHex&gt;"</c> with a 16-byte
    /// salt and a 64-character hash, so both halves have fixed positions and no
    /// delimiter parsing is needed.
    /// </summary>
    public static class AuthPassword
    {
        // Must stay in sync with the fixed-width parsing in VerifyAsync.
        const int Iterations = 1000;
        const int SaltBytes = 16;

        public static async Task<string> HashAsync(string password)
        {
            var salt = Crypto.RandomBytes(SaltBytes);
            var saltHex = Hex.Encode(salt);
            var hashHex = await RoundsAsync(saltHex, password);
            return saltHex + ":" + hashHex;
        }

        public static async Task<bool> VerifyAsync(string password, string stored)
        {
            // stored = "<32 char saltHex>:<64 char hashHex>".
            if (stored is null || stored.Length < 97)
                return false;

            var saltHex = stored.Substring(0, 32);
            var storedHash = stored.Substring(33, 64);
            var computedHash = await RoundsAsync(saltHex, password);
            return SecureEquals(computedHash, storedHash);
        }

        static async Task<string> RoundsAsync(string saltHex, string password)
        {
            var current = password;

            for (var i = 0; i < Iterations; i++)
            {
                var input = saltHex + ":" + current;
                var bytes = await Crypto.DigestTextAsync(DigestAlgorithm.Sha256, input);
                current = Hex.Encode(bytes);
            }

            return current;
        }

        /// <summary>
        /// Compares two equal-length hex strings without short-circuiting on the
        /// first difference, so a timing side channel cannot reveal how much of
        /// the hash matched.
        /// </summary>
        static bool SecureEquals(string a, string b)
        {
            if (a.Length != b.Length)
                return false;

            var same = true;
            for (var i = 0; i < a.Length; i++)
            {
                if (a.Substring(i, 1) != b.Substring(i, 1))
                    same = false;
            }

            return same;
        }
    }
}
