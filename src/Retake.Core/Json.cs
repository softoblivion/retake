using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Retake.Core
{
    /// <summary>The text is not valid JSON, or a value cannot be written as JSON.</summary>
    public sealed class JsonFormatException : Exception
    {
        public JsonFormatException(string message) : base(message) { }
    }

    /// <summary>A JSON object that keeps the order of its keys (insertion order, replaced keys keep their place).</summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, object?> _map = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>Number of keys.</summary>
        public int Count { get { return _keys.Count; } }

        /// <summary>The keys in insertion order.</summary>
        public IEnumerable<string> Keys { get { return _keys; } }

        /// <summary>Gets a value (KeyNotFoundException when absent) or sets it, adding the key at the end when new.</summary>
        public object? this[string key]
        {
            get { return _map[key]; }
            set
            {
                if (!_map.ContainsKey(key)) _keys.Add(key);
                _map[key] = value;
            }
        }

        public bool ContainsKey(string key) { return _map.ContainsKey(key); }

        public bool TryGetValue(string key, out object? value) { return _map.TryGetValue(key, out value); }

        /// <summary>Removes a key. False when it was absent.</summary>
        public bool Remove(string key)
        {
            if (!_map.Remove(key)) return false;
            _keys.Remove(key);
            return true;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach (string key in _keys) yield return new KeyValuePair<string, object?>(key, _map[key]);
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    /// <summary>
    /// A small JSON reader and writer. Values are null, bool, long (numbers without fraction or exponent), double,
    /// string, <see cref="List{T}"/> of object and <see cref="JsonObject"/>. The writer indents by two spaces, uses LF
    /// line endings, keeps non-ASCII characters as they are and prints floats the way Python's repr does.
    /// </summary>
    public static class Json
    {
        private const int MaxDepth = 256;

        /// <summary>Parses a complete JSON document. Throws <see cref="JsonFormatException"/> on any error.</summary>
        public static object? Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var parser = new Parser(text);
            object? value = parser.ReadValue(0);
            parser.SkipSpace();
            if (!parser.AtEnd) throw new JsonFormatException("Extra data after the JSON value");
            return value;
        }

        /// <summary>Writes a value as indented JSON without a trailing newline.</summary>
        public static string Write(object? value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object? value, int level)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string s: WriteString(sb, s); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new JsonFormatException("A non-finite number cannot be written");
                    sb.Append(Py.Repr(d));
                    return;
                case JsonObject obj:
                    if (obj.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    bool first = true;
                    foreach (KeyValuePair<string, object?> pair in obj)
                    {
                        if (!first) sb.Append(",\n");
                        first = false;
                        Indent(sb, level + 1);
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        WriteValue(sb, pair.Value, level + 1);
                    }
                    sb.Append('\n');
                    Indent(sb, level);
                    sb.Append('}');
                    return;
                case IList<object?> list:
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    sb.Append("[\n");
                    for (int n = 0; n < list.Count; n++)
                    {
                        if (n > 0) sb.Append(",\n");
                        Indent(sb, level + 1);
                        WriteValue(sb, list[n], level + 1);
                    }
                    sb.Append('\n');
                    Indent(sb, level);
                    sb.Append(']');
                    return;
                default:
                    throw new JsonFormatException("Cannot write a value of type " + value.GetType().Name);
            }
        }

        private static void Indent(StringBuilder sb, int level)
        {
            sb.Append(' ', level * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s) { _s = s; }

            public bool AtEnd { get { return _pos >= _s.Length; } }

            public void SkipSpace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                    else break;
                }
            }

            public object? ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new JsonFormatException("The JSON is nested too deeply");
                SkipSpace();
                if (AtEnd) throw new JsonFormatException("Unexpected end of the JSON text");
                char c = _s[_pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new JsonFormatException("Unexpected character at position " + _pos);
                }
            }

            private void ExpectWord(string word)
            {
                if (string.CompareOrdinal(_s, _pos, word, 0, word.Length) != 0) throw new JsonFormatException("Unexpected token at position " + _pos);
                _pos += word.Length;
            }

            private JsonObject ReadObject(int depth)
            {
                var obj = new JsonObject();
                _pos++;
                SkipSpace();
                if (!AtEnd && _s[_pos] == '}') { _pos++; return obj; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || _s[_pos] != '"') throw new JsonFormatException("Expected a string key at position " + _pos);
                    string key = ReadString();
                    SkipSpace();
                    if (AtEnd || _s[_pos] != ':') throw new JsonFormatException("Expected ':' at position " + _pos);
                    _pos++;
                    obj[key] = ReadValue(depth + 1);
                    SkipSpace();
                    if (AtEnd) throw new JsonFormatException("Unexpected end of the JSON text");
                    if (_s[_pos] == ',') { _pos++; continue; }
                    if (_s[_pos] == '}') { _pos++; return obj; }
                    throw new JsonFormatException("Expected ',' or '}' at position " + _pos);
                }
            }

            private List<object?> ReadArray(int depth)
            {
                var list = new List<object?>();
                _pos++;
                SkipSpace();
                if (!AtEnd && _s[_pos] == ']') { _pos++; return list; }
                while (true)
                {
                    list.Add(ReadValue(depth + 1));
                    SkipSpace();
                    if (AtEnd) throw new JsonFormatException("Unexpected end of the JSON text");
                    if (_s[_pos] == ',') { _pos++; continue; }
                    if (_s[_pos] == ']') { _pos++; return list; }
                    throw new JsonFormatException("Expected ',' or ']' at position " + _pos);
                }
            }

            private string ReadString()
            {
                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new JsonFormatException("Unterminated string");
                    char c = _s[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw new JsonFormatException("Control character in a string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw new JsonFormatException("Unterminated string");
                    char e = _s[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _s.Length) throw new JsonFormatException("Bad \\u escape");
                            int code;
                            if (!int.TryParse(_s.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                                throw new JsonFormatException("Bad \\u escape");
                            sb.Append((char)code);
                            _pos += 4;
                            break;
                        default:
                            throw new JsonFormatException("Bad escape in a string");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _pos;
                if (_s[_pos] == '-') _pos++;
                if (AtEnd) throw new JsonFormatException("Bad number");
                if (_s[_pos] == '0') _pos++;
                else if (_s[_pos] >= '1' && _s[_pos] <= '9') { while (!AtEnd && _s[_pos] >= '0' && _s[_pos] <= '9') _pos++; }
                else throw new JsonFormatException("Bad number");
                bool isFloat = false;
                if (!AtEnd && _s[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    int digits = 0;
                    while (!AtEnd && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; digits++; }
                    if (digits == 0) throw new JsonFormatException("Bad number");
                }
                if (!AtEnd && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    isFloat = true;
                    _pos++;
                    if (!AtEnd && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    int digits = 0;
                    while (!AtEnd && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; digits++; }
                    if (digits == 0) throw new JsonFormatException("Bad number");
                }
                string token = _s.Substring(start, _pos - start);
                if (!isFloat)
                {
                    long l;
                    if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
                }
                double d;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw new JsonFormatException("Bad number");
                return d;
            }
        }
    }
}
