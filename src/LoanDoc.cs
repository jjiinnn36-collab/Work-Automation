using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaymentAlert
{
    /// <summary>
    /// 차입 스케줄을 올릴 때 그 사업건 시트만 떼어 CSV 로 증빙에 보관한다 (사용자 결정 2026-09-23).
    /// 회차마다 복사하지 않고 차입건(묶음) 하나에 한 부만 둔다 — 회차가 수십 개라 회차마다 복사하면 낭비다.
    /// 파일은 증빙 뿌리 안의 차입\{묶음}\ 에 둔다. 맥판(node-server/lib/loan-doc.js)과 자리도 바이트도 같다.
    /// </summary>
    public static class LoanDoc
    {
        public const string 종류 = Store.차입원본종류;

        /// <summary>파일 이름에 쓸 수 없는 글자 (윈도우·맥 공통으로 넓게 막는다).</summary>
        static bool 못쓰는글자(char ch)
        {
            return ch == '<' || ch == '>' || ch == ':' || ch == '\u0022' || ch == '/' ||
                   ch == '\u005C' || ch == '|' || ch == '?' || ch == '*' || ch < ' ';
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new StringBuilder();
            foreach (char ch in s)
                sb.Append(못쓰는글자(ch) ? '_' : ch);
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "_" : r;
        }

        /// <summary>엑셀이 수식으로 읽지 않게 막고, 쉼표·따옴표·줄바꿈이 있으면 감싼다 (AC-W16 과 같은 규칙).</summary>
        static string 칸(string v)
        {
            string s = v ?? "";
            if (s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
            if (s.IndexOf('"') >= 0 || s.IndexOf(',') >= 0 || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        /// <summary>시트 한 장을 CSV 글자로. 엑셀이 한글을 바로 읽도록 BOM 을 앞에 붙인다.</summary>
        public static string Csv(Sheet sheet)
        {
            var lines = new List<string>();
            foreach (string[] row in sheet.행)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < row.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(칸(row[i]));
                }
                lines.Add(sb.ToString());
            }
            return "﻿" + string.Join("\r\n", lines.ToArray()) + "\r\n";
        }

        public static string 증빙루트(string dataDir) { return Path.Combine(dataDir, "증빙"); }
        public static string 차입폴더(string dataDir, string 묶음) { return Path.Combine(증빙루트(dataDir), "차입", Safe(묶음)); }

        static string 도장(DateTime n) { return n.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture); }

        static bool 같은바이트(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>.NET 4 에는 Path.GetRelativePath 가 없다. 뿌리 아래 경로만 다루므로 잘라내면 된다.</summary>
        static string 상대경로(string root, string full)
        {
            string r = Path.GetFullPath(root).TrimEnd('\u005C', '/') + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(full);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f.Substring(r.Length) : f;
        }

        /// <summary>
        /// 이 차입건의 원본 스케줄을 보관한다.
        /// 가장 최근 것과 내용이 똑같으면 새로 만들지 않는다 — 같은 파일을 여러 번 올려도 하나만 남는다.
        /// 보관에 실패해도 가져오기 자체는 살린다 (스케줄 자료가 더 중요하다).
        /// </summary>
        /// <returns>"새로" · "같음" · "실패"</returns>
        public static string 원본보관(Store db, string dataDir, string 묶음, Sheet sheet, LoanPlan plan, DateTime now)
        {
            try
            {
                byte[] buf = new UTF8Encoding(false).GetBytes(Csv(sheet));

                // 직전 것과 내용이 같으면 그대로 둔다.
                List<Attachment> 있던 = db.차입원본목록(묶음);
                if (있던.Count > 0)
                {
                    try
                    {
                        string 이전 = Path.Combine(증빙루트(dataDir), 있던[0].저장파일);
                        if (File.Exists(이전) && 같은바이트(File.ReadAllBytes(이전), buf)) return "같음";
                    }
                    catch { }   // 이전 파일이 없어졌으면 새로 만든다
                }

                string folder = 차입폴더(dataDir, 묶음);
                Directory.CreateDirectory(folder);
                string 이름바탕 = !string.IsNullOrEmpty(plan.차입명) ? plan.차입명 : (sheet.이름 ?? "차입 스케줄");
                string 보일이름 = Safe(이름바탕) + ".csv";
                string target = Path.Combine(folder, 도장(now) + "_" + 보일이름);
                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(folder, 도장(now) + "_" + n.ToString(CultureInfo.InvariantCulture) + "_" + 보일이름);
                    n++;
                }
                File.WriteAllBytes(target, buf);

                string rel = 상대경로(증빙루트(dataDir), target);
                try { db.차입원본추가(plan.차입일.Year, 묶음, rel, 보일이름, now); }
                catch { try { File.Delete(target); } catch { } throw; }
                return "새로";
            }
            catch { return "실패"; }
        }

        /// <summary>이 차입건의 원본 스케줄 파일들의 실제 경로 (지울 때 쓴다).</summary>
        public static List<string> 원본경로들(Store db, string dataDir, string 묶음)
        {
            var list = new List<string>();
            foreach (Attachment a in db.차입원본목록(묶음))
                list.Add(Path.Combine(증빙루트(dataDir), a.저장파일));
            return list;
        }
    }
}
