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
                항목하나씩();
                스키마이관();
                단계변경은원자적();
                두창이동시에누름();
                순서옮기기();
                자료준비();
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
                    Check("스키마 버전", s.GetMeta("schema_version"), Store.스키마버전.ToString());
                    Check("빈 항목", s.LoadMaster().Count, 0);
                    Check("빈 진행 기록", s.LoadStatus().Count, 0);
                }
                using (Store s = Store.Open(p))
                    Check("다시 열어도 버전 유지", s.GetMeta("schema_version"), Store.스키마버전.ToString());
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
                        금액(2026, "kofia-10", 1234558m, "안내", "원단위 조정") });

                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "web-only", 100000m, "웹 입력", "") });

                    // 엑셀에서 다시 가져오며 한 건만 고친다 — web-only 는 이 목록에 없다
                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "fss-05", 1840246m, "통보 정정", "") });

                    var m = s.LoadAmounts();
                    Check("세 건 모두 남음", m.Count, 3);
                    CheckTrue("웹에서 넣은 금액이 살아남음", m.ContainsKey("2026\tweb-only"));
                    Check("고친 금액 반영", m["2026\tfss-05"].금액, 1840246m);
                    Check("출처도 갱신", m["2026\tfss-05"].출처, "통보 정정");
                    Check("원 단위 조정 금액 정확", m["2026\tkofia-10"].금액, 1234558m);
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
                    "2026\tkofia-09\t1,234,560\t안내\t2026-05-13\t5차\r\n");
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
                    Check("쉼표 금액 파싱", s.LoadAmounts()["2026\tkofia-09"].금액, 1234560m);
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

        static void 자료준비()
        {
            Console.WriteLine("\n[DB-19] 처음 실행: 옛 TSV 가 없으면 빈 DB 와 공휴일만 (ADR-0013)");
            string baseDir = 임시폴더("fresh");
            string base2 = 임시폴더("fresh-tsv");
            try
            {
                string src = Path.Combine(baseDir, "data");
                Directory.CreateDirectory(src);
                TSV쓰기(Path.Combine(src, "holidays.tsv"), "날짜\t명칭\r\n2026-10-09\t한글날\r\n2026-12-25\t성탄절\r\n");
                var log = new List<string>();
                Check("DB 가 없고 TSV 도 없으면 새로 만듦", Importer.자료준비(baseDir, src, log), Importer.준비결과.새로만듦);
                using (Store s = Store.Open(DataPaths.Db(src)))
                {
                    Check("항목 0건", s.LoadMaster().Count, 0);
                    Check("공휴일은 들어감", s.LoadHolidays().Dates.Count, 2);
                    Check("최신 판", s.버전, Store.스키마버전);
                }
                CheckTrue("안내 문구", log.Exists(delegate(string l) { return l.Contains("항목 관리"); }));
                Check("두 번째는 이미 있음", Importer.자료준비(baseDir, src, new List<string>()), Importer.준비결과.이미있음);

                string src2 = Path.Combine(base2, "data");
                Directory.CreateDirectory(src2);
                TSV쓰기(Path.Combine(src2, "payment-master.tsv"),
                    "id\t기관\t비용명\t진행흐름\t월\t일\t알림영업일\t금액규칙\t고정금액\t비고\r\n" +
                    "a\t기관\t항목\t납부만\t6\t30\t3\t고지수령\t\t\r\n");
                Check("옛 TSV 가 있으면 옮김", Importer.자료준비(base2, src2, new List<string>()), Importer.준비결과.TSV옮김);
                using (Store s = Store.Open(DataPaths.Db(src2)))
                    Check("옮긴 항목의 규칙은 변동", s.LoadMaster()[0].금액규칙, "변동");
            }
            finally { 치우기(baseDir); 치우기(base2); }
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
        }

        static void 스키마이관()
        {
            Console.WriteLine("\n[DB-15] 1판 파일을 열면 2판으로 옮긴다 — 자료는 잃지 않는다 (ADR-0006)");
            string dir = 임시폴더("migrate");
            try
            {
                string p = Path.Combine(dir, "old.db");
                // 2026-09-15 에 배포한 1판과 같은 모양의 파일을 직접 만든다.
                using (var c = new Conn(p))
                {
                    c.Run("CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT)");
                    c.Run("CREATE TABLE items(id TEXT PRIMARY KEY, 기관 TEXT NOT NULL, 비용명 TEXT NOT NULL, 진행흐름 TEXT NOT NULL, " +
                          "월 INTEGER NOT NULL, 말일 INTEGER NOT NULL DEFAULT 0, 일 INTEGER NOT NULL DEFAULT 0, 알림영업일 INTEGER NOT NULL DEFAULT 3, " +
                          "금액규칙 TEXT NOT NULL DEFAULT '', 고정금액 TEXT, 비고 TEXT NOT NULL DEFAULT '', 순서 INTEGER NOT NULL DEFAULT 0)");
                    c.Run("CREATE TABLE attachments(rid INTEGER PRIMARY KEY AUTOINCREMENT, 연도 INTEGER NOT NULL, id TEXT NOT NULL, " +
                          "단계 TEXT NOT NULL DEFAULT '', 저장파일 TEXT NOT NULL, 원본파일명 TEXT NOT NULL DEFAULT '', 첨부일시 TEXT)");
                    c.Run("INSERT INTO meta VALUES('schema_version','1')");
                    string ins = "INSERT INTO items(id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고,순서) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)";
                    c.Run(ins, "gx-05", "협회", "회비", "납부만", 5, 0, 20, 3, "고지수령", null, "", 0);
                    c.Run(ins, "gx-06", "협회", "회비", "납부만", 6, 0, 20, 3, "고지수령", null, "", 1);
                    c.Run(ins, "fixed", "공사", "기여금", "납부만", 3, 1, 0, 3, "고정", "100000", "", 2);
                    c.Run(ins, "odd", "기관", "금액만 적힌 건", "납부만", 4, 0, 10, 3, "수작업", "2500", "", 3);
                    c.Run(ins, "sub", "기관", "제출", "제출만", 6, 1, 0, 3, "해당없음", null, "", 4);
                    c.Run(ins, "solo-07", "기관", "단독", "납부만", 7, 0, 1, 3, "수작업", null, "", 5);
                    c.Run("INSERT INTO attachments(연도,id,단계,저장파일,원본파일명,첨부일시) VALUES(2026,'gx-05','전표결재','a.pdf','a.pdf','2026-05-01 10:00:00')");
                    c.Run("INSERT INTO attachments(연도,id,단계,저장파일,원본파일명,첨부일시) VALUES(2026,'gx-05','납부완료','b.pdf','b.pdf','2026-05-02 10:00:00')");
                }

                using (Store s = Store.Open(p))
                {
                    Check("버전이 2 로", s.GetMeta("schema_version"), "2");
                    List<PaymentItem> m = s.LoadMaster();
                    Check("항목 수 그대로", m.Count, 6);
                    Func<string, PaymentItem> 찾기 = delegate(string id) { return m.Find(delegate(PaymentItem x) { return x.Id == id; }); };
                    Check("고지수령 → 변동", 찾기("gx-05").금액규칙, "변동");
                    Check("고정 그대로", 찾기("fixed").금액규칙, "고정");
                    Check("고정금액 보존", 찾기("fixed").고정금액, 100000m);
                    Check("금액이 적힌 수작업 → 고정 (금액을 버리지 않음)", 찾기("odd").금액규칙, "고정");
                    Check("그 금액도 보존", 찾기("odd").고정금액, 2500m);
                    Check("해당없음 → 변동", 찾기("sub").금액규칙, "변동");
                    Check("분할 회차는 한 묶음", 찾기("gx-05").묶음 + "|" + 찾기("gx-06").묶음, "gx|gx");
                    Check("회차가 하나뿐이면 묶음 없음", 찾기("solo-07").묶음, "");
                    Check("새 칸 기본값", 찾기("gx-05").홈페이지주소, "");

                    List<Attachment> atts = s.LoadAttachments();
                    Check("옛 단계 이름 전표결재 → 전표발행", atts[0].단계, "전표발행");
                    Check("옛 단계 이름 납부완료 → 납부", atts[1].단계, "납부");
                    Check("옛 첨부는 증빙 종류", atts[0].종류, Attachment.증빙);
                    Check("변경 기록 표가 비어 있음", s.LoadEvents(2026, "gx-05").Count, 0);
                }

                string[] backups = Directory.GetFiles(Path.Combine(dir, "backups"), "*이관전*.db");
                Check("이관 전 사본 1개", backups.Length, 1);
                using (Store b = Store.Open(backups[0]))
                    Check("사본을 열면 그것도 이관되지만 원래 항목이 다 있음", b.LoadMaster().Count, 6);

                using (Store s = Store.Open(p))
                    Check("다시 열어도 이관은 한 번만 (사본 수 그대로)", Directory.GetFiles(Path.Combine(dir, "backups"), "납부알림-v1*").Length, 1);

                // 이 프로그램보다 새 판 파일은 열지 않는다 — 모르는 칸을 망가뜨리지 않게.
                using (Store s = Store.Open(p)) s.SetMeta("schema_version", "99");
                bool 거절 = false;
                try { using (Store.Open(p)) { } }
                catch (InvalidOperationException) { 거절 = true; }
                CheckTrue("더 새 판 파일은 거절", 거절);
            }
            finally { 치우기(dir); }
        }

        static void 단계변경은원자적()
        {
            Console.WriteLine("\n[DB-16] 진행·대기·되돌리기는 확인과 쓰기가 한 번에, 기록이 남는다 (ADR-0004)");
            string dir = 임시폴더("atomic");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    DateTime 지금 = new DateTime(2026, 9, 16, 10, 30, 0);
                    DateTime 오늘 = new DateTime(2026, 9, 16);

                    StatusRecord a = s.Advance(2026, "k", Flow.납부만, 0, 지금, 오늘, "팝업");
                    Check("진행 → 단계 1", a.단계, 1);
                    Check("진행하면 오늘 확인", a.최종확인일.Value.ToString("yyyy-MM-dd"), "2026-09-16");
                    Check("DB 에도 1", s.LoadStatus(2026, "k").단계, 1);

                    bool 충돌 = false;
                    try { s.Advance(2026, "k", Flow.납부만, 0, 지금, 오늘, "웹"); }
                    catch (StageConflictException ce) { 충돌 = ce.현재단계 == 1; }
                    CheckTrue("옛 단계(0)를 보고 누르면 거절, 지금 단계를 알려 줌", 충돌);
                    Check("거절된 누름은 반영 안 됨", s.LoadStatus(2026, "k").단계, 1);

                    StatusRecord d = s.Defer(2026, "k", 1, 지금.AddHours(1), 오늘.AddDays(1), "웹");
                    Check("대기는 단계 그대로", d.단계, 1);
                    Check("대기는 확인일만", d.최종확인일.Value.ToString("yyyy-MM-dd"), "2026-09-17");

                    s.Advance(2026, "k", Flow.납부만, 1, 지금, 오늘, "웹");
                    bool 끝 = false;
                    try { s.Advance(2026, "k", Flow.납부만, -1, 지금, 오늘, "웹"); }
                    catch (StageConflictException) { 끝 = true; }
                    CheckTrue("마지막 단계 넘어 진행은 거절", 끝);

                    StatusRecord r = s.Revert(2026, "k", Flow.납부만, 2, 지금, "보드");
                    Check("되돌리면 1", r.단계, 1);
                    CheckTrue("되돌리면 확인일 지움", !r.최종확인일.HasValue);
                    s.Revert(2026, "k", Flow.납부만, 1, 지금, "보드");
                    bool 처음 = false;
                    try { s.Revert(2026, "k", Flow.납부만, 0, 지금, "보드"); }
                    catch (StageConflictException) { 처음 = true; }
                    CheckTrue("첫 단계에서 되돌리기는 거절", 처음);

                    List<StatusEvent> ev = s.LoadEvents(2026, "k");
                    Check("성공한 동작만 기록 (진행·대기·진행·되돌리기·되돌리기)", ev.Count, 5);
                    Check("최근 것이 먼저", ev[0].동작, "되돌리기");
                    Check("출처 기록", ev[0].출처, "보드");
                    Check("이전·이후 단계", ev[0].이전단계 + "→" + ev[0].이후단계, "1→0");
                    Check("첫 기록은 팝업의 진행", ev[4].동작 + "/" + ev[4].출처, "진행/팝업");
                    Check("진행 기록 내용은 도달 지점", ev[4].내용, "전표발행");

                    s.UpsertAmounts(new AmountRecord[] { 금액(2026, "k", 1234m, "고지서", "") }, "웹");
                    s.DeleteAmount(2026, "k", "웹");
                    s.AddEvent(2026, "k", "첨부", "웹", "영수증.pdf");
                    ev = s.LoadEvents(2026, "k");
                    Check("금액 입력·삭제·첨부도 기록", ev[2].동작 + "," + ev[1].동작 + "," + ev[0].동작, "금액,금액삭제,첨부");
                    CheckTrue("금액 기록에 금액이 적힘", ev[2].내용.Contains("1,234"));

                    Check("기간 조회", s.LoadEvents(DateTime.Today, DateTime.Today, 100).Count, 8);
                    Check("기간 밖은 없음", s.LoadEvents(new DateTime(2000, 1, 1), new DateTime(2000, 1, 2), 100).Count, 0);
                    Check("무결성 검사", s.무결성검사(), "ok");
                }
            }
            finally { 치우기(dir); }
        }

        static void 두창이동시에누름()
        {
            Console.WriteLine("\n[DB-17] 팝업과 웹이 같은 건을 거의 동시에 눌러도 한 번만 진행된다 (AC-W14)");
            string dir = 임시폴더("race");
            try
            {
                string p = Path.Combine(dir, "t.db");
                using (Store.Open(p)) { }
                int 성공 = 0, 거절 = 0;
                var threads = new List<System.Threading.Thread>();
                object 잠금 = new object();
                for (int i = 0; i < 6; i++)
                {
                    string 출처 = i % 2 == 0 ? "팝업" : "웹";
                    var t = new System.Threading.Thread(delegate()
                    {
                        using (Store s = Store.Open(p))
                        {
                            try
                            {
                                s.Advance(2026, "same", Flow.신고납부, 0, DateTime.Now, DateTime.Today, 출처);
                                lock (잠금) 성공++;
                            }
                            catch (StageConflictException) { lock (잠금) 거절++; }
                        }
                    });
                    threads.Add(t);
                }
                foreach (var t in threads) t.Start();
                foreach (var t in threads) t.Join();

                Check("여섯 번 중 한 번만 성공", 성공, 1);
                Check("나머지는 거절", 거절, 5);
                using (Store s = Store.Open(p))
                {
                    Check("단계는 1", s.LoadStatus(2026, "same").단계, 1);
                    Check("기록도 1건", s.LoadEvents(2026, "same").Count, 1);
                }
            }
            finally { 치우기(dir); }
        }

        static void 순서옮기기()
        {
            Console.WriteLine("\n[DB-18] 항목 순서 옮기기와 여러 건 한꺼번에 넣기");
            string dir = 임시폴더("order");
            try
            {
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    s.UpsertItems(new PaymentItem[] {
                        항목("a", "기관", "가", Flow.납부만, 1, false, 10, 3, "변동", null),
                        항목("b", "기관", "나", Flow.납부만, 2, false, 10, 3, "변동", null),
                        항목("c", "기관", "다", Flow.납부만, 3, false, 10, 3, "변동", null) });
                    Check("넣은 순서", string.Join(",", s.LoadMaster().ConvertAll(delegate(PaymentItem x) { return x.Id; }).ToArray()), "a,b,c");
                    CheckTrue("아래로", s.MoveItem("a", 1));
                    Check("a 가 둘째로", string.Join(",", s.LoadMaster().ConvertAll(delegate(PaymentItem x) { return x.Id; }).ToArray()), "b,a,c");
                    CheckTrue("맨 위에서 위로는 그대로", !s.MoveItem("b", -1));
                    CheckTrue("없는 항목은 그대로", !s.MoveItem("zz", 1));

                    PaymentItem v = 항목("v", "기관", "변동에 금액", Flow.납부만, 4, false, 10, 3, "변동", 999m);
                    v.금액규칙 = AmountRules.변동;
                    s.UpsertItem(v);
                    PaymentItem got = s.LoadMaster().Find(delegate(PaymentItem x) { return x.Id == "v"; });
                    Check("명시적 변동 + 금액은 고정으로 정규화 (금액 보존)", got.금액규칙, "고정");

                    PaymentItem sub = 항목("s", "기관", "제출", Flow.제출만, 4, false, 10, 3, "고정", 500m);
                    s.UpsertItem(sub);
                    got = s.LoadMaster().Find(delegate(PaymentItem x) { return x.Id == "s"; });
                    CheckTrue("제출만은 마스터 금액을 두지 않음", !got.고정금액.HasValue);

                    PaymentItem site = 항목("h", "국세청", "부가세", Flow.신고납부, 7, false, 25, 5, "변동", null);
                    site.홈페이지명 = "홈택스"; site.홈페이지주소 = "https://hometax.go.kr/"; site.묶음 = "vat";
                    s.UpsertItem(site);
                    got = s.LoadMaster().Find(delegate(PaymentItem x) { return x.Id == "h"; });
                    Check("홈페이지·묶음 왕복", got.홈페이지명 + "|" + got.홈페이지주소 + "|" + got.묶음, "홈택스|https://hometax.go.kr/|vat");
                }
            }
            finally { 치우기(dir); }
        }
    }
}
