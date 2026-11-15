using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Paranoidal
{
    /// <summary>
    /// The small JSON needs of the launcher (GitHub API answers, release.json, mod-list.json, info.json) in the shape
    /// that JavaScriptSerializer.DeserializeObject of .NET Framework gave: objects are Dictionary&lt;string, object&gt;,
    /// arrays object[], numbers int, long or decimal (double when out of range), plus string, bool and null.
    /// </summary>
    static class Json
    {
        static readonly JsonDocumentOptions Options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 256,
        };

        /// <summary>The value of a JSON text; throws on broken JSON.</summary>
        public static object Parse(string text)
        {
            using (var doc = JsonDocument.Parse(text ?? "", Options)) return Convert(doc.RootElement);
        }

        static object Convert(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) d[p.Name] = Convert(p.Value);
                    return d;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var x in e.EnumerateArray()) list.Add(Convert(x));
                    return list.ToArray();
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number:
                    int i; long l; decimal m;
                    if (e.TryGetInt32(out i)) return i;
                    if (e.TryGetInt64(out l)) return l;
                    if (e.TryGetDecimal(out m)) return m;
                    return e.GetDouble();
                default: return null;
            }
        }

        /// <summary>Compact JSON of a value of the shapes Parse returns (and of any IDictionary / IEnumerable).</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { Str(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is decimal || v is double || v is float || v is short || v is byte || v is uint || v is ulong)
            {
                sb.Append(System.Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, System.Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var seq = v as IEnumerable;
            if (seq != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var x in seq)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, x);
                }
                sb.Append(']');
                return;
            }
            Str(sb, System.Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
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
    }
}
