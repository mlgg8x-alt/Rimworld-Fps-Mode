using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RimWorldMW2
{
    /// <summary>
    /// Tiny reader for the flat protocol messages IW4L sends. Top-level strings, numbers and
    /// booleans become entries; nested arrays and objects are kept as their JSON text, and
    /// <see cref="Nums"/> reads an array of numbers.
    /// </summary>
    public sealed class JsonLine
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public string Type => Str("t");

        public string Str(string key) => values.TryGetValue(key, out var value) ? value : null;

        public float? Num(string key)
        {
            string value = Str(key);
            if (value != null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n))
                return n;
            return null;
        }

        /// <summary>A flat array of numbers such as <c>[1.5,0,2]</c>, or null.</summary>
        public float[] Nums(string key)
        {
            string value = Str(key);
            if (value == null || value.Length < 2 || value[0] != '[' || value[value.Length - 1] != ']') return null;
            string inner = value.Substring(1, value.Length - 2).Trim();
            if (inner.Length == 0) return new float[0];
            string[] items = inner.Split(',');
            var numbers = new float[items.Length];
            for (int k = 0; k < items.Length; k++)
                if (!float.TryParse(items[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[k]))
                    return null;
            return numbers;
        }

        /// <summary>The parsed line, or null when it is not a JSON object.</summary>
        public static JsonLine Parse(string line)
        {
            var json = new JsonLine();
            int i = 0;
            return json.ParseObject(line ?? "", ref i) ? json : null;
        }

        private bool ParseObject(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length || s[i] != '{') return false;
            i++;
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == '}') return true;
                if (s[i] == ',') { i++; continue; }
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (key == null || i >= s.Length || s[i] != ':') return false;
                i++;
                SkipSpace(s, ref i);
                if (i >= s.Length) return false;
                char c = s[i];
                if (c == '"')
                {
                    string value = ReadString(s, ref i);
                    if (value == null) return false;
                    values[key] = value;
                }
                else if (c == '{' || c == '[')
                {
                    int start = i;
                    if (!SkipNested(s, ref i)) return false;
                    values[key] = s.Substring(start, i - start);
                }
                else
                {
                    int start = i;
                    while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
                    values[key] = s.Substring(start, i - start);
                }
            }
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            var sb = new StringBuilder();
            for (i++; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }
                if (c != '\\') { sb.Append(c); continue; }
                if (++i >= s.Length) return null;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 >= s.Length) return null;
                        sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            return null;
        }

        private static bool SkipNested(string s, ref int i)
        {
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    if (ReadString(s, ref i) == null) return false;
                    continue;
                }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                i++;
                if (depth == 0) return true;
            }
            return false;
        }
    }
}
