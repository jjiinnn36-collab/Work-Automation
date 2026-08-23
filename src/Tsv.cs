using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaymentAlert
{
    /// <summary>탭 구분 파일 읽기·쓰기. '#'로 시작하는 줄은 주석으로 건너뛴다.</summary>
    public static class Tsv
    {
        static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        /// <summary>첫 유효 행을 헤더로 보고, 각 행을 헤더명 기준 사전으로 돌려준다.</summary>
        public static List<Dictionary<string, string>> Read(string path)
        {
            var rows = new List<Dictionary<string, string>>();
            if (!File.Exists(path)) return rows;

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            string[] header = null;

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r', '\n');
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) continue;

                string[] cells = line.Split('\t');
                if (header == null)
                {
                    header = new string[cells.Length];
                    for (int i = 0; i < cells.Length; i++)
                        header[i] = cells[i].Trim().TrimStart('\uFEFF');
                    continue;
                }

                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < header.Length; i++)
                    row[header[i]] = i < cells.Length ? cells[i].Trim() : "";
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>헤더와 행들을 UTF-8(BOM)로 쓴다. 메모장·엑셀 양쪽에서 한글이 깨지지 않는다.</summary>
        public static void Write(string path, string[] header, IEnumerable<string[]> rows, string[] comments)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            if (comments != null)
                foreach (string c in comments) sb.Append("#").Append(c).Append("\r\n");

            sb.Append(string.Join("\t", header)).Append("\r\n");
            foreach (string[] r in rows) sb.Append(string.Join("\t", r)).Append("\r\n");

            // 임시 파일에 쓴 뒤 교체한다. 쓰는 도중 죽어도 기존 파일이 남는다.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Utf8Bom);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static string Get(Dictionary<string, string> row, string key)
        {
            string v;
            return row.TryGetValue(key, out v) ? (v ?? "") : "";
        }

        public static int GetInt(Dictionary<string, string> row, string key, int fallback)
        {
            string s = Get(row, key);
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }

        public static decimal? GetDecimal(Dictionary<string, string> row, string key)
        {
            string s = Get(row, key).Replace(",", "");
            if (s.Length == 0) return null;
            decimal d;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }

        public static DateTime? GetDate(Dictionary<string, string> row, string key)
        {
            string s = Get(row, key);
            if (s.Length == 0) return null;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }
    }
}
