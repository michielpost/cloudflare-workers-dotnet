namespace WorkersDotNet
{
    /// <summary>
    /// Minimal lowercase hex encoding for byte arrays. The worker compiles a
    /// focused C# profile, so there is no Convert/bitconverter to lean on; this
    /// only uses string concatenation and char arithmetic.
    /// </summary>
    public static class Hex
    {
        const string Digits = "0123456789abcdef";

        public static string Encode(byte[] bytes)
        {
            var sb = "";
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                sb += Digits.Substring(b / 16, 1);
                sb += Digits.Substring(b % 16, 1);
            }

            return sb;
        }
    }
}
