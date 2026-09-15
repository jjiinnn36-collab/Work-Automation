using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// SQLite 저장소 시험. TSV 시절 TestRunner 가 지키던 약속을 DB 에서도 지키는지 본다.
    /// </summary>
    static class DbTests
    {
        static int passed = 0, failed = 0;

        static void Check(string name, object actual, object expected)
        {
            string a = Convert.ToString(actual, CultureInfo.InvariantCulture);
            string e = Convert.ToString(expected, CultureInfo.InvariantCulture);
            if (a == e) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  기대=" + e + " 실제=" + a); }
        }

        static void CheckTrue(string name, bool c)
        {
            if (c) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        static string 임시폴더(string tag)
        {
            string d = Path.Combine(Path.GetTempPath(), "pa_db_" + tag + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        static void 치우기(string d) { try { Directory.Delete(d, true); } catch { } }

        static void TSV쓰기(string path, string body) { File.WriteAllText(path, body, new UTF8Encoding(true)); }

        static PaymentItem 항목(string id, string 기관, string 비용명, Flow f, int 월, bool 말일, int 일,
                               int 알림, string 규칙, decimal? 고정)
        {
            var it = new PaymentItem();
            it.Id = id; it.기관 = 기관; it.비용명 = 비용명; it.진행흐름 = f; it.월 = 월;
            it.말일 = 말일; it.일 = 일; it.알림영업일 = 알림; it.금액규칙 = 규칙; it.고정금액 = 고정; it.비고 = "";
            return it;
        }

        static AmountRecord 금액(int y, string id, decimal amt, string 출처, string 비고)
        {
            var a = new AmountRecord();
            a.연도 = y; a.Id = id; a.금액 = amt; a.출처 = 출처; a.비고 = 비고;
            a.확인일 = new DateTime(2026, 3, 11);
            return a;
        }

        static StatusRecord 상태(int y, string id, int 단계, bool 변경)
        {
            var st = new StatusRecord();
            st.연도 = y; st.Id = id; st.단계 = 단계; st.변경됨 = 변경;
            return st;
        }

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                스키마();
                항목왕복();
                제약위반은되돌린다();
                시작일();
                금액합치기();
                진행상태왕복();
                동시저장();
                공휴일();
                증빙목록();
                백업();
                자료폴더설정();
                최초이전();
                다시가져오기();
                항목하나씩();
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  FAIL  예외: " + ex);
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 50));
            Console.WriteLine(string.Format("  통과 {0}건 / 실패 {1}건", passed, failed));
            Console.WriteLine(new string('=', 50));
            return failed == 0 ? 0 : 1;
        }

        static void 스키마()
        {
            Console.WriteLine("\n[DB-1] 새 파일을 열면 표가 만들어진다");
            string dir = 임시폴더("schema");
            try
            {
                string p = Path.Combine(dir, "t.db");
                using (Store s = Store.Open(p))
                {
                    CheckTrue("DB 파일 생성", File.Exists(p));
                    Check("스키마 버전", s.GetMeta("schema_version"), "1");
                    Check("빈 항목", s.LoadMaster().Count, 0);
                    Check("빈 진행 기록", s.LoadStatus().Count, 0);
                }
                using (Store s = Store.Open(p))
                    Check("다시 열어도 버전 유지", s.GetMeta("schema_version"), "1");
            }
            finally { 치우기(dir); }
        }

        static void 항목왕복()
        {
            Console.WriteLine("\n[DB-2] 항목 저장·복원");
            string dir = 임시폴더("master");
            try
            {
                var items = new List<PaymentItem>();
                items.Add(항목("fss-05", "금융감독원", "감독분담금", Flow.납부만, 5, false, 20, 3, "고지수령", null));
                items.Add(항목("fx-04", "기관나", "항목D", Flow.납부만, 10, true, 0, 3, "고정", 50000m));
                items.Add(항목("vat-q1", "국세청", "부가세", Flow.신고납부, 4, false, 25, 5, "수작업", null));

                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    s.ReplaceMaster(items);
                    var got = s.LoadMaster();
                    Check("건수", got.Count, 3);
                    Check("넣은 순서 유지 (첫 건)", got[0].Id, "fss-05");
                    Check("넣은 순서 유지 (마지막)", got[2].Id, "vat-q1");
                    Check("한글 기관 보존", got[0].기관, "금융감독원");
                    Check("진행흐름 보존", got[2].진행흐름, Flow.신고납부);
                    CheckTrue("말일 보존", got[1].말일);
                    Check("고정금액 원 단위 보존", got[1].고정금액.Value, 50000m);
                    CheckTrue("고정금액 없음은 null", !got[0].고정금액.HasValue);
                    Check("기한일 계산이 그대로", got[1].원기한일(2026).ToString("yyyy-MM-dd"), "2026-10-31");
                    CheckTrue("항목 갱신 시각 기록", s.GetMeta("master_updated_at") != null);

                    items.RemoveAt(2);
                    s.ReplaceMaster(items);
                    Check("전체 교체 — 빠진 항목은 사라짐", s.LoadMaster().Count, 2);
                }
            }
            finally { 치우기(dir); }
        }

        static void 제약위반은되돌린다()
        {
            Console.WriteLine("\n[DB-3] 잘못된 값은 DB 가 막고, 반쯤 쓴 것은 되돌린다");
            string dir = 임시폴더("check");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    var good = new List<PaymentItem>();
                    good.Add(항목("a", "기관", "정상", Flow.납부만, 3, false, 10, 3, "", null));
                    s.ReplaceMaster(good);

                    // 첫 행은 정상, 둘째 행의 월이 13 — 지우기와 첫 삽입까지 진행된 뒤 실패한다
                    var bad = new List<PaymentItem>();
                    bad.Add(항목("b", "기관", "먼저 들어갈 행", Flow.납부만, 4, false, 10, 3, "", null));
                    bad.Add(항목("c", "기관", "월이 잘못된 행", Flow.납부만, 13, false, 10, 3, "", null));

                    bool 막힘 = false;
                    try { s.ReplaceMaster(bad); } catch (InvalidOperationException) { 막힘 = true; }
                    CheckTrue("월 13 은 거부", 막힘);

                    var after = s.LoadMaster();
                    Check("실패하면 이전 항목이 그대로", after.Count, 1);
                    Check("이전 항목 id", after[0].Id, "a");
                }
            }
            finally { 치우기(dir); }
        }

        static void 시작일()
        {
            Console.WriteLine("\n[DB-4] 추적 시작일");
            string dir = 임시폴더("start");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    CheckTrue("처음엔 제한 없음", !s.LoadStartDate().HasValue);
                    s.SetStartDate(new DateTime(2026, 9, 1));
                    Check("시작일 보존", s.LoadStartDate().Value.ToString("yyyy-MM-dd"), "2026-09-01");
                    s.SetStartDate(null);
                    CheckTrue("지우면 다시 제한 없음", !s.LoadStartDate().HasValue);
                }
            }
            finally { 치우기(dir); }
        }

        static void 금액합치기()
        {
            Console.WriteLine("\n[DB-5] 연도별 금액은 합치기만 하고 지우지 않는다");
            string dir = 임시폴더("amount");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    s.UpsertAmounts(new AmountRecord[] {
                        금액(2026, "fss-05", 1840245m, "통보", "2회차"),
                        금액(2026, "kofia-10", 5838087m, "안내", "원단위 조정") });

                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "web-only", 100000m, "웹 입력", "") });

                    // 엑셀에서 다시 가져오며 한 건만 고친다 — web-only 는 이 목록에 없다
                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "fss-05", 1840246m, "통보 정정", "") });

                    var m = s.LoadAmounts();
                    Check("세 건 모두 남음", m.Count, 3);
                    CheckTrue("웹에서 넣은 금액이 살아남음", m.ContainsKey("2026\tweb-only"));
                    Check("고친 금액 반영", m["2026\tfss-05"].금액, 1840246m);
                    Check("출처도 갱신", m["2026\tfss-05"].출처, "통보 정정");
                    Check("원 단위 조정 금액 정확", m["2026\tkofia-10"].금액, 5838087m);
                    Check("확인일 보존", m["2026\tkofia-10"].확인일.Value.ToString("yyyy-MM-dd"), "2026-03-11");
                }
            }
            finally { 치우기(dir); }
        }

        static void 진행상태왕복()
        {
            Console.WriteLine("\n[DB-6] 진행 상태 저장·복원");
            string dir = 임시폴더("status");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    var s1 = 상태(2026, "fx-03", 1, true);
                    s1.변경일시 = new DateTime(2026, 4, 17, 9, 12, 34);
                    s1.최종확인일 = new DateTime(2026, 4, 20);
                    s1.메모 = "결재 상신, 승인 대기";
                    var 손안댐 = 상태(2026, "fx-07", 0, false);

                    s.SaveStatus(new StatusRecord[] { s1, 손안댐 });
                    var m = s.LoadStatus();
                    Check("바꾼 것만 저장", m.Count, 1);
                    CheckTrue("손대지 않은 기록은 저장 안 됨", !m.ContainsKey("2026\tfx-07"));
                    Check("단계 보존", m["2026\tfx-03"].단계, 1);
                    Check("변경일시 초까지 보존", m["2026\tfx-03"].변경일시.Value.ToString("yyyy-MM-dd HH:mm:ss"), "2026-04-17 09:12:34");
                    Check("최종확인일 보존", m["2026\tfx-03"].최종확인일.Value.ToString("yyyy-MM-dd"), "2026-04-20");
                    Check("한글 메모 보존", m["2026\tfx-03"].메모, "결재 상신, 승인 대기");

                    s1.단계 = 2;
                    s1.최종확인일 = null;
                    s.SaveStatus(new StatusRecord[] { s1 });
                    var m2 = s.LoadStatus();
                    Check("덮어쓰기 후 단계 갱신", m2["2026\tfx-03"].단계, 2);
                    CheckTrue("최종확인일을 비울 수 있음", !m2["2026\tfx-03"].최종확인일.HasValue);
                    Check("덮어쓰기 후 건수 유지", m2.Count, 1);
                }
            }
            finally { 치우기(dir); }
        }

        static void 동시저장()
        {
            Console.WriteLine("\n[DB-7] 두 연결이 같은 파일에 써도 서로의 변경을 지우지 않는다");
            // TSV 시절 [11c-2] 와 같은 상황. 팝업이 열린 동안 웹이 다른 건을 바꾼다.
            string dir = 임시폴더("concurrent");
            try
            {
                string p = Path.Combine(dir, "t.db");
                using (Store 팝업 = Store.Open(p))
                using (Store 웹 = Store.Open(p))
                {
                    var 팝업메모리 = new StatusRecord[] { 상태(2026, "fx-03", 1, true), 상태(2026, "fx-04", 0, false) };
                    팝업.SaveStatus(팝업메모리);

                    웹.SaveStatus(new StatusRecord[] { 상태(2026, "fx-05", 1, true) });

                    // 팝업이 닫히며 자기 메모리를 저장한다 — fx-05 는 모르는 상태
                    팝업.SaveStatus(팝업메모리);

                    var 최종 = 웹.LoadStatus();
                    CheckTrue("웹이 바꾼 건이 살아남음", 최종.ContainsKey("2026\tfx-05"));
                    Check("웹 변경값 유지", 최종["2026\tfx-05"].단계, 1);
                    Check("팝업 변경값도 유지", 최종["2026\tfx-03"].단계, 1);
                    CheckTrue("손대지 않은 기본값은 기록되지 않음", !최종.ContainsKey("2026\tfx-04"));

                    StatusRecord 되돌릴것 = 팝업.LoadStatus()["2026\tfx-03"];
                    되돌릴것.단계 = 0;
                    되돌릴것.변경됨 = true;
                    팝업.SaveStatus(new StatusRecord[] { 되돌릴것 });

                    var 되돌린뒤 = 웹.LoadStatus();
                    Check("되돌리기가 다른 연결에도 보임", 되돌린뒤["2026\tfx-03"].단계, 0);
                    Check("다른 건은 그대로", 되돌린뒤["2026\tfx-05"].단계, 1);
                }
            }
            finally { 치우기(dir); }
        }

        static void 공휴일()
        {
            Console.WriteLine("\n[DB-8] 공휴일 — 갱신 시각은 옮겨도 초기화되지 않는다");
            string dir = 임시폴더("holiday");
            try
            {
                var cache = new Holidays.Cache();
                cache.Dates[new DateTime(2026, 1, 1)] = "1월1일";
                cache.Dates[new DateTime(2026, 9, 25)] = "추석";
                cache.Years.Add(2026);
                cache.Years.Add(2027);   // 받았는데 날짜가 아직 없는 해
                cache.Updated = new DateTime(2026, 8, 23);

                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    s.SaveHolidays(cache);
                    Holidays.Cache got = s.LoadHolidays();
                    Check("공휴일 건수", got.Dates.Count, 2);
                    Check("한글 명칭 보존", got.Dates[new DateTime(2026, 9, 25)], "추석");
                    Check("갱신일은 원래 값", got.Updated.Value.ToString("yyyy-MM-dd"), "2026-08-23");
                    CheckTrue("날짜가 없는 해도 받은 해로 기억", got.Years.Contains(2027));

                    var cal = new BusinessDayCalendar(got.Dates.Keys, got.Years);
                    CheckTrue("달력이 공휴일을 앎", cal.IsHoliday(new DateTime(2026, 9, 25)));
                }
            }
            finally { 치우기(dir); }
        }

        static void 증빙목록()
        {
            Console.WriteLine("\n[DB-9] 증빙 목록");
            string dir = 임시폴더("attach");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    var a1 = new Attachment();
                    a1.연도 = 2026; a1.Id = "fx-03"; a1.단계 = "전표결재";
                    a1.저장파일 = "2026\\fx-03\\전표결재_20260906-154122_신고서.pdf";
                    a1.원본파일명 = "신고서 원본.pdf";
                    a1.첨부일시 = new DateTime(2026, 9, 6, 15, 41, 22);

                    var a2 = new Attachment();
                    a2.연도 = 2026; a2.Id = "fx-03"; a2.단계 = "납부완료";
                    a2.저장파일 = "2026\\fx-03\\납부완료_20260907-090000_영수증.png";
                    a2.원본파일명 = "영수증.png";
                    a2.첨부일시 = new DateTime(2026, 9, 7, 9, 0, 0);

                    s.AddAttachment(a2);   // 일부러 늦은 것을 먼저 넣는다
                    s.AddAttachment(a1);

                    var list = s.LoadAttachments();
                    Check("2건", list.Count, 2);
                    Check("첨부 시각 순으로 정렬", list[0].단계, "전표결재");
                    Check("한글 파일명 보존", list[0].원본파일명, "신고서 원본.pdf");
                    Check("상대 경로 보존", list[0].저장파일, "2026\\fx-03\\전표결재_20260906-154122_신고서.pdf");

                    s.RemoveAttachment(list[0]);
                    var after = s.LoadAttachments();
                    Check("한 건 삭제", after.Count, 1);
                    Check("남은 건", after[0].단계, "납부완료");
                }
            }
            finally { 치우기(dir); }
        }

        static void 백업()
        {
            Console.WriteLine("\n[DB-10] 백업은 쓰는 도중에도 온전한 사본을 만든다");
            string dir = 임시폴더("backup");
            try
            {
                string p = Path.Combine(dir, "t.db");
                string bak = Path.Combine(dir, "사본.db");
                using (Store s = Store.Open(p))
                {
                    s.SaveStatus(new StatusRecord[] { 상태(2026, "fx-03", 2, true) });
                    s.Backup(bak);   // 연결이 열린 채 — WAL 에만 있는 변경도 사본에 들어가야 한다
                }
                CheckTrue("사본 파일 생성", File.Exists(bak));
                using (Store b = Store.Open(bak))
                    Check("사본에 최근 변경이 들어 있음", b.LoadStatus()["2026\tfx-03"].단계, 2);
            }
            finally { 치우기(dir); }
        }

        static void 자료폴더설정()
        {
            Console.WriteLine("\n[DB-11] 자료 폴더 위치 설정");
            string b = 임시폴더("paths");
            try
            {
                Check("설정 없으면 exe 옆 data", DataPaths.자료폴더(b), Path.Combine(b, "data"));

                string cfg = Path.Combine(b, DataPaths.설정파일);
                string 원하는곳 = Path.Combine(b, "내 자료");
                File.WriteAllText(cfg, "# 자료 폴더\r\n\r\n\"" + 원하는곳 + "\"\r\n", new UTF8Encoding(true));
                Check("주석·빈 줄 건너뛰고 따옴표 벗김", DataPaths.자료폴더(b), 원하는곳);

                File.WriteAllText(cfg, "상대폴더\r\n", new UTF8Encoding(false));
                Check("상대 경로는 exe 기준", DataPaths.자료폴더(b), Path.Combine(b, "상대폴더"));

                Check("DB 경로", DataPaths.Db(원하는곳), Path.Combine(원하는곳, "납부알림.db"));
                Check("증빙은 자료 폴더 안", DataPaths.증빙(원하는곳), Path.Combine(원하는곳, "증빙"));
                Check("가져오기 원천은 설정과 무관", DataPaths.가져오기폴더(b), Path.Combine(b, "data"));
            }
            finally { 치우기(b); }
        }

        static void 최초이전()
        {
            Console.WriteLine("\n[DB-12] TSV → DB 최초 이전");
            string baseDir = 임시폴더("migrate");
            try
            {
                string src = Path.Combine(baseDir, "data");
                Directory.CreateDirectory(src);

                TSV쓰기(Path.Combine(src, "payment-master.tsv"),
                    "id\t기관\t비용명\t진행흐름\t월\t일\t알림영업일\t금액규칙\t고정금액\t비고\r\n" +
                    "kofia-09\t금융투자협회\t금융투자협회회비\t납부만\t9\t20\t3\t고지수령\t\t\r\n" +
                    "fss-10\t금융감독원\t감독분담금\t납부만\t10\tEOM\t3\t고지수령\t\t4회차\r\n" +
                    "bad\t기관\t월이 잘못됨\t납부만\t13\t1\t3\t\t\t\r\n");
                TSV쓰기(Path.Combine(src, "amounts.tsv"),
                    "#주석\r\n연도\tid\t금액\t출처\t확인일\t비고\r\n" +
                    "2026\tkofia-09\t5,838,089\t안내\t2026-05-13\t5차\r\n");
                TSV쓰기(Path.Combine(src, "status.tsv"),
                    "연도\tid\t단계\t단계명\t변경일시\t최종확인일\t메모\r\n" +
                    "2026\tkofia-09\t1\t전표결재\t2026-09-10 14:03\t2026-09-10\t상신\r\n");
                TSV쓰기(Path.Combine(src, "start-date.txt"), "# 시작일\r\n2026-09-01\r\n");
                TSV쓰기(Path.Combine(src, "holidays.tsv"), "#updated\t2026-08-23\r\n날짜\t명칭\r\n2026-09-25\t추석\r\n");
                File.WriteAllText(Path.Combine(src, "apikey.txt"), "TESTKEY");

                // 옛 증빙은 exe 옆 증빙 폴더에 있었다
                string 옛증빙 = Path.Combine(baseDir, "증빙");
                string rel = "2026\\kofia-09\\전표결재_20260910-140300_안내문.pdf";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(옛증빙, rel)));
                File.WriteAllText(Path.Combine(옛증빙, rel), "PDF 내용");
                TSV쓰기(Path.Combine(src, "attachments.tsv"),
                    "#목록\r\n연도\tid\t단계\t저장파일\t원본파일명\t첨부일시\r\n" +
                    "2026\tkofia-09\t전표결재\t" + rel + "\t안내문.pdf\t2026-09-10 14:03\r\n");

                // 자료 폴더를 다른 곳으로 지정한 경우
                string dataDir = Path.Combine(baseDir, "새 자료 폴더");
                var log = new List<string>();
                Importer.최초이전(baseDir, dataDir, log);

                string db = DataPaths.Db(dataDir);
                CheckTrue("DB 생성", File.Exists(db));
                CheckTrue("임시 파일이 남지 않음", !File.Exists(db + ".importing"));

                using (Store s = Store.Open(db))
                {
                    List<PaymentItem> m = s.LoadMaster();
                    Check("잘못된 행은 빼고 항목 2건", m.Count, 2);
                    CheckTrue("말일 항목도 옮김", m[1].말일);
                    Check("쉼표 금액 파싱", s.LoadAmounts()["2026\tkofia-09"].금액, 5838089m);
                    Check("진행 단계 옮김", s.LoadStatus()["2026\tkofia-09"].단계, 1);
                    Check("메모 옮김", s.LoadStatus()["2026\tkofia-09"].메모, "상신");
                    Check("시작일 옮김", s.LoadStartDate().Value.ToString("yyyy-MM-dd"), "2026-09-01");
                    Check("공휴일 갱신일 보존", s.LoadHolidays().Updated.Value.ToString("yyyy-MM-dd"), "2026-08-23");
                    Check("증빙 목록 옮김", s.LoadAttachments().Count, 1);
                    CheckTrue("이전 시각 기록", s.GetMeta("migrated_from_tsv_at") != null);
                }

                CheckTrue("증빙 파일이 새 폴더로 복사됨", File.Exists(Path.Combine(DataPaths.증빙(dataDir), rel)));
                CheckTrue("옛 증빙 파일은 그대로", File.Exists(Path.Combine(옛증빙, rel)));
                CheckTrue("원본 TSV 는 지우지 않음", File.Exists(Path.Combine(src, "payment-master.tsv")));
                CheckTrue("API 키 복사", File.Exists(DataPaths.ApiKey(dataDir)));

                bool 경고있음 = false;
                foreach (string l in log) if (l.IndexOf("월 값이 잘못", StringComparison.Ordinal) >= 0) 경고있음 = true;
                CheckTrue("잘못된 행을 기록에 남김", 경고있음);

                // 이미 DB 가 있으면 다시 옮기지 않는다 — 그 뒤 쌓인 기록을 덮으면 안 된다
                using (Store s = Store.Open(db))
                    s.SaveStatus(new StatusRecord[] { 상태(2026, "kofia-09", 2, true) });

                bool 거부 = false;
                try { Importer.최초이전(baseDir, dataDir, new List<string>()); }
                catch (IOException) { 거부 = true; }
                CheckTrue("DB 가 있으면 다시 옮기지 않음", 거부);
                CheckTrue("거부할 때 임시 파일을 남기지 않음", !File.Exists(db + ".importing"));
                using (Store s = Store.Open(db))
                    Check("이전 뒤 쌓인 기록 유지", s.LoadStatus()["2026\tkofia-09"].단계, 2);
            }
            finally { 치우기(baseDir); }
        }

        static void 다시가져오기()
        {
            Console.WriteLine("\n[DB-13] 엑셀·납부서 도구 뒤 다시 가져오기");
            string baseDir = 임시폴더("reimport");
            try
            {
                string src = Path.Combine(baseDir, "data");
                Directory.CreateDirectory(src);
                string dataDir = src;   // 기본 위치를 그대로 쓰는 경우

                using (Store s = Store.Open(DataPaths.Db(dataDir)))
                {
                    var items = new List<PaymentItem>();
                    items.Add(항목("old", "기관", "옛 항목", Flow.납부만, 3, false, 10, 3, "", null));
                    s.ReplaceMaster(items);
                    s.SaveStatus(new StatusRecord[] { 상태(2026, "old", 2, true) });
                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "web-only", 7777m, "웹", "") });
                }

                TSV쓰기(Path.Combine(src, "payment-master.tsv"),
                    "id\t기관\t비용명\t진행흐름\t월\t일\t알림영업일\t금액규칙\t고정금액\t비고\r\n" +
                    "new\t기관\t새 항목\t제출만\t6\t30\t3\t해당없음\t\t\r\n");
                TSV쓰기(Path.Combine(src, "amounts.tsv"),
                    "연도\tid\t금액\t출처\t확인일\t비고\r\n2026\tnew\t123\t고지서\t2026-06-01\t\r\n");

                var log = new List<string>();
                Check("항목 가져오기 성공", Importer.항목다시가져오기(baseDir, dataDir, log), 0);
                Check("금액 가져오기 성공", Importer.금액다시가져오기(baseDir, dataDir, log), 0);

                using (Store s = Store.Open(DataPaths.Db(dataDir)))
                {
                    var m = s.LoadMaster();
                    Check("항목은 엑셀 기준으로 교체", m.Count, 1);
                    Check("새 항목", m[0].Id, "new");
                    Check("진행 기록은 건드리지 않음", s.LoadStatus()["2026\told"].단계, 2);
                    var am = s.LoadAmounts();
                    CheckTrue("웹에서 넣은 금액 유지", am.ContainsKey("2026\tweb-only"));
                    Check("가져온 금액 추가", am["2026\tnew"].금액, 123m);
                }

                // 항목 파일이 비면 기존 항목을 지우지 않는다
                TSV쓰기(Path.Combine(src, "payment-master.tsv"), "id\t기관\t비용명\t진행흐름\t월\t일\r\n");
                CheckTrue("빈 항목 파일은 거부", Importer.항목다시가져오기(baseDir, dataDir, new List<string>()) != 0);
                using (Store s = Store.Open(DataPaths.Db(dataDir)))
                    Check("기존 항목 보존", s.LoadMaster().Count, 1);
            }
            finally { 치우기(baseDir); }
        }

        static void 항목하나씩()
        {
            Console.WriteLine("\n[DB-14] 항목을 한 건씩 넣고 고치고 지운다 — 웹 항목 관리");
            string dir = 임시폴더("item");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    var items = new List<PaymentItem>();
                    items.Add(항목("a", "기관", "첫째", Flow.납부만, 3, false, 10, 3, "", null));
                    items.Add(항목("b", "기관", "둘째", Flow.납부만, 4, false, 10, 3, "", null));
                    s.ReplaceMaster(items);

                    s.UpsertItem(항목("c", "새 기관", "셋째", Flow.제출만, 6, true, 0, 5, "해당없음", null));
                    var m = s.LoadMaster();
                    Check("새 항목은 맨 뒤", m[2].Id, "c");
                    Check("넣은 뒤 건수", m.Count, 3);

                    s.UpsertItem(항목("a", "기관", "첫째 (이름 바꿈)", Flow.신고납부, 3, false, 25, 5, "수작업", 1000m));
                    m = s.LoadMaster();
                    Check("고친 항목은 제자리", m[0].Id, "a");
                    Check("이름 반영", m[0].비용명, "첫째 (이름 바꿈)");
                    Check("흐름 반영", m[0].진행흐름, Flow.신고납부);
                    Check("고정금액 반영", m[0].고정금액.Value, 1000m);
                    Check("고쳐도 건수 그대로", m.Count, 3);

                    s.SaveStatus(new StatusRecord[] { 상태(2026, "b", 1, true) });
                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "b", 500m, "고지서", "") });
                    s.DeleteItem("b");
                    m = s.LoadMaster();
                    Check("지운 뒤 건수", m.Count, 2);
                    CheckTrue("지운 항목은 목록에 없음", m.Find(delegate(PaymentItem x) { return x.Id == "b"; }) == null);
                    CheckTrue("진행 기록은 남음", s.LoadStatus().ContainsKey("2026\tb"));
                    CheckTrue("금액도 남음", s.LoadAmounts().ContainsKey("2026\tb"));

                    s.UpsertItem(항목("b", "기관", "다시 넣은 둘째", Flow.납부만, 4, false, 10, 3, "", null));
                    Check("같은 id 로 다시 넣으면 기록이 이어짐", s.LoadStatus()["2026\tb"].단계, 1);

                    s.DeleteAmount(2026, "b");
                    CheckTrue("금액 한 건 삭제", !s.LoadAmounts().ContainsKey("2026\tb"));
                }
            }
            finally { 치우기(dir); }
        }    }
}
