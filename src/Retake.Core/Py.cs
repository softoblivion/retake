using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Retake.Core
{
    /// <summary>
    /// Text and number helpers that reproduce the exact behaviour of the corresponding Python operations, so that files
    /// written by this core are byte-identical to the ones written by the 1.x core.
    /// </summary>
    internal static class Py
    {
        /// <summary>True for the characters Python's <c>str.isspace</c> accepts.</summary>
        public static bool IsSpace(char c)
        {
            if (c <= ' ') return c == ' ' || (c >= '\t' && c <= '\r') || (c >= '\u001c' && c <= '\u001f');
            if (c < '\u0085') return false;
            int code = c;
            return code == 0x85 || code == 0xA0 || code == 0x1680 || (code >= 0x2000 && code <= 0x200A)
                || code == 0x2028 || code == 0x2029 || code == 0x202F || code == 0x205F || code == 0x3000;
        }

        /// <summary><c>str.strip()</c>.</summary>
        public static string Strip(string s)
        {
            int start = 0;
            int end = s.Length;
            while (start < end && IsSpace(s[start])) start++;
            while (end > start && IsSpace(s[end - 1])) end--;
            return s.Substring(start, end - start);
        }

        /// <summary><c>str.split()</c> without arguments.</summary>
        public static List<string> SplitWhitespace(string s)
        {
            var parts = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && IsSpace(s[i])) i++;
                int start = i;
                while (i < s.Length && !IsSpace(s[i])) i++;
                if (i > start) parts.Add(s.Substring(start, i - start));
            }
            return parts;
        }

        /// <summary><c>PurePath.suffix</c> of a file name: the part from the last dot, empty for ".x" and "x.".</summary>
        public static string Suffix(string name)
        {
            int i = name.LastIndexOf('.');
            return (i > 0 && i < name.Length - 1) ? name.Substring(i) : "";
        }

        /// <summary><c>PurePath.stem</c> of a file name.</summary>
        public static string Stem(string name)
        {
            int i = name.LastIndexOf('.');
            return (i > 0 && i < name.Length - 1) ? name.Substring(0, i) : name;
        }

        /// <summary>Exact <c>round(value * 1000)</c> with round-half-even on the exact binary value (Python's <c>round(x, 3)</c>).</summary>
        public static long RoundMilli(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("The value is not finite");
            bool negative = value < 0;
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            int exponent = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & ((1L << 52) - 1);
            long mantissa;
            int e;
            if (exponent == 0) { mantissa = fraction; e = -1074; }
            else { mantissa = fraction | (1L << 52); e = exponent - 1075; }
            BigInteger scaled = new BigInteger(mantissa) * 1000;
            BigInteger rounded;
            if (e >= 0)
            {
                rounded = scaled << e;
            }
            else
            {
                int shift = -e;
                BigInteger quotient = scaled >> shift;
                BigInteger remainder = scaled - (quotient << shift);
                BigInteger half = BigInteger.One << (shift - 1);
                int cmp = remainder.CompareTo(half);
                if (cmp > 0 || (cmp == 0 && !quotient.IsEven)) quotient += 1;
                rounded = quotient;
            }
            if (rounded > long.MaxValue) throw new OverflowException();
            long result = (long)rounded;
            return negative ? -result : result;
        }

        /// <summary><c>f"{value:.3f}"</c>.</summary>
        public static string Fixed3(double value)
        {
            long k = RoundMilli(value);
            bool negative = value < 0 || (value == 0 && 1.0 / value < 0);
            long abs = Math.Abs(k);
            string text = (abs / 1000).ToString(CultureInfo.InvariantCulture) + "." + (abs % 1000).ToString("D3", CultureInfo.InvariantCulture);
            return negative ? "-" + text : text;
        }

        /// <summary><c>round(value, 3)</c> for a finite value.</summary>
        public static double Round3(double value)
        {
            return RoundMilli(value) / 1000.0;
        }

        /// <summary>
        /// The shortest decimal digits that read back as <paramref name="abs"/> (the nearest one when several of that length
        /// do), with the decimal exponent of the first digit. Exact big-integer arithmetic: the framework's own formatting is
        /// not correct in the last digit of 16- and 17-digit values.
        /// </summary>
        private static void ShortestDigits(double abs, out string digits, out int exp10)
        {
            long bits = BitConverter.DoubleToInt64Bits(abs);
            int biased = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & ((1L << 52) - 1);
            BigInteger m;
            int e;
            if (biased == 0) { m = fraction; e = -1074; }
            else { m = fraction | (1L << 52); e = biased - 1075; }
            bool even = m.IsEven;
            int sh = e < 0 ? -e : 0;
            // Everything below is scaled by dn = 2^(sh + 2) so that the value and the rounding interval are integers.
            BigInteger dn = BigInteger.One << (sh + 2);
            BigInteger v = m << (e + sh + 2);
            BigInteger high = (2 * m + 1) << (e + sh + 1);
            BigInteger low = (m == (BigInteger.One << 52) && biased > 1) ? (4 * m - 1) << (e + sh) : (2 * m - 1) << (e + sh + 1);

            int k = (int)Math.Floor(Math.Log10(abs));
            while (CompareToPowerOfTen(v, dn, k) < 0) k--;
            while (CompareToPowerOfTen(v, dn, k + 1) >= 0) k++;

            for (int precision = 1; precision <= 17; precision++)
            {
                int s = k - precision + 1;
                BigInteger tNum = s >= 0 ? BigInteger.Pow(10, s) : BigInteger.One;
                BigInteger tDen = s < 0 ? BigInteger.Pow(10, -s) : BigInteger.One;
                BigInteger numerator = v * tDen;
                BigInteger denominator = dn * tNum;
                BigInteger n = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
                BigInteger twice = remainder * 2;
                int cmp = twice.CompareTo(denominator);
                if (cmp > 0 || (cmp == 0 && !n.IsEven)) n += 1;
                if (n >= BigInteger.Pow(10, precision)) { n /= 10; s += 1; tNum = s >= 0 ? BigInteger.Pow(10, s) : BigInteger.One; tDen = s < 0 ? BigInteger.Pow(10, -s) : BigInteger.One; }
                // candidate = n * tNum / tDen, scaled by dn
                BigInteger scaled = n * tNum * dn;
                BigInteger lowScaled = low * tDen;
                BigInteger highScaled = high * tDen;
                bool aboveLow = even ? scaled >= lowScaled : scaled > lowScaled;
                bool belowHigh = even ? scaled <= highScaled : scaled < highScaled;
                if ((aboveLow && belowHigh) || precision == 17)
                {
                    digits = n.ToString(CultureInfo.InvariantCulture);
                    exp10 = s + digits.Length - 1;
                    return;
                }
            }
            throw new InvalidOperationException();
        }

        /// <summary>Sign of <c>v / dn - 10^k</c>.</summary>
        private static int CompareToPowerOfTen(BigInteger v, BigInteger dn, int k)
        {
            return k >= 0 ? v.CompareTo(BigInteger.Pow(10, k) * dn) : (v * BigInteger.Pow(10, -k)).CompareTo(dn);
        }

        /// <summary><c>repr(float)</c>: the shortest text that reads back as the same double, always with a fraction or an exponent.</summary>
        public static string Repr(double value)
        {
            if (value == 0) return (1.0 / value < 0) ? "-0.0" : "0.0";
            bool negative = value < 0;
            double abs = Math.Abs(value);
            string mantissaText;
            int exp10;
            ShortestDigits(abs, out mantissaText, out exp10);
            string digits = mantissaText.TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            int decpt = exp10 + 1;
            var sb = new StringBuilder();
            if (negative) sb.Append('-');
            if (decpt > -4 && decpt <= 16)
            {
                if (decpt <= 0) sb.Append("0.").Append('0', -decpt).Append(digits);
                else if (decpt >= digits.Length) sb.Append(digits).Append('0', decpt - digits.Length).Append(".0");
                else sb.Append(digits, 0, decpt).Append('.').Append(digits, decpt, digits.Length - decpt);
            }
            else
            {
                sb.Append(digits[0]);
                if (digits.Length > 1) sb.Append('.').Append(digits, 1, digits.Length - 1);
                int exponent = decpt - 1;
                sb.Append('e').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
