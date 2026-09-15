using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
            DataDir = DataPaths.자료폴더(BaseDir);
            LogPath = DataPaths.로그(DataDir);

            bool 강제표시 = false;      // --force : 오늘 이미 확인한 건도 다시 표시
            bool 보드 = false;          // --board : 당월 기한 상시 보드
            bool 웹 = false;            // --web : 내 PC 전용 웹 화면 서버
            bool 브라우저열기 = true;   // --no-browser : 웹 서버만 띄우고 창은 열지 않는다
            string 가져오기 = null;     // --import-master / --import-amounts : 도구가 쓴 TSV 를 DB 로
            DateTime today = DateTime.Today;
            DateTime? 보드기준일 = null;   // --date 를 보드에도 적용해 다른 달을 볼 수 있게 한다

            foreach (string a in args)
            {
                if (a == "--force") 강제표시 = true;
                else if (a == "--board") 보드 = true;
                else if (a == "--web") 웹 = true;
                else if (a == "--no-browser") 브라우저열기 = false;
                else if (a == "--import-master" || a == "--import-amounts") 가져오기 = a;
                else if (a.StartsWith("--date="))
                {
                    // 테스트용. 특정 날짜로 실행한다.
                    DateTime d;
                    if (DateTime.TryParse(a.Substring(7), out d)) { today = d.Date; 보드기준일 = d.Date; }
                }
            }

            // 가져오기는 창 없이 끝낸다. 배치 파일이 종료 코드로 성공 여부를 판단한다.
            if (가져오기 != null) return RunImport(가져오기);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                string 준비실패 = EnsureDb();
                if (준비실패 != null)
                {
                    MessageBox.Show(준비실패, "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
                }

                if (웹) return RunWeb(보드기준일, 브라우저열기);
                if (보드) return RunBoard(보드기준일);

                // 웹에서 연달아 고치면 여러 번 불릴 수 있다. 팝업은 하나만 띄운다.
                bool 첫팝업;
                using (var mutex = new System.Threading.Mutex(true, "PaymentAlert.Popup", out 첫팝업))
                {
                    if (!첫팝업)
                    {
                        Log("알림 팝업이 이미 떠 있습니다.");
                        return 0;
                    }
                    return Run(today, 강제표시);
                }
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

        /// <summary>
        /// 자료 DB 가 없으면 기존 TSV 자료를 한 번 옮긴다.
        /// 옮길 자료조차 없으면 사용자에게 보여줄 문구를 돌려준다. 준비되면 null.
        /// </summary>
        static string EnsureDb()
        {
            string db = DataPaths.Db(DataDir);
            if (File.Exists(db)) return null;

            string master = Path.Combine(DataPaths.가져오기폴더(BaseDir), "payment-master.tsv");
            if (!File.Exists(master))
                return "납부 자료가 없습니다.\r\n\r\n" + master +
                       "\r\n\r\ndata/payment-master.sample.tsv 를 복사해서 만들어 주세요.";

            Log("자료 DB 가 없어 TSV 자료를 옮깁니다. 자료 폴더: " + DataDir);
            var log = new List<string>();
            Importer.최초이전(BaseDir, DataDir, log);
            foreach (string l in log) Log("  " + l);
            Log("옮기기 완료: " + db);
            return null;
        }

        static int RunImport(string 종류)
        {
            try
            {
                string 준비실패 = EnsureDb();
                if (준비실패 != null)
                {
                    Log(준비실패.Replace("\r\n", " "));
                    return 2;
                }

                var log = new List<string>();
                int rc = 종류 == "--import-master"
                    ? Importer.항목다시가져오기(BaseDir, DataDir, log)
                    : Importer.금액다시가져오기(BaseDir, DataDir, log);
                foreach (string l in log) Log(l);
                return rc;
            }
            catch (Exception ex)
            {
                Log("가져오기 실패: " + ex);
                return 1;
            }
        }

        /// <summary>당월 기한 보드를 띄운다. 처리를 강제하지 않는 보기 전용 창이다.</summary>
        static int RunBoard(DateTime? 기준일)
        {
            // 같은 보드를 두 개 띄우지 않는다.
            bool isNew;
            using (var mutex = new System.Threading.Mutex(true, "PaymentAlert.MonthlyBoard", out isNew))
            {
                if (!isNew)
                {
                    Log("보드가 이미 실행 중입니다.");
                    return 0;
                }

                var board = new MonthlyBoard(DataDir, DataPaths.증빙(DataDir), 기준일);
                Application.Run(board);
            }
            return 0;
        }

        /// <summary>
        /// 웹 화면 서버를 띄우고 알림 영역 아이콘으로 남는다. 이미 떠 있으면 그 화면만 연다.
        /// </summary>
        static int RunWeb(DateTime? 기준일, bool 브라우저열기)
        {
            string portFile = Path.Combine(Path.GetTempPath(), "PaymentAlert.web-port");

            bool isNew;
            using (var mutex = new System.Threading.Mutex(true, "PaymentAlert.Web", out isNew))
            {
                if (!isNew)
                {
                    int p;
                    if (File.Exists(portFile) && int.TryParse(File.ReadAllText(portFile).Trim(), out p))
                        OpenBrowser("http://localhost:" + p + "/");
                    else
                        Log("웹 화면이 이미 실행 중이지만 주소를 알 수 없습니다.");
                    return 0;
                }

                Func<DateTime> 오늘 = delegate { return 기준일.HasValue ? 기준일.Value : DateTime.Today; };
                using (var server = new WebServer(DataDir, Path.Combine(BaseDir, "web"), 오늘))
                {
                    server.Log = Log;
                    server.BaseDir = BaseDir;
                    using (Store db = Store.Open(DataPaths.Db(DataDir))) 일일백업(db, 오늘());
                    server.팝업요청 = delegate(string id) { 팝업띄우기(기준일, id); };
                    server.Start(WebServer.기본포트);
                    try { File.WriteAllText(portFile, server.Port.ToString(CultureInfo.InvariantCulture)); }
                    catch (Exception ex) { Log("웹 포트 기록 실패: " + ex.Message); }
                    Log("웹 화면 시작: " + server.Url + "  자료 폴더: " + DataDir);

                    using (var menu = new ContextMenuStrip())
                    using (var icon = new NotifyIcon())
                    {
                        menu.Items.Add("웹 화면 열기", null, delegate { OpenBrowser(server.Url); });
                        menu.Items.Add("끄기", null, delegate { Application.ExitThread(); });

                        Icon appIcon = null;
                        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                        icon.Icon = appIcon ?? SystemIcons.Application;
                        icon.Text = "납부 기한 알림 웹 화면";
                        icon.ContextMenuStrip = menu;
                        icon.DoubleClick += delegate { OpenBrowser(server.Url); };
                        icon.Visible = true;

                        if (브라우저열기) OpenBrowser(server.Url);
                        Application.Run();
                        icon.Visible = false;
                    }

                    try { File.Delete(portFile); } catch { }
                    Log("웹 화면 종료");
                }
            }
            return 0;
        }

        /// <summary>알림 팝업을 별도 실행으로 띄운다. 웹 서버가 멈추지 않게 기다리지 않는다.</summary>
        static void 팝업띄우기(DateTime? 기준일, string id)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath);
            psi.WorkingDirectory = BaseDir;
            psi.UseShellExecute = false;
            if (기준일.HasValue)
                psi.Arguments = "--date=" + 기준일.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using (Process.Start(psi)) { }
            Log("웹에서 고친 항목이 오늘 알릴 건이라 알림 팝업을 띄웁니다: " + id);
        }

        static void OpenBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log("브라우저를 열지 못했습니다: " + ex.Message); }
        }

        static int Run(DateTime today, bool 강제표시)
        {
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                일일백업(db, today);
                List<PaymentItem> master = db.LoadMaster();
                if (master.Count == 0)
                {
                    MessageBox.Show("납부 항목이 없습니다.\r\n\r\nconvert-excel.bat 으로 엑셀 양식을 가져와 주세요.",
                        "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
                }

                // ── 공휴일 준비 ──────────────────────────────────────
                var years = Scheduler.TargetYears(today);
                Holidays.Cache cache = db.LoadHolidays();

                string apiKey = null;
                string apiKeyPath = DataPaths.ApiKey(DataDir);
                if (File.Exists(apiKeyPath))
                    apiKey = File.ReadAllText(apiKeyPath, Encoding.UTF8).Trim();

                string refreshMessage;
                if (Holidays.TryRefresh(cache, apiKey, years, out refreshMessage))
                {
                    try { db.SaveHolidays(cache); }
                    catch (Exception ex) { Log("공휴일 저장 실패: " + ex.Message); }
                }
                if (!string.IsNullOrEmpty(refreshMessage)) Log(refreshMessage);

                var cal = new BusinessDayCalendar(cache.Dates.Keys, cache.Years);

                // ── 표시 대상 계산 ───────────────────────────────────
                Dictionary<string, AmountRecord> amounts = db.LoadAmounts();
                DateTime? 시작일 = db.LoadStartDate();
                List<Occurrence> occurrences = Scheduler.BuildOccurrences(master, cal, today, amounts, 시작일);
                Dictionary<string, StatusRecord> statusMap = db.LoadStatus();

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
                string warningText = BuildWarning(db, cache, years);

                var store = new AttachmentStore(db, DataPaths.증빙(DataDir));
                try { store.Load(); }
                catch (Exception ex) { Log("증빙 목록을 읽지 못했습니다: " + ex.Message); }

                var form = new AlertForm(rows, set.Overdue, cal, today, warningText, store);

                // 누를 때마다 바로 DB 에 쓴다 (ADR-0004). 창이 비정상 종료돼도 누른 것은 남고,
                // 그사이 웹에서 같은 건을 바꿨으면 DB 가 거절해 두 번 진행되지 않는다.
                string dbPath = DataPaths.Db(DataDir);
                form.단계변경 = delegate(AlertRow row, string 동작)
                {
                    using (Store s = Store.Open(dbPath))
                    {
                        Occurrence o = row.Occ;
                        if (동작 == "진행") return s.Advance(o.연도, o.Item.Id, o.Item.진행흐름, row.Status.단계, DateTime.Now, today, "팝업");
                        if (동작 == "대기") return s.Defer(o.연도, o.Item.Id, row.Status.단계, DateTime.Now, today, "팝업");
                        return s.Revert(o.연도, o.Item.Id, o.Item.진행흐름, row.Status.단계, DateTime.Now, "팝업");
                    }
                };
                Application.Run(form);

                Log(string.Format("{0}: {1}건 표시. (기한초과 미처리 {2}건)",
                    today.ToString("yyyy-MM-dd"), rows.Count, set.Overdue.Count));
                return 0;
            }
        }

        static string BuildWarning(Store db, Holidays.Cache cache, List<int> years)
        {
            var parts = new List<string>();

            // 엑셀 양식을 고치고 가져오기를 잊으면 프로그램은 옛 자료로 계속 돈다.
            // 알아채기 어려운 실패라 반드시 눈에 띄게 알린다.
            string templatePath = Path.Combine(DataPaths.가져오기폴더(BaseDir), "payment-master-template.xlsx");
            string 기록 = db.GetMeta("master_updated_at");
            DateTime 가져온시각;
            if (File.Exists(templatePath) && 기록 != null &&
                DateTime.TryParse(기록, CultureInfo.InvariantCulture, DateTimeStyles.None, out 가져온시각))
            {
                DateTime x = File.GetLastWriteTime(templatePath);
                if (x > 가져온시각)
                {
                    parts.Add(string.Format(
                        "엑셀 양식이 가져온 항목보다 최신입니다 (양식 {0}, 가져옴 {1}). " +
                        "엑셀에서 고친 내용이 아직 반영되지 않았습니다. convert-excel.bat 을 실행하세요.",
                        x.ToString("MM-dd HH:mm"), 가져온시각.ToString("MM-dd HH:mm")));
                }
            }

            parts.AddRange(Warnings.Build(cache, years, DateTime.Today));
            return parts.Count == 0 ? null : string.Join("\r\n", parts.ToArray());
        }

        /// <summary>하루 첫 실행에 DB 사본을 만든다 (ADR-0008). 실패해도 알림은 계속한다.</summary>
        static void 일일백업(Store db, DateTime today)
        {
            try
            {
                string made = Backups.일일백업(db, Backups.폴더(BaseDir, DataDir), today, Backups.기본보관);
                if (made != null) Log("자료 사본을 만들었습니다: " + made);
            }
            catch (Exception ex) { Log("자료 사본 만들기 실패: " + ex.Message); }
        }

        static void Log(string message)
        {
            // 크기를 넘으면 run.1.log … 로 밀어낸다 (ADR-0008). 실패해도 멈추지 않는다.
            LogFile.Append(LogPath, message);
        }
    }
}
