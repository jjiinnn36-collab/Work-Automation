using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PaymentAlert
{
    /// <summary>
    /// TSV 자료를 DB 로 옮긴다. 원본 TSV 와 옛 증빙 파일은 지우지 않는다.
    /// TSV 를 읽는 검증 규칙은 Repository 의 것을 그대로 쓴다 — 두 벌로 두면 어긋난다.
    /// </summary>
    public static class Importer
    {
        /// <summary>
        /// 처음 한 번. 임시 파일에 전부 옮긴 뒤 이름을 바꿔 확정한다.
        /// 중간에 실패하면 반쪽짜리 DB 가 남지 않고, 다음 실행에 처음부터 다시 한다.
        /// </summary>
        public static void 최초이전(string baseDir, string dataDir, List<string> log)
        {
            string src = DataPaths.가져오기폴더(baseDir);
            string db = DataPaths.Db(dataDir);

            // 이미 옮겼으면 손대지 않는다. 그 뒤에 쌓인 기록을 옛 TSV 로 덮으면 안 된다.
            if (File.Exists(db)) throw new IOException("이미 DB 가 있어 다시 옮기지 않습니다: " + db);

            string tmp = db + ".importing";
            string 옛증빙 = DataPaths.옛증빙폴더(baseDir);
            string 새증빙 = DataPaths.증빙(dataDir);
            지우기(tmp);

            try
            {
                using (Store s = Store.Open(tmp))
                {
                    var warnings = new List<string>();
                    List<PaymentItem> master = Repository.LoadMaster(Path.Combine(src, "payment-master.tsv"), warnings);
                    s.ReplaceMaster(master);
                    log.Add(string.Format("항목 {0}건을 옮겼습니다.", master.Count));
                    foreach (string w in warnings) log.Add("  항목 확인: " + w);

                    var amWarn = new List<string>();
                    Dictionary<string, AmountRecord> amounts = Repository.LoadAmounts(Path.Combine(src, "amounts.tsv"), amWarn);
                    s.UpsertAmounts(amounts.Values);
                    log.Add(string.Format("금액 {0}건을 옮겼습니다.", amounts.Count));
                    foreach (string w in amWarn) log.Add("  금액 확인: " + w);

                    // 파일에 있던 진행 기록은 전부 사용자가 남긴 것이다. 모두 저장 대상으로 표시한다.
                    Dictionary<string, StatusRecord> status = Repository.LoadStatus(Path.Combine(src, "status.tsv"));
                    foreach (StatusRecord st in status.Values) st.변경됨 = true;
                    s.SaveStatus(status.Values);
                    log.Add(string.Format("진행 기록 {0}건을 옮겼습니다.", status.Count));

                    DateTime? 시작일 = Repository.LoadStartDate(Path.Combine(src, "start-date.txt"));
                    s.SetStartDate(시작일);
                    if (시작일.HasValue) log.Add("추적 시작일 " + 시작일.Value.ToString("yyyy-MM-dd") + " 을 옮겼습니다.");

                    Holidays.Cache cache = Holidays.Load(Path.Combine(src, "holidays.tsv"));
                    s.SaveHolidays(cache);
                    log.Add(string.Format("공휴일 {0}건을 옮겼습니다.", cache.Dates.Count));

                    int 옮김 = 증빙옮기기(s, Path.Combine(src, "attachments.tsv"), 옛증빙, 새증빙, log);
                    log.Add(string.Format("증빙 {0}건을 옮겼습니다.", 옮김));

                    s.SetMeta("migrated_from_tsv_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

                    // WAL 을 본 파일에 합쳐야 이름을 바꾼 뒤에도 자료가 온전하다.
                    s.단일파일로();
                }

                // 공휴일 API 키는 자료 폴더에서 찾으므로, 폴더를 옮겼다면 함께 복사한다.
                string 옛키 = DataPaths.ApiKey(src);
                string 새키 = DataPaths.ApiKey(dataDir);
                if (File.Exists(옛키) && !File.Exists(새키) && !같은경로(옛키, 새키))
                {
                    File.Copy(옛키, 새키);
                    log.Add("공휴일 API 키를 자료 폴더로 복사했습니다.");
                }

                File.Move(tmp, db);
            }
            finally
            {
                // 성공했으면 tmp 는 이미 옮겨졌고 남은 부속 파일만 치운다.
                // 실패했으면 반쪽 DB 를 지워 다음 실행이 처음부터 다시 하게 한다.
                지우기(tmp);
            }
        }

        /// <summary>
        /// 증빙 목록을 옮기고 실제 파일을 새 위치로 복사한다.
        /// 저장 경로가 증빙 폴더 기준 상대 경로라, 루트만 바뀌어도 그대로 이어진다.
        /// </summary>
        static int 증빙옮기기(Store s, string indexPath, string 옛루트, string 새루트, List<string> log)
        {
            int n = 0;
            bool 같은곳 = 같은경로(옛루트, 새루트);

            foreach (var row in Tsv.Read(indexPath))
            {
                string id = Tsv.Get(row, "id");
                int year = Tsv.GetInt(row, "연도", 0);
                string saved = Tsv.Get(row, "저장파일");
                if (id.Length == 0 || year == 0 || saved.Length == 0) continue;

                if (!같은곳 && !Path.IsPathRooted(saved))
                {
                    string from = Path.Combine(옛루트, saved);
                    string to = Path.Combine(새루트, saved);
                    if (File.Exists(from))
                    {
                        string dir = Path.GetDirectoryName(to);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        if (!File.Exists(to)) File.Copy(from, to);   // 다시 시도해도 이미 복사한 것은 건너뛴다
                    }
                    else
                    {
                        log.Add("  증빙 파일이 없어 목록만 옮깁니다: " + from);
                    }
                }

                var a = new Attachment();
                a.연도 = year;
                a.Id = id;
                a.단계 = Tsv.Get(row, "단계");
                a.저장파일 = saved;
                a.원본파일명 = Tsv.Get(row, "원본파일명");
                DateTime? d = Tsv.GetDate(row, "첨부일시");
                a.첨부일시 = d.HasValue ? d.Value : DateTime.MinValue;
                s.AddAttachment(a);
                n++;
            }
            return n;
        }

        /// <summary>
        /// 엑셀 변환 뒤 항목을 다시 가져온다. 엑셀이 항목의 원본이므로 전체를 바꾼다.
        /// 진행 기록과 금액은 건드리지 않는다.
        /// </summary>
        public static int 항목다시가져오기(string baseDir, string dataDir, List<string> log)
        {
            string path = Path.Combine(DataPaths.가져오기폴더(baseDir), "payment-master.tsv");
            var warnings = new List<string>();
            List<PaymentItem> master;
            try { master = Repository.LoadMaster(path, warnings); }
            catch (FileNotFoundException) { log.Add("항목 파일이 없거나 비어 있습니다: " + path); return 2; }

            if (master.Count == 0) { log.Add("가져올 유효한 항목이 없습니다. 기존 자료를 그대로 둡니다."); return 2; }

            using (Store s = Store.Open(DataPaths.Db(dataDir)))
                s.ReplaceMaster(master);

            log.Add(string.Format("항목 {0}건을 가져왔습니다.", master.Count));
            foreach (string w in warnings) log.Add("  확인: " + w);
            return 0;
        }

        /// <summary>납부서 판독 뒤 금액을 가져온다. 합치기만 하고 지우지 않는다.</summary>
        public static int 금액다시가져오기(string baseDir, string dataDir, List<string> log)
        {
            string path = Path.Combine(DataPaths.가져오기폴더(baseDir), "amounts.tsv");
            if (!File.Exists(path)) { log.Add("금액 파일이 없습니다: " + path); return 2; }

            var warnings = new List<string>();
            Dictionary<string, AmountRecord> amounts = Repository.LoadAmounts(path, warnings);

            using (Store s = Store.Open(DataPaths.Db(dataDir)))
                s.UpsertAmounts(amounts.Values);

            log.Add(string.Format("금액 {0}건을 가져왔습니다.", amounts.Count));
            foreach (string w in warnings) log.Add("  확인: " + w);
            return 0;
        }

        static bool 같은경로(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
                                 Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        static void 지우기(string dbPath)
        {
            foreach (string f in new string[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}
