using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Engraving.Core.Figures
{
    /// <summary>
    /// A small JSON reader and writer for pose files (the core library targets netstandard2.0 inside the game, where
    /// no JSON library comes for free). Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers
    /// double, plus string, bool and null. Also accepts // comments and trailing commas, since people edit these by hand.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Skip(text, ref i);
            if (i < text.Length) throw Error(text, i, "unexpected text after the end");
            return v;
        }

        static Exception Error(string s, int i, string what)
        {
            int line = 1;
            for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
            return new FormatException($"line {line}: {what}");
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw Error(s, i, "unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    if (i >= s.Length || s[i] != '"') throw Error(s, i, "expected a \"name\"");
                    string key = Str(s, ref i);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Error(s, i, $"expected ':' after \"{key}\"");
                    i++;
                    o[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw Error(s, i, "expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                i++;
                var a = new List<object>();
                while (true)
                {
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw Error(s, i, "expected ',' or ']'");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i > start && double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            throw Error(s, start, "expected a value");
        }

        static string Str(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u' when i + 4 < s.Length:
                            sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; break;
                        default: sb.Append(s[i]); break;
                    }
                    i++;
                    continue;
                }
                sb.Append(s[i++]);
            }
            if (i >= s.Length) throw Error(s, i, "unterminated string");
            i++;
            return sb.ToString();
        }

        /// <summary>Writes a value on one line.</summary>
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Write(v, sb);
            return sb.ToString();
        }

        static void Write(object v, StringBuilder sb)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s:
                    sb.Append('"');
                    foreach (char c in s)
                    {
                        if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                        else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                    }
                    sb.Append('"');
                    break;
                case double d: sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("0.###", CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> o:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(kv.Key, sb);
                        sb.Append(':');
                        Write(kv.Value, sb);
                    }
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var x in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(x, sb);
                    }
                    sb.Append(']');
                    break;
                }
                default: sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
        }
    }
}
