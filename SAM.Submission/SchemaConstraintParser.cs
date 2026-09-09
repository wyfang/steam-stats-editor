// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SAM.Submission
{
    // Constraints must not gain permission through rounding, underflow or a fallback.
    // Accept normal float32 round-trip spellings, not arbitrary nearby decimal values.
    public static class SchemaConstraintParser
    {
        private static readonly Regex Number = new(@"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant);

        public static bool TryFloat(string text, out float value)
        {
            value = 0;
            if (text == null) return false;
            text = text.Trim();
            string canonical = Canonical(text);
            if (canonical == null || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value)) return false;
            return canonical == Canonical(value.ToString("R", CultureInfo.InvariantCulture)) ||
                canonical == Canonical(((double)value).ToString("R", CultureInfo.InvariantCulture));
        }

        private static string Canonical(string token)
        {
            if (token.Length > 128 || !Number.IsMatch(token)) return null;
            bool negative = token.StartsWith("-", StringComparison.Ordinal);
            string unsigned = token.TrimStart('+', '-');
            int exponentAt = unsigned.IndexOfAny(new[] { 'e', 'E' });
            string mantissa = exponentAt < 0 ? unsigned : unsigned.Substring(0, exponentAt);
            int pointAt = mantissa.IndexOf('.');
            int fraction = pointAt < 0 ? 0 : mantissa.Length - pointAt - 1;
            string digits = mantissa.Replace(".", "").TrimStart('0');
            if (digits.Length == 0) return "0";
            long exponent = 0;
            if (exponentAt >= 0 && !long.TryParse(unsigned.Substring(exponentAt + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent)) return null;
            if (exponent < -1024 || exponent > 1024) return null;
            string significant = digits.TrimEnd('0');
            long scale = exponent - fraction + digits.Length - significant.Length;
            return (negative ? "-" : "") + significant + "e" + scale.ToString(CultureInfo.InvariantCulture);
        }
    }
}
