using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Retake.Design
{
    /// <summary>
    /// A small JSON reader for the embedded theme file (net48 has no System.Text.Json). Objects become
    /// <see cref="Dictionary{TKey,TValue}"/> with string keys, arrays <see cref="List{T}"/>, numbers
    /// <see cref="double"/>, plus string, bool and null.
    /// </summary>
    public static class Json
    {
        public static object? Parse(string text)
        {
            int i = 0;
            object? value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value at " + i);
            return value;
        }

        static object? ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (Match(s, ref i, "true")) return true;
            if (Match(s, ref i, "false")) return false;
            if (Match(s, ref i, "null")) return null;
            return ReadNumber(s, ref i);
        }

        static Dictionary<string, object?> ReadObject(string s, ref int i)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipSpace(s, ref i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                Expect(s, ref i, ':');
                result[key] = ReadValue(s, ref i);
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                Expect(s, ref i, '}');
                return result;
            }
        }

        static List<object?> ReadArray(string s, ref int i)
        {
            var result = new List<object?>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (true)
            {
                result.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                Expect(s, ref i, ']');
                return result;
            }
        }

        static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var b = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Bad unicode escape in JSON");
                        b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape in JSON at " + i);
                }
            }
            throw new FormatException("Unterminated JSON string");
        }

        static double ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("Unexpected character in JSON at " + i);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException("Expected '" + c + "' in JSON at " + i);
            i++;
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
