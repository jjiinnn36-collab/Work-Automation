using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PaymentAlert
{
    static class Program
    {
        static string BaseDir;
        static string DataDir;
        static string LogPath;

        [STAThread]
        static int Main(string[] args)
        {
            BaseDir = AppDomain.CurrentDomain.BaseDirectory;
            DataDir = Path.Combine(BaseDir, "data");
            LogPath = Path.Combine(DataDir, "run.log");

            bool 강제표시 = false;      // --force : 오늘 이미 확인한 건도 다시 표시
            DateTime today = DateTime.Today;

            foreach (string a in args)
            {
                if (a == "--force") 강제표시 = true;
                else if (a.StartsWith("--date="))
                {
                    // 테스트용. 특정 날짜로 실행한다.
                    DateTime d;
                    if (DateTime.TryParse(a.Substring(7), out d)) today = d.Date;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                return Run(today, 강제표시);
            }
            catch (Exception ex)
            {
                Log("치명적 오류: " + ex);
                MessageBox.Show(
                    "납부 기한 알림을 실행하지 못했습니다.\r\n\r\n" + ex.Message +
                    "\r\n\r\n자세한 내용은 다음 파일을 확인하세요:\r\n" + LogPath,
                    "납부 기한 알림 - 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Run(DateTime today, bool 강제표시)
        {
            string masterPath = Path.Combine(DataDir, "payment-master.tsv");
            string statusPath = Path.Combine(DataDir, "status.tsv");
            string holidayPath = Path.Combine(DataDir, "holidays.tsv");
            string amountPath = Path.Combine(DataDir, "amounts.tsv");
            string apiKeyPath = Path.Combine(DataDir, "apikey.txt");

            if (!File.Exists(masterPath))
            {
                MessageBox.Show(
                    "납부 마스터 파일이 없습니다.\r\n\r\n" + masterPath +
                    "\r\n\r\ndata/payment-master.sample.tsv 를 복사해서 만들어 주세요.",
                    "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            var warnings = new List<string>();
            List<PaymentItem> master = Repository.LoadMaster(masterPath, warnings);
            if (master.Count == 0)
            {
                MessageBox.Show("납부 마스터에 유효한 항목이 없습니다.\r\n\r\n" + string.Join("\r\n", warnings.ToArray()),
                    "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            // ── 공휴일 준비 ──────────────────────────────────────
            var years = Scheduler.TargetYears(today);
            Holidays.Cache cache = Holidays.Load(holidayPath);

            string apiKey = null;
            if (File.Exists(apiKeyPath))
                apiKey = File.ReadAllText(apiKeyPath, Encoding.UTF8).Trim();

            string refreshMessage;
            if (Holidays.TryRefresh(cache, apiKey, years, out refreshMessage))
            {
                try { Holidays.Save(holidayPath, cache); }
                catch (Exception ex) { Log("공휴일 캐시 저장 실패: " + ex.Message); }
            }
            if (!string.IsNullOrEmpty(refreshMessage)) Log(refreshMessage);

            var cal = new BusinessDayCalendar(cache.Dates.Keys, cache.Years);

            // ── 표시 대상 계산 ───────────────────────────────────
            Dictionary<string, AmountRecord> amounts = Repository.LoadAmounts(amountPath, warnings);
            List<Occurrence> occurrences = Scheduler.BuildOccurrences(master, cal, today, amounts);
            Dictionary<string, StatusRecord> statusMap = Repository.LoadStatus(statusPath);

            if (강제표시)
                foreach (StatusRecord st in statusMap.Values)
                    if (st.최종확인일.HasValue && st.최종확인일.Value.Date == today.Date)
                        st.최종확인일 = null;

            RowSet set = Scheduler.BuildRows(occurrences, statusMap, cal, today);
            List<AlertRow> rows = set.Rows;

            foreach (AlertRow od in set.Overdue)
                Log(string.Format("기한초과 미처리: {0} {1} 기한 {2} 단계 {3}",
                    od.Occ.연도, od.Occ.Item.표시명,
                    od.Occ.보정기한일.ToString("yyyy-MM-dd"), od.현재단계명));

            if (rows.Count == 0)
            {
                Log(string.Format("{0}: 표시할 건 없음. (기한초과 미처리 {1}건)",
                    today.ToString("yyyy-MM-dd"), set.Overdue.Count));
                return 0;    // AC-24b: 물어볼 게 없으면 팝업을 띄우지 않는다
            }

            // ── 경고 문구 조립 ───────────────────────────────────
            string warningText = BuildWarning(cache, years, warnings, masterPath);

            var form = new AlertForm(rows, set.Overdue, cal, today, warningText);
            Application.Run(form);

            // ── 저장 ─────────────────────────────────────────────
            try
            {
                Repository.SaveStatus(statusPath, statusMap.Values, master);
                Log(string.Format("{0}: {1}건 표시, 저장 완료. (기한초과 미처리 {2}건)",
                    today.ToString("yyyy-MM-dd"), rows.Count, set.Overdue.Count));
            }
            catch (Exception ex)
            {
                Log("상태 저장 실패: " + ex);
                MessageBox.Show(
                    "진행 상태를 저장하지 못했습니다.\r\n오늘 누른 내용이 기록되지 않았습니다.\r\n\r\n" + ex.Message,
                    "납부 기한 알림 - 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }

            return 0;
        }

        static string BuildWarning(Holidays.Cache cache, List<int> years,
                                   List<string> masterWarnings, string masterPath)
        {
            var parts = new List<string>();

            // 엑셀 양식을 고치고 변환을 잊으면 프로그램은 옛 자료로 계속 돈다.
            // 알아채기 어려운 실패라 반드시 눈에 띄게 알린다.
            string templatePath = Path.Combine(DataDir, "payment-master-template.xlsx");
            if (File.Exists(templatePath) && File.Exists(masterPath))
            {
                DateTime x = File.GetLastWriteTime(templatePath);
                DateTime t = File.GetLastWriteTime(masterPath);
                if (x > t)
                {
                    parts.Add(string.Format(
                        "엑셀 양식이 납부 자료보다 최신입니다 (양식 {0}, 자료 {1}). " +
                        "엑셀에서 고친 내용이 아직 반영되지 않았습니다. convert-excel.bat 을 실행하세요.",
                        x.ToString("MM-dd HH:mm"), t.ToString("MM-dd HH:mm")));
                }
            }

            var missing = new List<string>();
            foreach (int y in years)
                if (!cache.Years.Contains(y)) missing.Add(y.ToString());

            if (missing.Count > 0)
            {
                parts.Add(string.Format(
                    "{0}년 공휴일 자료가 없습니다. 해당 연도는 주말만 반영해 계산하며, 안전을 위해 알림을 이틀 앞당겼습니다. " +
                    "data/holidays.tsv 를 채우거나 data/apikey.txt 에 공공데이터포털 서비스키를 넣어 주세요.",
                    string.Join(", ", missing.ToArray())));
            }
            else if (cache.경과일 > 90)
            {
                parts.Add(string.Format("공휴일 자료를 갱신한 지 {0}일 지났습니다. 임시공휴일이 반영되지 않았을 수 있습니다.", cache.경과일));
            }

            if (masterWarnings.Count > 0)
                parts.Add("납부 마스터 확인 필요: " + string.Join(" / ", masterWarnings.ToArray()));

            return parts.Count == 0 ? null : string.Join("\r\n", parts.ToArray());
        }

        static void Log(string message)
        {
            try
            {
                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + message + "\r\n",
                    new UTF8Encoding(true));
            }
            catch { /* 로그 실패로 프로그램을 멈추지 않는다 */ }
        }
    }
}
