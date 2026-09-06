using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    static class TestRunner
    {
        static int passed = 0;
        static int failed = 0;

        static void Check(string name, object actual, object expected)
        {
            string a = Convert.ToString(actual, CultureInfo.InvariantCulture);
            string e = Convert.ToString(expected, CultureInfo.InvariantCulture);
            if (a == e) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "\n          기대: " + e + "\n          실제: " + a); }
        }

        static void CheckTrue(string name, bool cond)
        {
            if (cond) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 검증용 합성 공휴일. 실제 공휴일 자료와 무관하게 알고리즘만 시험한다.
            var holidays = new List<DateTime> {
                new DateTime(2026,1,1),    // 목
                new DateTime(2026,3,2),    // 월 (삼일절 대체 가정)
                new DateTime(2026,3,25),   // 수 - 역산 경로에 일부러 끼워넣음
                new DateTime(2026,5,5),    // 화
                new DateTime(2027,1,1)
            };
            var cal = new BusinessDayCalendar(holidays, new int[] { 2025, 2026, 2027 });

            Console.WriteLine("\n[1] 영업일 판정");
            Check("2026-03-28 토요일은 영업일 아님", cal.IsBusinessDay(new DateTime(2026,3,28)), false);
            Check("2026-03-29 일요일은 영업일 아님", cal.IsBusinessDay(new DateTime(2026,3,29)), false);
            Check("2026-03-31 화요일은 영업일",      cal.IsBusinessDay(new DateTime(2026,3,31)), true);
            Check("2026-01-01 공휴일은 영업일 아님",  cal.IsBusinessDay(new DateTime(2026,1,1)), false);

            Console.WriteLine("\n[2] 기한 보정 (주말/공휴일이면 다음 영업일)");
            Check("2026-01-25(일) -> 01-26(월)",
                cal.NextBusinessDayOrSame(new DateTime(2026,1,25)).ToString("yyyy-MM-dd"), "2026-01-26");
            Check("2026-03-31(화)는 그대로",
                cal.NextBusinessDayOrSame(new DateTime(2026,3,31)).ToString("yyyy-MM-dd"), "2026-03-31");
            Check("2026-01-01(공휴일,목) -> 01-02(금)",
                cal.NextBusinessDayOrSame(new DateTime(2026,1,1)).ToString("yyyy-MM-dd"), "2026-01-02");
            Check("2026-05-31(일) -> 06-01(월)",
                cal.NextBusinessDayOrSame(new DateTime(2026,5,31)).ToString("yyyy-MM-dd"), "2026-06-01");

            Console.WriteLine("\n[3] N영업일 역산");
            // 3/31(화) 기준: 3/30 월, 3/27 금, 3/26 목  => 3영업일 전 = 3/26
            Check("3/31에서 3영업일 전 = 3/26",
                cal.SubtractBusinessDays(new DateTime(2026,3,31), 3).ToString("yyyy-MM-dd"), "2026-03-26");
            // 5영업일 전: 3/30, 3/27, 3/26, (3/25는 공휴일 건너뜀), 3/24, 3/23 => 3/23
            Check("3/31에서 5영업일 전 = 3/23 (3/25 공휴일 건너뜀)",
                cal.SubtractBusinessDays(new DateTime(2026,3,31), 5).ToString("yyyy-MM-dd"), "2026-03-23");

            Console.WriteLine("\n[4] 말일(EOM) 처리 - 윤년");
            var eom = new PaymentItem();
            eom.월 = 2; eom.말일 = true;
            Check("2026년 2월 말일 = 02-28", eom.원기한일(2026).ToString("yyyy-MM-dd"), "2026-02-28");
            Check("2027년 2월 말일 = 02-28", eom.원기한일(2027).ToString("yyyy-MM-dd"), "2027-02-28");
            Check("2028년 2월 말일 = 02-29 (윤년)", eom.원기한일(2028).ToString("yyyy-MM-dd"), "2028-02-29");

            Console.WriteLine("\n[5] 진행흐름 단계 정의");
            Check("신고납부 단계 수", Stages.For(Flow.신고납부).Length, 4);
            Check("납부만 단계 수",   Stages.For(Flow.납부만).Length, 3);
            Check("제출만 단계 수",   Stages.For(Flow.제출만).Length, 2);
            Check("신고납부 마지막", Stages.For(Flow.신고납부)[3], "납부완료");
            Check("납부만 첫 단계",   Stages.For(Flow.납부만)[0], "납부 전");

            Console.WriteLine("\n[6] 마스터 파일 적재");
            // 사내 자료(data/payment-master.tsv)가 아니라 저장소에 포함된 픽스처를 쓴다.
            // 클론 직후에도 테스트가 돌아야 하고, 실제 납부 일정이 저장소에 남으면 안 된다.
            string masterPath = "test\\fixtures\\master-fixture.tsv";
            if (!File.Exists(masterPath))
                masterPath = Path.Combine("..", "test", "fixtures", "master-fixture.tsv");

            var warnings = new List<string>();
            List<PaymentItem> master = Repository.LoadMaster(masterPath, warnings);
            Check("픽스처 항목 수 = 7", master.Count, 7);
            Check("적재 경고 없음", warnings.Count, 0);

            int 신고납부 = 0, 납부만 = 0, 제출만 = 0, lead5 = 0, lead7 = 0, lead3 = 0;
            foreach (PaymentItem it in master)
            {
                if (it.진행흐름 == Flow.신고납부) 신고납부++;
                if (it.진행흐름 == Flow.납부만) 납부만++;
                if (it.진행흐름 == Flow.제출만) 제출만++;
                if (it.알림영업일 == 5) lead5++;
                if (it.알림영업일 == 7) lead7++;
                if (it.알림영업일 == 3) lead3++;
            }
            Check("신고납부 2건", 신고납부, 2);
            Check("납부만 3건", 납부만, 3);
            Check("제출만 2건", 제출만, 2);
            Check("5영업일 지정 1건", lead5, 1);
            Check("7영업일 지정 1건", lead7, 1);
            Check("3영업일(공란 기본값 포함) 5건", lead3, 5);

            Console.WriteLine("\n[7] AC-17: 잘못된 알림영업일은 기본 3으로 대체");
            string tmp = Path.Combine(Path.GetTempPath(), "pa_bad_master.tsv");
            File.WriteAllText(tmp,
                "id\t기관\t비용명\t진행흐름\t월\t일\t알림영업일\t금액규칙\t고정금액\t비고\r\n" +
                "bad1\t기관\t비용\t납부만\t3\tEOM\t\t고정\t1000\t\r\n" +      // 빈 값
                "bad2\t기관\t비용\t납부만\t4\t25\tabc\t고정\t1000\t\r\n" +     // 문자
                "bad3\t기관\t비용\t납부만\t5\t20\t-1\t고정\t1000\t\r\n",       // 음수
                new System.Text.UTF8Encoding(true));
            var w2 = new List<string>();
            List<PaymentItem> bad = Repository.LoadMaster(tmp, w2);
            Check("3건 모두 적재됨 (중단 없음)", bad.Count, 3);
            Check("빈 값 -> 3", bad[0].알림영업일, 3);
            Check("문자 -> 3", bad[1].알림영업일, 3);
            Check("음수 -> 3", bad[2].알림영업일, 3);
            File.Delete(tmp);

            Console.WriteLine("\n[8] 2026년 전체 일정 (눈으로 검산)");
            Console.WriteLine("  {0,-14} {1,-22} {2,-12} {3,-12} {4,-12} {5}",
                "id", "비용명", "원기한", "보정기한", "알림일", "흐름/영업일");
            Console.WriteLine("  " + new string('-', 96));

            var occs = Scheduler.BuildOccurrences(master, cal, new DateTime(2026, 6, 15));
            var y2026 = new List<Occurrence>();
            foreach (Occurrence o in occs) if (o.연도 == 2026) y2026.Add(o);
            y2026.Sort(delegate(Occurrence a, Occurrence b) { return a.알림일.CompareTo(b.알림일); });

            foreach (Occurrence o in y2026)
            {
                Console.WriteLine("  {0,-14} {1,-22} {2,-12} {3,-12} {4,-12} {5}/{6}일",
                    o.Item.Id, o.Item.비용명,
                    o.원기한일.ToString("MM-dd (ddd)", new CultureInfo("ko-KR")),
                    o.보정기한일.ToString("MM-dd (ddd)", new CultureInfo("ko-KR")),
                    o.알림일.ToString("MM-dd (ddd)", new CultureInfo("ko-KR")),
                    o.Item.진행흐름, o.Item.알림영업일);
            }
            Check("2026년 이벤트 7건", y2026.Count, 7);

            Console.WriteLine("\n[9] 모든 알림일이 보정기한일보다 앞선다");
            bool ordered = true;
            foreach (Occurrence o in y2026)
                if (o.알림일 >= o.보정기한일) { ordered = false; Console.WriteLine("    위반: " + o.Item.Id); }
            CheckTrue("알림일 < 보정기한일", ordered);

            Console.WriteLine("\n[10] 알림일부터 기한까지의 영업일 수가 설정값과 일치");
            bool leadOk = true;
            foreach (Occurrence o in y2026)
            {
                int actual = cal.BusinessDaysBetween(o.알림일, o.보정기한일);
                if (actual != o.Item.알림영업일)
                {
                    leadOk = false;
                    Console.WriteLine(string.Format("    불일치: {0} 설정={1} 실제={2}",
                        o.Item.Id, o.Item.알림영업일, actual));
                }
            }
            CheckTrue("역산 결과가 설정 영업일과 일치", leadOk);

            Console.WriteLine("\n[11] 표시 대상 선정");
            var statusMap = new Dictionary<string, StatusRecord>();
            // fx-03: 5/20(수) 기한, 3영업일 전 = 5/15(금). 그 사이 날짜면 표시 대상이어야 한다.
            DateTime probe = new DateTime(2026, 5, 18);
            var rows = Scheduler.BuildRows(occs, statusMap, cal, probe).Rows;
            bool found = false;
            foreach (AlertRow r in rows) if (r.Occ.Item.Id == "fx-03" && r.Occ.연도 == 2026) found = true;
            CheckTrue("알림 기간에 든 건이 표시 대상", found);

            // 최종 단계면 제외된다
            var st = new StatusRecord();
            st.연도 = 2026; st.Id = "fx-03"; st.단계 = 2;   // 납부만 흐름의 마지막
            statusMap[st.Key] = st;
            var rows2 = Scheduler.BuildRows(occs, statusMap, cal, probe).Rows;
            bool still = false;
            foreach (AlertRow r in rows2) if (r.Occ.Item.Id == "fx-03" && r.Occ.연도 == 2026) still = true;
            CheckTrue("최종 단계 도달 건은 제외됨 (AC-24)", !still);

            // AC-24b: 오늘 이미 확인한 건은 제외
            var st2 = new StatusRecord();
            st2.연도 = 2026; st2.Id = "fx-03"; st2.단계 = 1;
            st2.최종확인일 = probe;
            statusMap[st2.Key] = st2;
            var rows3 = Scheduler.BuildRows(occs, statusMap, cal, probe).Rows;
            bool again = false;
            foreach (AlertRow r in rows3) if (r.Occ.Item.Id == "fx-03" && r.Occ.연도 == 2026) again = true;
            CheckTrue("오늘 확인한 건은 재표시 안 함 (AC-24b)", !again);

            // 회귀 방지: 예전에는 알림일만 보고 필터링해서 작년치까지 27건이 한꺼번에 떴다.
            // 전부 처리해야 닫히는 구조라 첫 실행에서 창을 닫을 수 없었다.
            Console.WriteLine("\n[11b] 기한 유예 규칙 (회귀 테스트)");
            var freshMap = new Dictionary<string, StatusRecord>();
            RowSet set326 = Scheduler.BuildRows(occs, freshMap, cal, new DateTime(2026, 3, 26));
            Console.WriteLine(string.Format("    3/26 강제표시 {0}건 / 기한초과 요약 {1}건",
                set326.Rows.Count, set326.Overdue.Count));
            foreach (AlertRow r in set326.Rows)
                Console.WriteLine("      - " + r.Occ.Item.Id + " 기한 " + r.Occ.보정기한일.ToString("MM-dd"));

            // 핵심은 "작년치까지 전부 강제표시되지 않는다" 는 성질이다.
            // 픽스처가 바뀌어도 유효하도록 건수 대신 성질로 검증한다.
            CheckTrue("강제표시가 전체 발생건보다 훨씬 적음",
                set326.Rows.Count < set326.Overdue.Count);
            CheckTrue("기한초과 건이 요약으로 분리됨", set326.Overdue.Count > 0);

            bool 미래기한만 = true;
            foreach (AlertRow r in set326.Rows)
                if (r.Occ.보정기한일 < new DateTime(2026, 3, 26)) 미래기한만 = false;
            CheckTrue("강제표시 건은 기한이 아직 남아 있음", 미래기한만);

            bool 작년없음 = true;
            foreach (AlertRow r in set326.Rows)
                if (r.Occ.연도 != 2026) 작년없음 = false;
            CheckTrue("강제표시에 작년 건이 섞이지 않음", 작년없음);

            Console.WriteLine("\n[11c] 진행 상태 저장·복원 왕복");
            // 저장이 깨지면 사용자가 누른 내용이 통째로 사라진다. 반드시 왕복 확인.
            string stPath = Path.Combine(Path.GetTempPath(), "pa_status_rt.tsv");
            if (File.Exists(stPath)) File.Delete(stPath);

            const string K1 = "2026\tfx-03";   // 납부만: 납부 전 / 전표결재 / 납부완료
            const string K2 = "2026\tfx-05";   // 제출만: 제출 전 / 제출완료

            var save = new List<StatusRecord>();
            var s1 = new StatusRecord();
            s1.연도 = 2026; s1.Id = "fx-03"; s1.단계 = 1;   // 전표결재
            s1.변경일시 = new DateTime(2026, 4, 17, 9, 12, 0);
            s1.최종확인일 = new DateTime(2026, 4, 20);
            s1.메모 = "결재 상신, 승인 대기";
            s1.변경됨 = true;      // 사용자가 실제로 바꾼 기록만 저장된다
            save.Add(s1);

            var s2 = new StatusRecord();
            s2.연도 = 2026; s2.Id = "fx-05"; s2.단계 = 1;   // 제출완료
            s2.변경일시 = new DateTime(2026, 2, 24, 10, 41, 0);
            s2.최종확인일 = new DateTime(2026, 2, 24);
            s2.변경됨 = true;
            save.Add(s2);

            Repository.SaveStatus(stPath, save, master);
            CheckTrue("상태 파일이 생성됨", File.Exists(stPath));

            var loaded = Repository.LoadStatus(stPath);
            Check("복원된 건수", loaded.Count, 2);
            CheckTrue("첫 번째 건이 복원됨", loaded.ContainsKey(K1));
            CheckTrue("두 번째 건이 복원됨", loaded.ContainsKey(K2));

            StatusRecord r1 = loaded[K1];
            Check("단계 보존", r1.단계, 1);
            Check("변경일시 보존", r1.변경일시.Value.ToString("yyyy-MM-dd HH:mm"), "2026-04-17 09:12");
            Check("최종확인일 보존", r1.최종확인일.Value.ToString("yyyy-MM-dd"), "2026-04-20");
            Check("한글 메모 보존", r1.메모, "결재 상신, 승인 대기");

            StatusRecord r2 = loaded[K2];
            Check("두 번째 건 단계 보존", r2.단계, 1);

            // 사람이 읽을 수 있도록 단계명이 함께 기록되는지
            string body = File.ReadAllText(stPath, System.Text.Encoding.UTF8);
            CheckTrue("납부만 흐름의 단계명이 기록됨", body.Contains("전표결재"));
            CheckTrue("제출만 흐름의 단계명이 기록됨", body.Contains("제출완료"));
            CheckTrue("UTF-8 BOM 으로 저장됨", File.ReadAllBytes(stPath)[0] == 0xEF);

            // 덮어쓰기 후에도 정상인지
            s1.단계 = 2;   // 납부완료
            s1.변경됨 = true;
            Repository.SaveStatus(stPath, save, master);
            var loaded2 = Repository.LoadStatus(stPath);
            Check("덮어쓰기 후 단계 갱신", loaded2[K1].단계, 2);
            Check("덮어쓰기 후 건수 유지", loaded2.Count, 2);

            // 손대지 않은 기록은 파일에 남기지 않는다.
            // 단계 0 짜리 빈 행이 쌓이는 것을 막고, 동시 실행 시 덮어쓰기도 막는다.
            var 손안댐 = new StatusRecord();
            손안댐.연도 = 2026; 손안댐.Id = "fx-07"; 손안댐.단계 = 0;   // 변경됨 = false
            var 섞음 = new List<StatusRecord>(save);
            섞음.Add(손안댐);
            Repository.SaveStatus(stPath, 섞음, master);
            var loaded3 = Repository.LoadStatus(stPath);
            CheckTrue("손대지 않은 기록은 저장 안 됨", !loaded3.ContainsKey("2026	fx-07"));
            Check("기존 기록은 그대로", loaded3.Count, 2);

            File.Delete(stPath);

            Console.WriteLine("\n[11d] 연도별 실제 금액");
            string amPath = Path.Combine(Path.GetTempPath(), "pa_amounts.tsv");
            File.WriteAllText(amPath,
                "연도\tid\t금액\t출처\t확인일\t비고\r\n" +
                "2026\tfx-03\t5838089\t고지서\t2026-05-13\t1차\r\n" +
                "2026\tfx-04\t1,840,245\t통보문\t2026-03-11\t쉼표 있는 금액\r\n" +
                "2027\tfx-03\t6000000\t고지서\t2027-05-12\t다음 해는 금액이 다르다\r\n" +
                "2026\tfx-05\t안내문참조\t\t\t숫자가 아닌 값\r\n",
                new System.Text.UTF8Encoding(true));

            var amWarn = new List<string>();
            var amounts = Repository.LoadAmounts(amPath, amWarn);
            Check("유효한 금액만 적재", amounts.Count, 3);
            Check("숫자가 아닌 행은 경고", amWarn.Count, 1);
            Check("금액 보존", amounts["2026\tfx-03"].금액, 5838089);
            Check("쉼표 있는 금액 파싱", amounts["2026\tfx-04"].금액, 1840245);
            Check("출처 보존", amounts["2026\tfx-03"].출처, "고지서");

            // 같은 항목이라도 해가 바뀌면 다른 금액이어야 한다.
            // 마스터의 고정금액에 넣었다면 2027년에 2026년 금액을 보여주게 된다.
            Check("연도별로 다른 금액", amounts["2027\tfx-03"].금액, 6000000);

            var occAm = Scheduler.BuildOccurrences(master, cal, new DateTime(2026, 6, 15), amounts);
            Occurrence o26 = null, o27 = null, oNone = null;
            foreach (Occurrence o in occAm)
            {
                if (o.Item.Id == "fx-03" && o.연도 == 2026) o26 = o;
                if (o.Item.Id == "fx-03" && o.연도 == 2027) o27 = o;
                if (o.Item.Id == "fx-01" && o.연도 == 2026) oNone = o;
            }
            CheckTrue("2026 발생건에 금액이 붙음", o26 != null && o26.실제금액 != null);
            Check("2026 금액", o26.실제금액.금액, 5838089);
            Check("2027 금액은 별개", o27.실제금액.금액, 6000000);
            CheckTrue("금액 자료가 없는 건은 null", oNone != null && oNone.실제금액 == null);

            // 금액 파일이 아예 없어도 알림은 멈추면 안 된다
            var noAm = Repository.LoadAmounts(Path.Combine(Path.GetTempPath(), "pa_no_such.tsv"), null);
            Check("파일이 없으면 빈 사전", noAm.Count, 0);
            File.Delete(amPath);

            Console.WriteLine("\n[11c-2] 동시 실행 시 서로의 변경을 지우지 않는다");
            // 팝업이 열려 있는 동안 보드에서 되돌리면, 팝업이 닫힐 때
            // 옛 메모리 내용으로 덮어써서 되돌린 것이 사라지는 문제가 있었다.
            string cPath = Path.Combine(Path.GetTempPath(), "pa_concurrent.tsv");
            if (File.Exists(cPath)) File.Delete(cPath);

            // 팝업이 시작할 때 읽은 상태 (fx-03 = 전표결재 1, fx-04 = 납부 전 0)
            var 팝업메모리 = new List<StatusRecord>();
            var pa = new StatusRecord();
            pa.연도 = 2026; pa.Id = "fx-03"; pa.단계 = 1; pa.변경됨 = true;
            팝업메모리.Add(pa);
            var pb = new StatusRecord();
            pb.연도 = 2026; pb.Id = "fx-04"; pb.단계 = 0;   // 손대지 않음
            팝업메모리.Add(pb);
            Repository.SaveStatus(cPath, 팝업메모리, master);

            // 보드가 다른 건(fx-05)을 바꾼다
            var 보드변경 = new StatusRecord();
            보드변경.연도 = 2026; 보드변경.Id = "fx-05"; 보드변경.단계 = 1; 보드변경.변경됨 = true;
            Repository.SaveStatus(cPath, new StatusRecord[] { 보드변경 }, master);

            // 팝업이 닫히며 자기 메모리를 저장한다 (fx-05 는 모르는 상태)
            Repository.SaveStatus(cPath, 팝업메모리, master);

            var 최종 = Repository.LoadStatus(cPath);
            CheckTrue("보드가 바꾼 건이 살아남음", 최종.ContainsKey("2026\tfx-05"));
            Check("보드 변경값 유지", 최종["2026\tfx-05"].단계, 1);
            Check("팝업 변경값도 유지", 최종["2026\tfx-03"].단계, 1);
            CheckTrue("손대지 않은 기본값은 기록되지 않음", !최종.ContainsKey("2026\tfx-04"));

            // 되돌리기: 파일을 다시 읽어 바꾼 뒤 그것만 저장
            var 다시읽음 = Repository.LoadStatus(cPath);
            StatusRecord 되돌릴것 = 다시읽음["2026\tfx-03"];
            되돌릴것.단계 = 0;
            되돌릴것.변경됨 = true;
            Repository.SaveStatus(cPath, new StatusRecord[] { 되돌릴것 }, master);

            var 되돌린뒤 = Repository.LoadStatus(cPath);
            Check("되돌리기 반영", 되돌린뒤["2026\tfx-03"].단계, 0);
            Check("다른 건은 그대로", 되돌린뒤["2026\tfx-05"].단계, 1);
            File.Delete(cPath);

            Console.WriteLine("\n[11e] 증빙 첨부 보관");
            string attRoot = Path.Combine(Path.GetTempPath(), "pa_att_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string attIndex = Path.Combine(attRoot, "attachments.tsv");
            Directory.CreateDirectory(attRoot);

            // 첨부할 원본 두 개를 만든다
            string src1 = Path.Combine(attRoot, "신고서 원본.pdf");
            string src2 = Path.Combine(attRoot, "영수증.png");
            File.WriteAllText(src1, "신고서 내용", new System.Text.UTF8Encoding(true));
            File.WriteAllText(src2, "영수증 내용", new System.Text.UTF8Encoding(true));

            var store = new AttachmentStore(attIndex, attRoot);
            store.Load();
            Check("처음엔 증빙 없음", store.CountFor(2026, "fx-03"), 0);

            Attachment a1 = store.Attach(2026, "fx-03", "전표결재", src1);
            Attachment a2 = store.Attach(2026, "fx-03", "납부완료", src2);
            Check("2건 첨부됨", store.CountFor(2026, "fx-03"), 2);
            Check("다른 건에는 영향 없음", store.CountFor(2026, "fx-04"), 0);
            Check("연도가 다르면 별개", store.CountFor(2027, "fx-03"), 0);

            // 원본이 사라져도 증빙은 남아야 한다
            CheckTrue("복사본이 존재", File.Exists(store.FullPath(a1)));
            File.Delete(src1);
            CheckTrue("원본을 지워도 복사본 유지", File.Exists(store.FullPath(a1)));

            Check("단계가 기록됨", a1.단계, "전표결재");
            Check("원본 파일명 보존", a1.원본파일명, "신고서 원본.pdf");

            // 다시 읽어도 그대로여야 한다
            var store2 = new AttachmentStore(attIndex, attRoot);
            store2.Load();
            Check("재적재 후에도 2건", store2.CountFor(2026, "fx-03"), 2);
            var loadedAtts = store2.For(2026, "fx-03");
            Check("첨부 순서 유지 (첫 건)", loadedAtts[0].단계, "전표결재");
            Check("한글 파일명 보존", loadedAtts[0].원본파일명, "신고서 원본.pdf");

            // 삭제
            store2.Remove(loadedAtts[0]);
            Check("삭제 후 1건", store2.CountFor(2026, "fx-03"), 1);

            var store3 = new AttachmentStore(attIndex, attRoot);
            store3.Load();
            Check("삭제가 파일에도 반영", store3.CountFor(2026, "fx-03"), 1);

            // 같은 파일을 두 번 붙여도 덮어쓰지 않는다
            store3.Attach(2026, "fx-04", "납부완료", src2);
            store3.Attach(2026, "fx-04", "납부완료", src2);
            Check("같은 파일 두 번 첨부 시 2건", store3.CountFor(2026, "fx-04"), 2);
            var dup = store3.For(2026, "fx-04");
            CheckTrue("서로 다른 파일로 저장됨", dup[0].저장파일 != dup[1].저장파일);
            CheckTrue("둘 다 실재", File.Exists(store3.FullPath(dup[0])) && File.Exists(store3.FullPath(dup[1])));

            try { Directory.Delete(attRoot, true); } catch { }

            Console.WriteLine("\n[12] 공휴일 자료 없는 연도는 안전 여유 적용");
            var calNoYear = new BusinessDayCalendar(new DateTime[0], new int[] { 2025 });
            var occs2 = Scheduler.BuildOccurrences(master, calNoYear, new DateTime(2026, 6, 15));
            bool flagged = true;
            foreach (Occurrence o in occs2)
                if (o.연도 == 2026 && !o.공휴일자료없음) flagged = false;
            CheckTrue("2026 자료 없음 표시됨", flagged);

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine(string.Format("  통과 {0}건 / 실패 {1}건", passed, failed));
            Console.WriteLine(new string('=', 60));
            return failed == 0 ? 0 : 1;
        }
    }
}
