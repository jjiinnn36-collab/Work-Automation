using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PaymentAlert
{
    /// <summary>
    /// 당월 기한을 화면 한켠에 상시 표시하는 보드.
    /// 매일 뜨는 알림 팝업과 달리 처리를 강제하지 않는다. 보기만 하는 창이다.
    /// </summary>
    public class MonthlyBoard : Form
    {
        readonly string dataDir;
        readonly string attachRoot;
        readonly string posPath;

        readonly Label headerLabel;
        readonly Label summaryLabel;
        readonly Panel listPanel;
        readonly Timer refreshTimer;

        DateTime shownMonth;
        readonly DateTime? 기준일override;
        readonly bool 자동높이;

        static readonly Color 배경 = Color.FromArgb(252, 252, 253);
        static readonly Color 테두리 = Color.FromArgb(222, 224, 230);
        static readonly Color 완료색 = Color.FromArgb(28, 132, 74);
        static readonly Color 긴급색 = Color.FromArgb(198, 40, 40);
        static readonly Color 임박색 = Color.FromArgb(190, 110, 0);
        static readonly Color 흐린글씨 = Color.FromArgb(120, 122, 130);
        static readonly Color 진한글씨 = Color.FromArgb(35, 37, 45);

        public MonthlyBoard(string dataDir, string attachRoot)
            : this(dataDir, attachRoot, null) { }

        public MonthlyBoard(string dataDir, string attachRoot, DateTime? 기준일)
        {
            this.dataDir = dataDir;
            this.attachRoot = attachRoot;
            this.기준일override = 기준일;
            this.posPath = Path.Combine(dataDir, "board-position.txt");
            // 사용자가 크기를 정한 적이 없으면 내용에 맞춰 줄인다.
            this.자동높이 = !File.Exists(this.posPath);

            Text = "당월 납부 기한";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = 배경;
            Font = new Font("맑은 고딕", 9f);
            MinimumSize = new Size(260, 220);
            ClientSize = new Size(330, 460);

            headerLabel = new Label();
            headerLabel.AutoSize = false;
            headerLabel.Dock = DockStyle.Top;
            headerLabel.Height = 28;
            headerLabel.TextAlign = ContentAlignment.MiddleLeft;
            headerLabel.Padding = new Padding(10, 0, 0, 0);
            headerLabel.Font = new Font("맑은 고딕", 11f, FontStyle.Bold);
            headerLabel.ForeColor = 진한글씨;

            summaryLabel = new Label();
            summaryLabel.AutoSize = false;
            summaryLabel.Dock = DockStyle.Top;
            summaryLabel.Height = 22;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Padding = new Padding(10, 0, 0, 0);
            summaryLabel.ForeColor = 흐린글씨;

            listPanel = new Panel();
            listPanel.Dock = DockStyle.Fill;
            listPanel.AutoScroll = true;
            listPanel.BackColor = 배경;
            listPanel.Padding = new Padding(8, 4, 8, 8);

            var foot = new Label();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 22;
            foot.TextAlign = ContentAlignment.MiddleLeft;
            foot.Padding = new Padding(10, 0, 0, 0);
            foot.ForeColor = 흐린글씨;
            foot.Text = "항목을 두 번 누르면 증빙 폴더가 열립니다.";

            Controls.Add(listPanel);
            Controls.Add(foot);
            Controls.Add(summaryLabel);
            Controls.Add(headerLabel);

            RestorePosition();

            // 자정을 넘기면 달과 D-day 가 바뀐다. 주기적으로 다시 그린다.
            refreshTimer = new Timer();
            refreshTimer.Interval = 10 * 60 * 1000;   // 10분
            refreshTimer.Tick += delegate { Reload(); };
            refreshTimer.Start();

            Reload();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePosition();
            base.OnFormClosing(e);
        }

        /// <summary>자료를 다시 읽어 당월 목록을 그린다.</summary>
        public void Reload()
        {
            DateTime today = 기준일override.HasValue ? 기준일override.Value : DateTime.Today;
            shownMonth = new DateTime(today.Year, today.Month, 1);

            listPanel.Controls.Clear();

            List<Occurrence> month;
            Dictionary<string, StatusRecord> status;
            BusinessDayCalendar cal;
            string error = null;

            try
            {
                month = LoadMonth(today, out status, out cal);
            }
            catch (Exception ex)
            {
                month = new List<Occurrence>();
                status = new Dictionary<string, StatusRecord>();
                cal = new BusinessDayCalendar(new DateTime[0], new int[0]);
                error = ex.Message;
            }

            headerLabel.Text = string.Format("{0}년 {1}월 기한", today.Year, today.Month);

            if (error != null)
            {
                summaryLabel.Text = "자료를 읽지 못했습니다.";
                summaryLabel.ForeColor = 긴급색;
                AddNotice("오류: " + error);
                return;
            }

            if (month.Count == 0)
            {
                summaryLabel.Text = "이번 달 기한이 없습니다.";
                summaryLabel.ForeColor = 흐린글씨;
                AddNotice("이번 달에는 납부·신고 기한이 없습니다.");
                return;
            }

            int done = 0;
            foreach (Occurrence o in month)
            {
                if (IsDone(o, status)) done++;
            }

            summaryLabel.Text = string.Format("전체 {0}건 · 완료 {1}건 · 남은 {2}건",
                month.Count, done, month.Count - done);
            summaryLabel.ForeColor = (done == month.Count) ? 완료색 : 흐린글씨;

            int y = 0;
            foreach (Occurrence o in month)
            {
                Panel row = BuildRow(o, status, cal, today);
                row.Location = new Point(0, y);
                listPanel.Controls.Add(row);
                y += row.Height + 6;
            }

            FitHeight(y);
        }

        /// <summary>내용 높이에 맞춰 창을 줄인다. 화면을 넘지 않게 상한을 둔다.</summary>
        void FitHeight(int 목록높이)
        {
            if (!자동높이) return;

            int 고정 = headerLabel.Height + summaryLabel.Height + 22 + 24;  // 머리말+요약+안내+여백
            int 원하는높이 = 고정 + 목록높이 + 12;

            int 최대 = Screen.PrimaryScreen.WorkingArea.Height - Top - 40;
            if (최대 < MinimumSize.Height) 최대 = MinimumSize.Height;

            int h = Math.Min(원하는높이, 최대);
            if (h < MinimumSize.Height) h = MinimumSize.Height;

            if (ClientSize.Height != h) ClientSize = new Size(ClientSize.Width, h);
        }

        List<Occurrence> LoadMonth(DateTime today,
                                   out Dictionary<string, StatusRecord> status,
                                   out BusinessDayCalendar cal)
        {
            var warnings = new List<string>();
            List<PaymentItem> master = Repository.LoadMaster(
                Path.Combine(dataDir, "payment-master.tsv"), warnings);

            Holidays.Cache cache = Holidays.Load(Path.Combine(dataDir, "holidays.tsv"));
            cal = new BusinessDayCalendar(cache.Dates.Keys, cache.Years);

            Dictionary<string, AmountRecord> amounts =
                Repository.LoadAmounts(Path.Combine(dataDir, "amounts.tsv"), null);

            status = Repository.LoadStatus(Path.Combine(dataDir, "status.tsv"));

            List<Occurrence> all = Scheduler.BuildOccurrences(master, cal, today, amounts);

            // 보정된 기한이 이번 달에 드는 건만 고른다.
            // 원기한이 지난달 말이어도 밀려서 이번 달이면 이번 달 일이다.
            var month = new List<Occurrence>();
            foreach (Occurrence o in all)
            {
                if (o.보정기한일.Year == today.Year && o.보정기한일.Month == today.Month)
                    month.Add(o);
            }
            month.Sort(delegate(Occurrence a, Occurrence b)
            {
                int c = a.보정기한일.CompareTo(b.보정기한일);
                if (c != 0) return c;
                return string.Compare(a.Item.Id, b.Item.Id, StringComparison.Ordinal);
            });
            return month;
        }

        static bool IsDone(Occurrence o, Dictionary<string, StatusRecord> status)
        {
            StatusRecord st;
            if (!status.TryGetValue(o.Key, out st)) return false;
            return st.단계 >= Stages.FinalIndex(o.Item.진행흐름);
        }

        static string StageName(Occurrence o, Dictionary<string, StatusRecord> status)
        {
            StatusRecord st;
            string[] stages = Stages.For(o.Item.진행흐름);
            if (!status.TryGetValue(o.Key, out st)) return stages[0];
            int i = st.단계;
            if (i < 0) i = 0;
            if (i >= stages.Length) i = stages.Length - 1;
            return stages[i];
        }

        void AddNotice(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(listPanel.ClientSize.Width - 20, 40);
            l.Location = new Point(0, 0);
            l.ForeColor = 흐린글씨;
            listPanel.Controls.Add(l);
        }

        Panel BuildRow(Occurrence o, Dictionary<string, StatusRecord> status,
                       BusinessDayCalendar cal, DateTime today)
        {
            bool done = IsDone(o, status);
            int 남은 = cal.BusinessDaysBetween(today, o.보정기한일);

            var p = new Panel();
            p.Width = Math.Max(listPanel.ClientSize.Width - 22, 220);
            p.Height = 52;
            p.BackColor = done ? Color.FromArgb(245, 250, 246) : Color.White;
            p.BorderStyle = BorderStyle.FixedSingle;
            p.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            var 날짜 = new Label();
            날짜.Text = o.보정기한일.ToString("MM/dd", CultureInfo.InvariantCulture);
            날짜.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            날짜.AutoSize = true;
            날짜.Location = new Point(8, 6);
            날짜.ForeColor = done ? 흐린글씨 : 진한글씨;
            p.Controls.Add(날짜);

            var 요일 = new Label();
            요일.Text = o.보정기한일.ToString("ddd", new CultureInfo("ko-KR"));
            요일.AutoSize = true;
            요일.Location = new Point(10, 28);
            요일.ForeColor = 흐린글씨;
            p.Controls.Add(요일);

            var 이름 = new Label();
            이름.Text = o.Item.비용명;
            이름.Font = new Font("맑은 고딕", 9.75f, done ? FontStyle.Regular : FontStyle.Bold);
            이름.AutoSize = true;
            이름.Location = new Point(52, 6);
            이름.ForeColor = done ? 흐린글씨 : 진한글씨;
            p.Controls.Add(이름);

            var 상태 = new Label();
            상태.AutoSize = true;
            상태.Location = new Point(52, 28);
            if (done)
            {
                상태.Text = "완료";
                상태.ForeColor = 완료색;
            }
            else
            {
                string d;
                if (남은 > 0) d = string.Format("D-{0}영업일", 남은);
                else if (남은 == 0) d = "오늘";
                else d = string.Format("{0}영업일 초과", -남은);
                상태.Text = d + " · " + StageName(o, status);
                상태.ForeColor = (남은 < 0) ? 긴급색 : (남은 <= 3 ? 임박색 : 흐린글씨);
            }
            p.Controls.Add(상태);

            var 금액 = new Label();
            금액.Text = o.실제금액 != null
                ? string.Format("{0:N0}", o.실제금액.금액)
                : (o.Item.고정금액.HasValue ? string.Format("{0:N0}", o.Item.고정금액.Value) : "");
            금액.AutoSize = true;
            금액.Location = new Point(52, 28);
            금액.Visible = false;   // 폭이 좁아 기본은 숨김. 도구설명으로 보여준다.
            p.Controls.Add(금액);

            string tip = string.Format("{0} ({1})\n기한 {2}\n{3}",
                o.Item.비용명, o.Item.기관,
                o.보정기한일.ToString("yyyy-MM-dd"),
                금액.Text.Length > 0 ? 금액.Text + "원" : "금액 미확인");
            var tt = new ToolTip();
            tt.SetToolTip(p, tip);
            tt.SetToolTip(이름, tip);
            tt.SetToolTip(상태, tip);
            tt.SetToolTip(날짜, tip);

            // 두 번 누르면 증빙 폴더
            MouseEventHandler dbl = delegate(object s, MouseEventArgs e) { OpenAttachments(o); };
            p.MouseDoubleClick += dbl;
            이름.MouseDoubleClick += dbl;
            상태.MouseDoubleClick += dbl;
            날짜.MouseDoubleClick += dbl;

            return p;
        }

        void OpenAttachments(Occurrence o)
        {
            try
            {
                string folder = Path.Combine(attachRoot,
                    o.연도.ToString(CultureInfo.InvariantCulture), o.Item.Id);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "폴더를 열지 못했습니다.\r\n\r\n" + ex.Message,
                    "증빙", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── 창 위치 기억 ─────────────────────────────────────
        void RestorePosition()
        {
            StartPosition = FormStartPosition.Manual;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - Width - 16;
            int y = wa.Top + 60;

            try
            {
                if (File.Exists(posPath))
                {
                    string[] parts = File.ReadAllText(posPath, Encoding.UTF8).Trim().Split(',');
                    if (parts.Length >= 4)
                    {
                        int px, py, pw, ph;
                        if (int.TryParse(parts[0], out px) && int.TryParse(parts[1], out py) &&
                            int.TryParse(parts[2], out pw) && int.TryParse(parts[3], out ph))
                        {
                            // 모니터 구성이 바뀌어 화면 밖이면 기본 위치로 되돌린다.
                            var r = new Rectangle(px, py, pw, ph);
                            if (IsOnAnyScreen(r))
                            {
                                Location = new Point(px, py);
                                Size = new Size(Math.Max(pw, MinimumSize.Width),
                                                Math.Max(ph, MinimumSize.Height));
                                return;
                            }
                        }
                    }
                }
            }
            catch { /* 위치 복원 실패는 무시하고 기본 위치를 쓴다 */ }

            Location = new Point(x, y);
        }

        static bool IsOnAnyScreen(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(r)) return true;
            return false;
        }

        void SavePosition()
        {
            try
            {
                Rectangle b = (WindowState == FormWindowState.Normal)
                    ? Bounds : RestoreBounds;
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                File.WriteAllText(posPath,
                    string.Format("{0},{1},{2},{3}", b.X, b.Y, b.Width, b.Height),
                    new UTF8Encoding(false));
            }
            catch { /* 위치 저장 실패로 종료를 막지 않는다 */ }
        }
    }
}
