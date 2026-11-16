using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UnityTools.PlaytestFeedback.Internal
{
    /// <summary>
    /// Small JSON writer. JsonUtility can't serialize dictionaries or arbitrary objects,
    /// and we only ever need to write JSON, never read it.
    /// </summary>
    internal static class MiniJson
    {
        public static string Serialize(object value, bool pretty = false)
        {
            var sb = new StringBuilder(256);
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value, bool pretty, int indent)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case char c:
                    WriteString(sb, c.ToString());
                    return;
                case Enum e:
                    WriteString(sb, e.ToString());
                    return;
                case float f:
                    WriteDouble(sb, f);
                    return;
                case double d:
                    WriteDouble(sb, d);
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case int or long or short or byte or sbyte or uint or ulong or ushort:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case DateTime dt:
                    WriteString(sb, dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                    return;
                case Vector2 v2:
                    WriteNumbers(sb, v2.x, v2.y);
                    return;
                case Vector3 v3:
                    WriteNumbers(sb, v3.x, v3.y, v3.z);
                    return;
                case Vector4 v4:
                    WriteNumbers(sb, v4.x, v4.y, v4.z, v4.w);
                    return;
                case Vector2Int v2i:
                    WriteNumbers(sb, v2i.x, v2i.y);
                    return;
                case Vector3Int v3i:
                    WriteNumbers(sb, v3i.x, v3i.y, v3i.z);
                    return;
                case Quaternion q:
                    WriteNumbers(sb, q.eulerAngles.x, q.eulerAngles.y, q.eulerAngles.z);
                    return;
                case Color col:
                    WriteString(sb, "#" + ColorUtility.ToHtmlStringRGBA(col));
                    return;
                case IDictionary dict:
                    WriteObject(sb, dict, pretty, indent);
                    return;
                case IEnumerable list:
                    WriteArray(sb, list, pretty, indent);
                    return;
                default:
                    WriteString(sb, value.ToString());
                    return;
            }
        }

        static void WriteObject(StringBuilder sb, IDictionary dict, bool pretty, int indent)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, pretty, indent + 1);
                WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                sb.Append(pretty ? ": " : ":");
                Write(sb, entry.Value, pretty, indent + 1);
            }
            if (!first) NewLine(sb, pretty, indent);
            sb.Append('}');
        }

        static void WriteArray(StringBuilder sb, IEnumerable list, bool pretty, int indent)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, pretty, indent + 1);
                Write(sb, item, pretty, indent + 1);
            }
            if (!first) NewLine(sb, pretty, indent);
            sb.Append(']');
        }

        static void WriteNumbers(StringBuilder sb, params float[] values)
        {
            sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                WriteDouble(sb, values[i]);
            }
            sb.Append(']');
        }

        static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
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
