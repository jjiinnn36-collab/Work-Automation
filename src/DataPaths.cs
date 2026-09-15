using System;
using System.IO;
using System.Text;

namespace PaymentAlert
{
    /// <summary>
    /// 자료가 놓이는 위치.
    ///
    /// 자료 폴더 — DB, 증빙, 실행 기록, 공휴일 API 키. 실행 파일 옆 data-folder.txt 에
    ///            경로를 적으면 그곳을 쓰고, 없으면 실행 파일 옆 data 폴더를 쓴다.
    /// 가져오기 원천 — 실행 파일 옆 data 폴더. 옛 판 TSV 자료와 함께 배포하는 공휴일(holidays.tsv)이 있다.
    ///                자료 폴더를 옮겨도 도구가 쓰는 곳은 그대로라 둘을 따로 둔다.
    /// </summary>
    public static class DataPaths
    {
        public const string 설정파일 = "data-folder.txt";

        /// <summary>data-folder.txt 를 읽어 자료 폴더를 정한다. 못 읽으면 기본 위치로 떨어진다.</summary>
        public static string 자료폴더(string baseDir)
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
            catch { /* 설정을 못 읽는다고 알림이 멈추면 안 된다 */ }

            return 가져오기폴더(baseDir);
        }

        /// <summary>엑셀·납부서 도구가 TSV 를 쓰는 곳. 자료 폴더 설정과 무관하게 고정이다.</summary>
        public static string 가져오기폴더(string baseDir) { return Path.Combine(baseDir, "data"); }

        /// <summary>DB 로 옮기기 전 증빙이 있던 곳.</summary>
        public static string 옛증빙폴더(string baseDir) { return Path.Combine(baseDir, "증빙"); }

        public static string Db(string dataDir) { return Path.Combine(dataDir, Store.파일이름); }
        public static string 증빙(string dataDir) { return Path.Combine(dataDir, "증빙"); }
        public static string 로그(string dataDir) { return Path.Combine(dataDir, "run.log"); }
        public static string ApiKey(string dataDir) { return Path.Combine(dataDir, "apikey.txt"); }
    }
}
