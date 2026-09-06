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

            Button close = null;
            foreach (Control c in form.Controls)
            {
                Button b = c as Button;
                if (b != null && b.Text == "닫기") close = b;
            }
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

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine(string.Format("  통과 {0}건 / 실패 {1}건", passed, failed));
            Console.WriteLine(new string('=', 50));
            return failed == 0 ? 0 : 1;
        }
    }
}
