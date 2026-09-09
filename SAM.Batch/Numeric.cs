// Steam Stats Editor 新增代码；遵循仓库 LICENSE.txt 中的 zlib 许可证。
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SAM.Batch
{
    internal static class Numeric
    {
        internal static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private static readonly Regex NumberPattern = new Regex(
            @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z",
            RegexOptions.CultureInvariant);

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        internal static bool TryNumber(string token, out double value)
        {
            value = 0;
            return token.Length <= 128 && NumberPattern.IsMatch(token)
                && double.TryParse(token, NumberStyles.Float, Culture, out value) && Finite(value)
                && (value != 0 || IsLexicalZero(token));
        }

        private static bool IsLexicalZero(string token)
        {
            foreach (char c in token)
            {
                if (c == 'e' || c == 'E') break;
                if (c >= '1' && c <= '9') return false;
            }
            return true;
        }

        // 在转换为 double 之前保留原始数字，避免 2147483647.00000001 被四舍五入为整数。
        internal static bool TryInteger(string token, out int result)
        {
            result = 0;
            bool negative = token.StartsWith("-", StringComparison.Ordinal);
            string unsigned = token.TrimStart('+', '-');
            int exponentAt = unsigned.IndexOfAny(new[] { 'e', 'E' });
            string mantissa = exponentAt < 0 ? unsigned : unsigned.Substring(0, exponentAt);
            int pointAt = mantissa.IndexOf('.');
            int fractionalDigits = pointAt < 0 ? 0 : mantissa.Length - pointAt - 1;
            string digits = mantissa.Replace(".", "").TrimStart('0');
            if (digits.Length == 0) return true;
            long exponent = 0;
            if (exponentAt >= 0 && !long.TryParse(unsigned.Substring(exponentAt + 1),
                NumberStyles.AllowLeadingSign, Culture, out exponent)) return false;
            if (exponent < -1024 || exponent > 1024) return false;
            string significant = digits.TrimEnd('0');
            long scale = exponent - fractionalDigits + digits.Length - significant.Length;
            if (scale < 0 || significant.Length + scale > 10) return false;
            string integer = (negative ? "-" : "") + significant + new string('0', (int)scale);
            return int.TryParse(integer, NumberStyles.AllowLeadingSign, Culture, out result);
        }

        internal static string Format(double value, StatValueKind kind)
            => kind == StatValueKind.Integer ? value.ToString("R", Culture)
                : ((float)value).ToString("R", Culture);

        // 接受 float 的标准往返写法、提升为 double 后的标准写法及其等价科学计数法。
        // 不接受“几乎相同”的其他数，避免导入的目标在转换中被悄悄改变。
        internal static bool IsFloatToken(string token, double normalized)
        {
            string canonical = CanonicalDecimal(token);
            return canonical != null &&
                (canonical == CanonicalDecimal(((float)normalized).ToString("R", Culture))
                || canonical == CanonicalDecimal(normalized.ToString("R", Culture)));
        }

        private static string CanonicalDecimal(string token)
        {
            if (token.Length > 128 || !NumberPattern.IsMatch(token)) return null;
            bool negative = token.StartsWith("-", StringComparison.Ordinal);
            string unsigned = token.TrimStart('+', '-');
            int exponentAt = unsigned.IndexOfAny(new[] { 'e', 'E' });
            string mantissa = exponentAt < 0 ? unsigned : unsigned.Substring(0, exponentAt);
            int pointAt = mantissa.IndexOf('.');
            int fractionalDigits = pointAt < 0 ? 0 : mantissa.Length - pointAt - 1;
            string digits = mantissa.Replace(".", "").TrimStart('0');
            if (digits.Length == 0) return "0";
            long exponent = 0;
            if (exponentAt >= 0 && !long.TryParse(unsigned.Substring(exponentAt + 1),
                NumberStyles.AllowLeadingSign, Culture, out exponent)) return null;
            if (exponent < -1024 || exponent > 1024) return null;
            string significant = digits.TrimEnd('0');
            long scale = exponent - fractionalDigits + digits.Length - significant.Length;
            return (negative ? "-" : "") + significant + "e" + scale.ToString(Culture);
        }

        internal static string FloatPrecisionError(string id, double normalized)
            => id + "：目标超出 float32 可保留的精度；可表示值为 "
                + ((float)normalized).ToString("R", Culture) + "。请明确修改为该值后重新导入。";

        internal static uint Bits(float value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
        internal static float Float(uint bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);

        internal static double Adjacent(double value, int direction)
        {
            float single = (float)value;
            if (single == 0) return direction > 0 ? Float(1) : -Float(1);
            uint bits = Bits(single);
            bits = ((single > 0) == (direction > 0)) ? bits + 1 : bits - 1;
            return Float(bits);
        }
    }
}
