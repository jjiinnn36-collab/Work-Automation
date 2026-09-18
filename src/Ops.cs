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
            return 기본폴더(dataDir);
        }

        /// <summary>기본 위치 = 자료 폴더 안 backups.</summary>
        public static string 기본폴더(string dataDir) { return Path.Combine(dataDir, "backups"); }

        /// <summary>backup-folder.txt 로 위치를 따로 정해 두었는가.</summary>
        public static bool 따로정함(string baseDir)
        {
            return File.Exists(Path.Combine(baseDir, 설정파일));
        }

        static readonly string[] 동기화폴더 = { "OneDrive", "Dropbox", "Google Drive", "GoogleDrive", "iCloudDrive", "iCloud Drive", "내 드라이브", "My Drive", "MYBOX", "네이버 MYBOX" };

        /// <summary>
        /// 화면에서 고른 백업 위치를 검사한다. 잘못이면 이유, 괜찮으면 null.
        /// 사본에는 실제 자료가 그대로 들어 있어 이 PC 밖으로 나가는 곳(네트워크·클라우드 동기화 폴더)은 막는다.
        /// 웹 화면 폴더 안은 브라우저로 내려받을 수 있어 막는다.
        /// </summary>
        public static string 폴더검사(string folder, string webRoot)
        {
            string p = (folder ?? "").Trim().Trim('"');
            if (p.Length == 0) return null;
            if (p.StartsWith(@"\\") || p.StartsWith("//")) return "네트워크 폴더는 쓸 수 없습니다. 이 PC 의 폴더를 넣으세요.";
            if (p.Length < 3 || !char.IsLetter(p[0]) || p[1] != ':' || (p[2] != '\\' && p[2] != '/'))
                return @"C:\백업 처럼 드라이브부터 쓴 전체 경로를 넣으세요.";
            string full;
            try { full = Path.GetFullPath(p); }
            catch { return "폴더 경로가 올바르지 않습니다."; }
            try
            {
                var drive = new DriveInfo(full.Substring(0, 1));
                if (drive.DriveType == DriveType.Network) return "네트워크 드라이브는 쓸 수 없습니다. 이 PC 의 폴더를 넣으세요.";
                if (drive.DriveType == DriveType.NoRootDirectory || !drive.IsReady) return "그 드라이브를 찾을 수 없습니다.";
                string label = "";
                try { label = drive.VolumeLabel ?? ""; } catch { }
                foreach (string name in 동기화폴더)
                    if (label.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return "클라우드 동기화 드라이브(" + label + ")는 쓸 수 없습니다. 사본이 PC 밖으로 올라갑니다.";
            }
            catch (ArgumentException) { return "그 드라이브를 찾을 수 없습니다."; }
            foreach (string seg in full.Split('\\', '/'))
                foreach (string name in 동기화폴더)
                    if (seg.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                        return "클라우드 동기화 폴더(" + seg + ")는 쓸 수 없습니다. 사본이 PC 밖으로 올라갑니다.";
            foreach (string env in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                string od = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(od) && 안에있음(full, od))
                    return "OneDrive 폴더는 쓸 수 없습니다. 사본이 PC 밖으로 올라갑니다.";
            }
            if (!string.IsNullOrEmpty(webRoot) && 안에있음(full, webRoot))
                return "웹 화면 폴더 안에는 둘 수 없습니다.";
            try
            {
                Directory.CreateDirectory(full);
                string probe = Path.Combine(full, ".쓰기확인-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex) { return "그 폴더에 쓸 수 없습니다: " + ex.Message; }
            return null;
        }

        static bool 안에있음(string path, string root)
        {
            string a = Path.GetFullPath(path).TrimEnd('\\') + "\\";
            string b = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>위치를 backup-folder.txt 에 적는다. 빈 값이면 파일을 지워 기본 위치로 돌린다. 이미 만든 사본은 옮기지 않는다.</summary>
        public static void 폴더저장(string baseDir, string folder)
        {
            string cfg = Path.Combine(baseDir, 설정파일);
            string p = (folder ?? "").Trim().Trim('"');
            if (p.Length == 0) { if (File.Exists(cfg)) File.Delete(cfg); return; }
            string tmp = cfg + ".tmp";
            File.WriteAllText(tmp, "# 백업 폴더 (설정 화면에서 바꿈)\r\n" + Path.GetFullPath(p) + "\r\n", new UTF8Encoding(false));
            if (File.Exists(cfg)) File.Delete(cfg);
            File.Move(tmp, cfg);
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
