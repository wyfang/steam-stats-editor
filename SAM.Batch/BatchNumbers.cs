// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Globalization;
using System.Linq;

namespace SAM.Batch
{
    public static class BatchNumbers
    {
        // Preserve the original decimal digits while translating locale punctuation.
        // Parsing and reformatting a double first would hide precision loss in the input.
        public static float ParseUiFloat(string text, CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            if (text == null || text.Length > 128)
                throw new FormatException("请输入不超过 128 个字符的有限数字。");
            string token = Normalize(text.Trim(), culture.NumberFormat);
            if (token == null || !Numeric.TryNumber(token, out double parsed))
                throw new FormatException("请输入有限数字，并使用当前区域设置的小数点和分组符号。");
            float value = (float)parsed;
            if (float.IsNaN(value) || float.IsInfinity(value) || (value == 0 && parsed != 0))
                throw new FormatException("目标超出 float32 可表示的范围，不能溢出或下溢为零。");
            if (!Numeric.IsFloatToken(token, value))
                throw new FormatException("目标超出 float32 可保留的精度；可表示值为 " +
                    value.ToString("R", culture) + "。请明确修改为该值后重新输入。");
            return value;
        }

        private static string Normalize(string text, NumberFormatInfo format)
        {
            string sign = ReadSign(ref text, format);
            int exponentAt = text.IndexOfAny(new[] { 'e', 'E' });
            string exponent = "";
            if (exponentAt >= 0)
            {
                string digits = text.Substring(exponentAt + 1);
                string exponentSign = ReadSign(ref digits, format);
                if (!Digits(digits)) return null;
                exponent = "e" + exponentSign + digits;
                text = text.Substring(0, exponentAt);
            }
            string decimalSeparator = format.NumberDecimalSeparator;
            int decimalAt = text.IndexOf(decimalSeparator, StringComparison.Ordinal);
            string integer = decimalAt < 0 ? text : text.Substring(0, decimalAt);
            string fraction = decimalAt < 0 ? "" : text.Substring(decimalAt + decimalSeparator.Length);
            if (fraction.Length > 0 && !Digits(fraction)) return null;
            if (integer.Length == 0 && fraction.Length == 0) return null;

            string groupSeparator = format.NumberGroupSeparator;
            if (!string.IsNullOrEmpty(groupSeparator) && integer.Contains(groupSeparator))
            {
                string[] groups = integer.Split(new[] { groupSeparator }, StringSplitOptions.None);
                int[] sizes = format.NumberGroupSizes;
                if (sizes.Length == 0 || groups.Any(g => !Digits(g))) return null;
                int sizeIndex = 0;
                for (int index = groups.Length - 1; index > 0; index--)
                {
                    int size = sizes[Math.Min(sizeIndex, sizes.Length - 1)];
                    if (size == 0 || groups[index].Length != size) return null;
                    if (sizeIndex < sizes.Length - 1) sizeIndex++;
                }
                int leadingSize = sizes[Math.Min(sizeIndex, sizes.Length - 1)];
                if (leadingSize != 0 && groups[0].Length > leadingSize) return null;
                integer = string.Concat(groups);
            }
            if (integer.Length > 0 && !Digits(integer)) return null;
            return sign + (integer.Length == 0 ? "0" : integer) +
                (decimalAt >= 0 ? "." + fraction : "") + exponent;
        }

        private static string ReadSign(ref string text, NumberFormatInfo format)
        {
            if (!string.IsNullOrEmpty(format.NegativeSign) && text.StartsWith(format.NegativeSign, StringComparison.Ordinal))
            {
                text = text.Substring(format.NegativeSign.Length);
                return "-";
            }
            if (!string.IsNullOrEmpty(format.PositiveSign) && text.StartsWith(format.PositiveSign, StringComparison.Ordinal))
            {
                text = text.Substring(format.PositiveSign.Length);
                return "+";
            }
            return "";
        }

        private static bool Digits(string text) => text.Length > 0 && text.All(c => c >= '0' && c <= '9');
    }
}
