using System.Globalization;
using System.Text;

namespace RimWorldMW2
{
    /// <summary>
    /// Tiny JSON writer for protocol v0 messages. Hand-rolled so the mod needs no extra DLL.
    /// Replace with a real serializer if the protocol outgrows it.
    /// </summary>
    public sealed class Json
    {
        private readonly StringBuilder sb = new StringBuilder("{");
        private bool first = true;

        public static Json Msg(string type) => new Json().Str("t", type);

        public Json Str(string key, string value)
        {
            Key(key);
            Quote(value);
            return this;
        }

        public Json Num(string key, long value)
        {
            Key(key);
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public Json Num(string key, float value)
        {
            Key(key);
            sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            return this;
        }

        public Json Bool(string key, bool value)
        {
            Key(key);
            sb.Append(value ? "true" : "false");
            return this;
        }

        /// <summary>Insert an already-serialized JSON value (object or array).</summary>
        public Json Raw(string key, string json)
        {
            Key(key);
            sb.Append(json);
            return this;
        }

        public override string ToString() => sb.ToString() + "}";

        private void Key(string key)
        {
            if (!first) sb.Append(',');
            first = false;
            Quote(key);
            sb.Append(':');
        }

        private void Quote(string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
