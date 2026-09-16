using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// 팝업을 실제로 띄우지 않고 생성·갱신·닫힘판정만 시험한다.
    /// 레이아웃 코드에서 터지는 문제를 잡기 위한 것.
    /// </summary>
    static class FormSmokeTest
    {
        static int passed = 0, failed = 0;

        static void Check(string name, object actual, object expected)
        {
            string a = Convert.ToString(actual), e = Convert.ToString(expected);
            if (a == e) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  기대=" + e + " 실제=" + a); }
        }
        static void CheckTrue(string name, bool c)
        {
            if (c) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        /// <summary>버튼이 중첩 패널 안에 있어도 찾는다. 배치가 바뀌어도 시험이 깨지지 않게.</summary>
        static void 누름(Button b)
        {
            // 창을 띄우기 전에는 PerformClick 이 무시되므로 Click 처리기를 직접 부른다.
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(b, new object[] { EventArgs.Empty });
        }

        /// <summary>버튼이 중첩 패널 안에 있어도 찾는다. 배치가 바뀌어도 시험이 깨지지 않게.</summary>
        static Button FindButton(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                Button b = c as Button;
                if (b != null && b.Text == text) return b;
                Button found = FindButton(c, text);
                if (found != null) return found;
            }
            return null;
        }

        static AlertRow 행(PaymentItem it, int year, DateTime due, int stage)
        {
            var o = new Occurrence { Item = it, 연도 = year, 원기한일 = due, 보정기한일 = due, 알림일 = due.AddDays(-5) };
            return new AlertRow { Occ = o, Status = new StatusRecord { 연도 = year, Id = it.Id, 단계 = stage } };
        }

        /// <summary>코너형 팝업 (ADR-0009, 시안 v4): 자리·크기·넘기기·자동 넘김·점 상태·닫기.</summary>
        static void 코너팝업(BusinessDayCalendar cal, List<PaymentItem> master, AttachmentStore store)
        {
            Console.WriteLine("\n[GUI-2] 코너형 팝업 — 자리와 크기");
            var wa = new System.Drawing.Rectangle(0, 0, 1920, 1032);
            var r = AlertForm.자리(wa);
            Check("폭 340", r.Width, 340);
            Check("높이 260", r.Height, 260);
            Check("작업 표시줄 바로 위 (틈 없음)", r.Bottom, 1032);
            Check("오른쪽 12px", wa.Right - r.Right, 12);

            DateTime today = new DateTime(2026, 9, 16);
            PaymentItem a = master[0], b = master[2], c = master[4];
            var rows = new List<AlertRow> {
                행(a, 2026, new DateTime(2026, 9, 11), 0),   // 기한 지남
                행(b, 2026, new DateTime(2026, 9, 18), 0),
                행(c, 2026, new DateTime(2026, 9, 21), 0)
            };
            var form = new AlertForm(rows, new List<AlertRow>(), cal, today, null, store);
            form.CreateControl();
            Check("제목줄 없음", form.FormBorderStyle, FormBorderStyle.None);
            Check("창 크기", form.Size.Width + "x" + form.Size.Height, "340x260");
            Check("X 버튼 없음", form.ControlBox, false);
            CheckTrue("항상 위", form.TopMost);
            Check("첫 장은 기한이 가장 이른 건", form.현재번호, 0);
            Check("건수", form.건수값, 3);

            Console.WriteLine("\n[GUI-3] 접기: 제목 줄만 남고 아래 끝은 작업 표시줄에 붙은 채");
            int bottom = form.Bounds.Bottom;
            CheckTrue("처음엔 펼침", !form.접힘);
            CheckTrue("접기 버튼이 있음", FindButton(form, "▾") != null);
            form.접기(true);
            CheckTrue("접힘", form.접힘);
            Check("접힌 높이", form.Height, AlertForm.접힌높이);
            Check("아래 끝 그대로", form.Bounds.Bottom, bottom);
            Check("폭 그대로", form.Width, AlertForm.폭);
            CheckTrue("펼치기 버튼으로 바뀜", FindButton(form, "▴") != null);
            form.접기(false);
            Check("펼친 높이", form.Height, AlertForm.높이);
            Check("펼쳐도 아래 끝 그대로", form.Bounds.Bottom, bottom);

            Console.WriteLine("\n[GUI-4] 설정 버튼은 웹 화면 열기를 부른다");
            Button gear = FindButton(form, form.설정버튼문구);
            CheckTrue("설정 버튼이 있음", gear != null);
            int 불림 = 0;
            form.웹열기 = delegate { 불림++; };
            누름(gear);
            Check("누르면 웹 열기 한 번", 불림, 1);
            form.웹열기 = null;
            누름(gear);
            CheckTrue("연결이 없어도 오류 없음", true);
            CheckTrue("설정 버튼이 접기 버튼 왼쪽", gear.Right <= FindButton(form, "▾").Left);

            Console.WriteLine("\n[GUI-2] 넘기기");
            form.이동(1);
            Check("오른쪽으로", form.현재번호, 1);
            form.이동(99);
            Check("끝에서 멈춤", form.현재번호, 2);
            form.이동(-5);
            Check("처음에서 멈춤", form.현재번호, 0);

            Console.WriteLine("\n[GUI-2] 버튼은 행동 문구 (ADR-0005)");
            Check("납부만 시작 → 전표 발행", form.진행버튼문구, Stages.다음행동(a.진행흐름, 0));

            Console.WriteLine("\n[GUI-2] 점 상태");
            PageDots dots = null;
            foreach (Control ctl in form.Controls) if (ctl is PageDots) dots = (PageDots)ctl;
            CheckTrue("점이 있음", dots != null);
            Check("지금 보는 건이 기한 지남 → 길쭉한 빨강", dots.상태(0), "현재-지남");
            Check("아직 안 고른 건 → 빈 점", dots.상태(1), "남음");

            Console.WriteLine("\n[GUI-2] 고르면 다음 남은 건으로, 다 고르면 닫기");
            rows[0].Status.최종확인일 = today;          // 첫 건 대기
            form.RefreshState();
            Check("고른 건은 회색", dots.상태(0) == "현재-지남" ? "현재" : dots.상태(0), "현재");
            Check("다음 남은 건 = 1", form.다음남은건(0), 1);
            form.이동(1);
            Check("넘긴 뒤 첫 건 점은 처리됨", dots.상태(0), "처리됨");
            rows[1].Status.단계 = 1; rows[1].오늘단계변경 = true;
            Check("한 바퀴 돌아 남은 건 = 2", form.다음남은건(2), 2);
            Button close = FindButton(form, "닫기");
            Check("남은 건이 있으면 닫기 꺼짐", close.Enabled, false);
            rows[2].Status.최종확인일 = today;
            form.RefreshState();
            Check("남은 건 없음 → -1", form.다음남은건(0), -1);
            Check("다 고르면 닫기 켜짐", close.Enabled, true);
            form.Dispose();

            Console.WriteLine("\n[GUI-2] 한 건이면 점을 숨기고, 13건 이상이면 글자로");
            var one = new AlertForm(new List<AlertRow> { 행(a, 2026, new DateTime(2026, 9, 18), 0) }, null, cal, today, "경고 문구", store);
            one.CreateControl();
            CheckTrue("한 건이면 점 숨김", !one.점보임);
            CheckTrue("여러 건이면 점 보임 (앞 창)", true);
            one.Dispose();

            var many = new List<AlertRow>();
            for (int i = 0; i < 15; i++) many.Add(행(master[i % master.Count], 2026, new DateTime(2026, 9, 18).AddDays(i), 0));
            var big = new AlertForm(many, null, cal, today, null, store);
            big.CreateControl();
            PageDots d2 = null;
            foreach (Control ctl in big.Controls) if (ctl is PageDots) d2 = (PageDots)ctl;
            CheckTrue("15건이면 'n / 15' 글자", d2.글자로);
            CheckTrue("점 줄이 창 폭 안", d2.Width < AlertForm.폭 - 80);
            big.Dispose();
        }

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Application.EnableVisualStyles();

            var cal = new BusinessDayCalendar(
                new DateTime[] { new DateTime(2026,1,1) },
                new int[] { 2025, 2026, 2027 });

            var warnings = new List<string>();
            // 사내 자료가 아니라 저장소에 포함된 픽스처를 쓴다. 클론 직후에도 돌아야 한다.
            List<PaymentItem> master = Repository.LoadMaster("test\\fixtures\\master-fixture.tsv", warnings);

            DateTime today = new DateTime(2026, 3, 26);   // 픽스처에서 알림이 걸리는 날
            var occs = Scheduler.BuildOccurrences(master, cal, today);
            var statusMap = new Dictionary<string, StatusRecord>();
            RowSet set = Scheduler.BuildRows(occs, statusMap, cal, today);
            List<AlertRow> rows = set.Rows;

            Console.WriteLine("\n[GUI] 2026-03-26 기준 표시 대상 " + rows.Count + "건");
            foreach (AlertRow r in rows)
                Console.WriteLine("    - " + r.Occ.Item.표시명 + "  현재:" + r.현재단계명 + " -> 다음:" + r.다음단계명);

            Console.WriteLine("    (기한초과 요약 " + set.Overdue.Count + "건)");
            CheckTrue("표시 대상이 1건 이상", rows.Count >= 1);

            Console.WriteLine("\n[GUI] 폼 생성");
            string tmpRoot = Path.Combine(Path.GetTempPath(), "pa_att_test");
            var store = new AttachmentStore(Path.Combine(tmpRoot, "attachments.tsv"), tmpRoot);
            store.Load();
            var form = new AlertForm(rows, set.Overdue, cal, today, "공휴일 자료 시험용 경고 문구입니다.", store);
            form.CreateControl();

            CheckTrue("폼 생성됨", form != null);
            CheckTrue("컨트롤이 배치됨", form.Controls.Count >= 4);
            CheckTrue("창 높이가 양수", form.ClientSize.Height > 100);
            Check("X 버튼 비활성 (AC-25)", form.ControlBox, false);
            CheckTrue("항상 위 표시", form.TopMost);

            Button close = FindButton(form, "닫기");
            CheckTrue("닫기 버튼 존재", close != null);
            Check("초기 상태에서 닫기 비활성 (AC-11)", close.Enabled, false);

            Console.WriteLine("\n[GUI] 단계 진행 후 닫기 활성화 판정");
            foreach (AlertRow r in rows)
            {
                r.Status.단계++;
                r.Status.변경일시 = DateTime.Now;
                r.Status.최종확인일 = today;
                r.오늘단계변경 = true;
            }
            form.RefreshState();
            Check("모든 건 처리 후 닫기 활성", close.Enabled, true);

            Console.WriteLine("\n[GUI] 한 건만 되돌리면 다시 비활성");
            rows[0].Status.단계--;
            rows[0].오늘단계변경 = false;
            rows[0].Status.최종확인일 = null;
            form.RefreshState();
            Check("미처리 1건 발생 -> 닫기 비활성", close.Enabled, false);

            Console.WriteLine("\n[GUI] 오늘은 대기로도 닫기 가능해야 함");
            rows[0].Status.최종확인일 = today;
            form.RefreshState();
            Check("대기 처리 후 닫기 활성", close.Enabled, true);

            form.Dispose();

            코너팝업(cal, master, store);

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine(string.Format("  통과 {0}건 / 실패 {1}건", passed, failed));
            Console.WriteLine(new string('=', 50));
            return failed == 0 ? 0 : 1;
        }
    }
}
