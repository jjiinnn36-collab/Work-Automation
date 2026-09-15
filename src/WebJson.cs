using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PaymentAlert
{
    /// <summary>순서를 지키는 JSON 객체. 키를 넣은 순서대로 쓴다.</summary>
    public sealed class JObj
    {
        internal readonly List<KeyValuePair<string, object>> 항목 = new List<KeyValuePair<string, object>>();

        public JObj Set(string key, object value)
        {
            항목.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }
    }

    /// <summary>웹 화면과 주고받는 JSON 을 손으로 쓴다. 외부 라이브러리를 들이지 않기 위해서다.</summary>
    public static class Json
    {
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Emit(sb, v);
            return sb.ToString();
        }

        static void Emit(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Str(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is decimal) { sb.Append(((decimal)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is DateTime) { Str(sb, ((DateTime)v).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); return; }

            var o = v as JObj;
            if (o != null)
            {
                sb.Append('{');
                for (int i = 0; i < o.항목.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Str(sb, o.항목[i].Key);
                    sb.Append(':');
                    Emit(sb, o.항목[i].Value);
                }
                sb.Append('}');
                return;
            }

            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object x in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Emit(sb, x);
                }
                sb.Append(']');
                return;
            }

            Str(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    // 화면에 그대로 끼워 넣어도 태그로 읽히지 않게 한다.
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    case '&': sb.Append("\\u0026"); break;
                    case (char)0x2028: sb.Append("\\u2028"); break;
                    case (char)0x2029: sb.Append("\\u2029"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
