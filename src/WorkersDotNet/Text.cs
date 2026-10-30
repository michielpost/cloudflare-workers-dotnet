namespace WorkersDotNet
{
    /// <summary>
    /// Small string helpers. The Workers C# profile has no
    /// <c>System.Text.RegularExpressions</c>, so the numeric header checks the
    /// endpoints need are done by hand.
    /// </summary>
    public static class Text
    {
        /// <summary>
        /// True when <paramref name="value"/> is nothing but ASCII digits and its
        /// length is within the given bounds.
        /// </summary>
        public static bool IsDigits(string value, int minLength, int maxLength)
        {
            if (value.Length < minLength || value.Length > maxLength)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                    return false;
            }

            return true;
        }
    }
}
