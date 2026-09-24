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
            // 다른 PC 에 exe 만 옮겨도 되도록 자료 폴더가 없으면 만든다 (사용자 요청 2026-09-20).
            try { Directory.CreateDirectory(DataDir); } catch { }
            LogPath = DataPaths.로그(DataDir);

            bool 강제표시 = false;      // --force : 오늘 이미 확인한 건도 다시 표시
            bool 보드 = false;          // --board : 당월 기한 상시 보드
            bool 웹 = false;            // --web : 내 PC 전용 웹 화면 서버
            bool 브라우저열기 = true;   // --no-browser : 웹 서버만 띄우고 창은 열지 않는다
            DateTime today = DateTime.Today;
            DateTime? 보드기준일 = null;   // --date 를 보드에도 적용해 다른 달을 볼 수 있게 한다

            foreach (string a in args)
            {
                if (a == "--force") 강제표시 = true;
                else if (a == "--board") 보드 = true;
                else if (a == "--web") 웹 = true;
                else if (a == "--no-browser") 브라우저열기 = false;
                else if (a.StartsWith("--date="))
                {
                    // 테스트용. 특정 날짜로 실행한다.
                    DateTime d;
                    if (DateTime.TryParse(a.Substring(7), out d)) { today = d.Date; 보드기준일 = d.Date; }
                }
            }

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

                // 첫 실행이면 공휴일 인증키를 받아 자동으로 내려받는다 (holidays.tsv 없이도 동작).
                EnsureHolidays(today);

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
        /// 자료 DB 를 준비한다. 옛 TSV 가 있으면 한 번 옮기고, 없으면 빈 DB 를 만든다 (ADR-0013).
        /// 항목은 웹 '항목 관리' 에서 넣는다. 준비되면 null.
        /// </summary>
        static string EnsureDb()
        {
            var log = new List<string>();
            Importer.준비결과 r = Importer.자료준비(BaseDir, DataDir, log);
            if (r != Importer.준비결과.이미있음)
            {
                Log(r == Importer.준비결과.TSV옮김 ? "자료 DB 가 없어 TSV 자료를 옮겼습니다. 자료 폴더: " + DataDir
                                                  : "자료 DB 를 새로 만들었습니다. 자료 폴더: " + DataDir);
                foreach (string l in log) Log("  " + l);
            }
            return null;
        }

        /// <summary>
        /// 공휴일 자료를 준비한다. 자료도 인증키도 없는 첫 실행이면 공공데이터포털 인증키를 받아
        /// 자동으로 내려받는다. 키가 있으면 조용히 갱신만 한다 (사용자 요청 2026-09-20).
        /// </summary>
        static void EnsureHolidays(DateTime today)
        {
            try
            {
                string apiKeyPath = DataPaths.ApiKey(DataDir);
                string marker = Path.Combine(DataDir, ".holiday-ask-skip");
                var years = Scheduler.TargetYears(today);

                using (Store db = Store.Open(DataPaths.Db(DataDir)))
                {
                    Holidays.Cache cache = db.LoadHolidays();
                    string apiKey = File.Exists(apiKeyPath) ? File.ReadAllText(apiKeyPath, Encoding.UTF8).Trim() : null;

                    bool 빠진해있음 = false;
                    foreach (int y in years) if (!cache.Years.Contains(y)) { 빠진해있음 = true; break; }

                    // 첫 실행: 공휴일도 없고 키도 없으면 키를 받는다. '나중에' 를 고르면 표시를 남겨 다시 묻지 않는다.
                    bool 방금물음 = false;
                    if (string.IsNullOrEmpty(apiKey) && 빠진해있음 && !File.Exists(marker))
                    {
                        bool 나중에;
                        string key = ApiKeyPrompt.물어보기(out 나중에);
                        방금물음 = true;
                        if (!string.IsNullOrEmpty(key))
                        {
                            try { File.WriteAllText(apiKeyPath, key, new UTF8Encoding(false)); apiKey = key; try { File.Delete(marker); } catch { } }
                            catch (Exception ex) { Log("인증키 저장 실패: " + ex.Message); }
                        }
                        else
                        {
                            try { File.WriteAllText(marker, "설정 화면에서 인증키를 넣으면 공휴일을 자동으로 받습니다."); } catch { }
                            Log("공휴일 인증키 입력을 나중으로 미뤘습니다. 설정 화면에서 넣을 수 있습니다.");
                        }
                    }

                    if (string.IsNullOrEmpty(apiKey)) return;   // 키가 없으면 주말만 반영한다

                    Cursor.Current = Cursors.WaitCursor;
                    string msg;
                    bool 받음 = Holidays.TryRefresh(cache, apiKey, years, out msg);
                    Cursor.Current = Cursors.Default;
                    if (받음)
                    {
                        try { db.SaveHolidays(cache); }
                        catch (Exception ex) { Log("공휴일 저장 실패: " + ex.Message); }
                    }
                    if (!string.IsNullOrEmpty(msg)) Log(msg);

                    // 방금 키를 받은 첫 실행이면 결과를 한 번 알려 준다.
                    if (방금물음)
                    {
                        if (받음)
                            MessageBox.Show("공휴일 자료를 받았습니다.\r\n주말·공휴일이 기한 계산에 반영됩니다.",
                                "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        else
                            MessageBox.Show("공휴일 자료를 받지 못했습니다.\r\n\r\n" + (msg ?? "")
                                + "\r\n\r\n인터넷 연결과 인증키를 확인한 뒤, 설정 화면에서 다시 시도할 수 있습니다.",
                                "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex) { Log("공휴일 준비 중 오류: " + ex.Message); }
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
                    {
                        Log("이미 떠 있는 웹 화면을 엽니다: http://localhost:" + p + "/");
                        OpenBrowser("http://localhost:" + p + "/");
                    }
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
                    server.폴더고르기 = FolderPicker.Pick;
                    server.Start(WebServer.기본포트);
                    try { File.WriteAllText(portFile, server.Port.ToString(CultureInfo.InvariantCulture)); }
                    catch (Exception ex) { Log("웹 포트 기록 실패: " + ex.Message); }
                    Log("웹 화면 시작: " + server.Url + "  자료 폴더: " + DataDir);

                    using (var menu = new ContextMenuStrip())
                    using (var icon = new NotifyIcon())
                    {
                        menu.Items.Add("웹 화면 열기", null, delegate { OpenBrowser(server.Url); });
                        menu.Items.Add("끄기", null, delegate { Application.ExitThread(); });
                        menu.Font = Ui.글꼴(12);
                        Ui.메뉴꾸미기(menu);
                        Ui.메뉴항목정리(menu);

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

        /// <summary>
        /// 팝업의 설정 버튼. 자기 자신을 --web 으로 실행한다 —
        /// 웹 서버가 이미 떠 있으면 그 주소로 브라우저만 열고, 없으면 서버를 띄운 뒤 연다 (RunWeb).
        /// </summary>
        static void 웹화면열기()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--web");
                psi.WorkingDirectory = BaseDir;
                psi.UseShellExecute = false;
                using (Process.Start(psi)) { }
            }
            catch (Exception ex) { Log("웹 화면을 열지 못했습니다: " + ex.Message); }
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
                    MessageBox.Show("납부 항목이 없습니다.\r\n\r\nstart-web.bat 으로 웹 화면을 열고 '항목 관리' 에서 항목을 추가하세요.",
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

                // 오늘 '오늘은 대기' 를 눌러 미뤄 둔 건도 팝업에 보여 준다 (웹처럼, 사용자 요청 2026-09-22).
                // 최종확인일을 잠깐 비워 BuildRows 에 넣되, 어떤 건이 대기였는지 기억해 처리 상태로 표시한다.
                var 마지막동작 = db.마지막동작();
                var 오늘대기키 = new HashSet<string>(StringComparer.Ordinal);
                foreach (StatusRecord st in statusMap.Values)
                {
                    if (!st.최종확인일.HasValue || st.최종확인일.Value.Date != today.Date) continue;
                    string 마지막;
                    bool 대기 = 마지막동작.TryGetValue(st.Key, out 마지막) && 마지막 == "대기";
                    if (강제표시 || 대기)
                    {
                        if (대기) 오늘대기키.Add(st.Key);
                        st.최종확인일 = null;   // BuildRows 가 포함하도록
                    }
                }

                RowSet set = Scheduler.BuildRows(occurrences, statusMap, cal, today);
                List<AlertRow> rows = set.Rows;
                foreach (AlertRow r in rows) r.오늘이미대기 = 오늘대기키.Contains(r.Occ.Key);

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
                List<string> 경고들 = Warnings.Build(cache, years, today);
                string warningText = 경고들.Count == 0 ? null : string.Join("\r\n", 경고들.ToArray());

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
                        if (동작 == "진행") return s.Advance(o.연도, o.Item.Id, Stages.For(o.Item), row.Status.단계, DateTime.Now, today, "팝업", row.기한임박(today));
                        if (동작 == "대기") return s.Defer(o.연도, o.Item.Id, row.Status.단계, DateTime.Now, today, "팝업");
                        if (동작 == "대기취소") return s.대기취소(o.연도, o.Item.Id, row.Status.단계, DateTime.Now, today, "팝업");
                        return s.Revert(o.연도, o.Item.Id, Stages.For(o.Item), row.Status.단계, DateTime.Now, "팝업");
                    }
                };
                // 웹 화면에서 바꾼 것을 팝업이 따라잡는다 (2초마다 번호만 확인, 달라졌을 때만 다시 읽기).
                form.변경번호읽기 = delegate
                {
                    using (Store s = Store.Open(dbPath)) return s.변경번호();
                };
                form.상태다시읽기 = delegate
                {
                    using (Store s = Store.Open(dbPath))
                    {
                        Dictionary<string, StatusRecord> 새것 = s.LoadStatus();
                        foreach (AlertRow r in rows)
                        {
                            StatusRecord st;
                            if (!새것.TryGetValue(r.Occ.Key, out st)) continue;
                            r.Status.단계 = st.단계;
                            r.Status.변경일시 = st.변경일시;
                            r.Status.최종확인일 = st.최종확인일;
                        }
                    }
                };
                form.웹열기 = 웹화면열기;
                Application.Run(form);

                Log(string.Format("{0}: {1}건 표시. (기한초과 미처리 {2}건)",
                    today.ToString("yyyy-MM-dd"), rows.Count, set.Overdue.Count));
                return 0;
            }
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
