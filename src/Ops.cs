using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PaymentAlert
{
    /// <summary>프로그램 판. 화면·기록·/api/health 에 같은 값을 쓴다.</summary>
    public static class AppInfo
    {
        public const string 버전 = "2.0.0";
    }

    /// <summary>
    /// 실행 기록 파일. 정해 둔 크기를 넘으면 run.1.log, run.2.log … 로 밀어낸다 (ADR-0008).
    /// 기록을 못 남겨도 알림은 멈추면 안 되므로 예외를 밖으로 내지 않는다.
    /// </summary>
    public static class LogFile
    {
        public const long 기본최대 = 1024 * 1024;
        public const int 기본보관 = 3;

        public static void Append(string path, string message)
        {
            Append(path, message, 기본최대, 기본보관, DateTime.Now);
        }

        public static void Append(string path, string message, long 최대바이트, int 보관수, DateTime 시각)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length >= 최대바이트) 밀어내기(path, 보관수);

                File.AppendAllText(path,
                    시각.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" + message + "\r\n",
                    new UTF8Encoding(true));
            }
            catch { /* 기록 실패로 프로그램을 멈추지 않는다 */ }
        }

        static void 밀어내기(string path, int 보관수)
        {
            string 이름 = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            string ext = Path.GetExtension(path);
            string 가장옛 = 이름 + "." + 보관수 + ext;
            if (File.Exists(가장옛)) File.Delete(가장옛);
            for (int i = 보관수 - 1; i >= 1; i--)
            {
                string from = 이름 + "." + i + ext;
                if (File.Exists(from)) File.Move(from, 이름 + "." + (i + 1) + ext);
            }
            File.Move(path, 이름 + ".1" + ext);
        }
    }

    /// <summary>
    /// 자료 DB 날짜별 사본 (ADR-0008). 하루 첫 실행에 한 번 만들고 최근 N개만 남긴다.
    /// 위치는 실행 파일 옆 backup-folder.txt 로 바꿀 수 있다 — 다른 디스크에 두는 것이 안전하다.
    /// </summary>
    public static class Backups
    {
        public const string 설정파일 = "backup-folder.txt";
        public const int 기본보관 = 30;
        static readonly Regex 이름규칙 = new Regex(@"^납부알림-(\d{4}-\d{2}-\d{2})\.db$");

        public static string 폴더(string baseDir, string dataDir)
        {
            string cfg = Path.Combine(baseDir, 설정파일);
            try
            {
                if (File.Exists(cfg))
                {
                    foreach (string raw in File.ReadAllLines(cfg, Encoding.UTF8))
                    {
                        string line = raw.Trim().TrimStart('﻿').Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        string p = Environment.ExpandEnvironmentVariables(line.Trim('"'));
                        if (!Path.IsPathRooted(p)) p = Path.GetFullPath(Path.Combine(baseDir, p));
                        return p;
                    }
                }
            }
            catch { }
            return Path.Combine(dataDir, "backups");
        }

        /// <summary>오늘 사본이 없으면 만든다. 만들었으면 경로, 이미 있었으면 null.</summary>
        public static string 일일백업(Store db, string folder, DateTime today, int 보관수)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "납부알림-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".db");
            if (File.Exists(path)) return null;

            // 반쯤 쓴 사본이 오늘 사본으로 남지 않게, 다른 이름으로 만든 뒤 바꾼다.
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            db.Backup(tmp);
            File.Move(tmp, path);
            정리(folder, 보관수);
            return path;
        }

        /// <summary>지금 바로 사본을 만든다 (설정 화면의 '지금 백업'). 날짜별 사본과 이름이 겹치지 않는다.</summary>
        public static string 지금백업(Store db, string folder, DateTime 지금)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "납부알림-수동-" + 지금.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db");
            db.Backup(path);
            return path;
        }

        /// <summary>날짜별 사본 중 최근 보관수만 남긴다. 수동·이관전 사본은 건드리지 않는다.</summary>
        public static int 정리(string folder, int 보관수)
        {
            var dated = new List<string>();
            foreach (string f in Directory.GetFiles(folder, "납부알림-*.db"))
                if (이름규칙.IsMatch(Path.GetFileName(f))) dated.Add(f);
            dated.Sort(StringComparer.Ordinal);   // 날짜가 이름에 있어 이름순 = 날짜순
            int removed = 0;
            for (int i = 0; i < dated.Count - 보관수; i++)
            {
                try { File.Delete(dated[i]); removed++; } catch { }
            }
            return removed;
        }

        public sealed class 사본
        {
            public string 이름;
            public long 크기;
            public DateTime 만든시각;
        }

        public static List<사본> 목록(string folder)
        {
            var list = new List<사본>();
            if (!Directory.Exists(folder)) return list;
            foreach (string f in Directory.GetFiles(folder, "*.db"))
            {
                var fi = new FileInfo(f);
                list.Add(new 사본 { 이름 = fi.Name, 크기 = fi.Length, 만든시각 = fi.LastWriteTime });
            }
            list.Sort(delegate(사본 a, 사본 b) { return b.만든시각.CompareTo(a.만든시각); });
            return list;
        }
    }

    /// <summary>
    /// 사용자가 알아야 하는 자료 상태 경고. 팝업과 웹이 같은 문구를 쓴다 (ADR-0008).
    /// </summary>
    public static class Warnings
    {
        public const int 공휴일오래됨일 = 90;

        public static List<string> Build(Holidays.Cache cache, IEnumerable<int> years, DateTime today)
        {
            var parts = new List<string>();

            var missing = new List<string>();
            foreach (int y in years)
                if (!cache.Years.Contains(y)) missing.Add(y.ToString(CultureInfo.InvariantCulture));

            if (missing.Count > 0)
            {
                parts.Add(string.Format(
                    "{0}년 공휴일 자료가 없습니다. 해당 연도는 주말만 반영해 계산하며, 안전을 위해 알림을 이틀 앞당겼습니다. " +
                    "설정 화면에서 공공데이터포털 인증키를 넣고 공휴일을 갱신하세요.",
                    string.Join(", ", missing.ToArray())));
            }
            else if (cache.Updated.HasValue && (today.Date - cache.Updated.Value.Date).TotalDays > 공휴일오래됨일)
            {
                parts.Add(string.Format("공휴일 자료를 갱신한 지 {0}일 지났습니다. 임시공휴일이 반영되지 않았을 수 있습니다.",
                    (int)(today.Date - cache.Updated.Value.Date).TotalDays));
            }
            return parts;
        }
    }
}
