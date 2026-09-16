using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaymentAlert
{
    /// <summary>
    /// 알림 팝업 — 화면 오른쪽 아래, 작업 표시줄 바로 위에 붙는 340×260 둥근 창 (ADR-0009, 시안 v4).
    /// 한 번에 한 건을 보여 주고 옆으로 넘긴다. 아래 점이 몇 번째 건인지와 처리 상태를 알려 준다.
    /// 표시된 모든 건을 진행하거나 '오늘은 대기' 를 골라야 닫힌다 (AC-11, AC-25).
    /// </summary>
    public class AlertForm : Form
    {
        public const int 폭 = 340;
        public const int 높이 = 260;
        public const int 오른쪽여백 = 12;
        /// <summary>접었을 때 높이 — 머리(제목 줄)만 남는다.</summary>
        public const int 접힌높이 = 44;
        const int 카드폭 = 316;
        const int 카드높이 = 150;
        const int 자동넘김ms = 400;

        readonly List<AlertRow> rows;
        readonly List<AlertRow> overdue;
        readonly BusinessDayCalendar cal;
        readonly DateTime today;
        readonly AttachmentStore store;
        readonly string warningText;

        int index;
        bool allowClose;

        readonly Label 제목;
        readonly Label 건수;
        readonly Label 날짜;
        readonly Label 경고;
        readonly CardPanel 카드;
        readonly Label 상태;
        readonly Label 기관;
        readonly Label 이름;
        readonly Label 기한;
        readonly Label 금액;
        readonly StepDots 단계점;
        readonly PillButton 진행;
        readonly PillButton 대기;
        readonly PillButton 더보기;
        readonly PillButton 되돌리기;
        readonly Label 처리표시;
        readonly PageDots 점;
        readonly PillButton 이전;
        readonly PillButton 다음;
        readonly Panel 바닥;
        readonly Label summaryLabel;
        readonly Label 밀린건;
        readonly PillButton closeButton;
        readonly ContextMenuStrip 메뉴;
        readonly Timer 넘김타이머;

        /// <summary>
        /// 누른 즉시 DB 에 쓰는 통로 (ADR-0004). 인자는 행과 동작(진행/대기/되돌리기),
        /// 돌려주는 값은 DB 에 기록된 뒤의 상태. 비어 있으면 메모리에서만 바꾼다 (화면 시험용).
        /// </summary>
        public Func<AlertRow, string, StatusRecord> 단계변경;

        /// <summary>작업 영역(작업 표시줄을 뺀 화면)에서 팝업이 놓일 자리. 아래는 틈 없이, 오른쪽은 12px 띄운다.</summary>
        public static Rectangle 자리(Rectangle 작업영역)
        {
            return new Rectangle(작업영역.Right - 폭 - 오른쪽여백, 작업영역.Bottom - 높이, 폭, 높이);
        }

        public AlertForm(List<AlertRow> rows, List<AlertRow> overdue,
                         BusinessDayCalendar cal, DateTime today, string warningText,
                         AttachmentStore store)
        {
            this.rows = rows;
            this.overdue = overdue ?? new List<AlertRow>();
            this.cal = cal;
            this.today = today;
            this.store = store;
            this.warningText = warningText;

            Text = "납부 기한 알림";                 // 작업 표시줄에 보이는 이름
            FormBorderStyle = FormBorderStyle.None;  // 제목줄 없음 — 끌어 옮길 수 없다 (자리 고정)
            StartPosition = FormStartPosition.Manual;
            Bounds = 자리(Screen.PrimaryScreen.WorkingArea);
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            BackColor = Ui.캔버스;
            Font = Ui.글꼴(13);
            ForeColor = Ui.잉크;
            DoubleBuffered = true;

            // ── 머리 ──
            제목 = 라벨("기한 알림", 15, true, Ui.잉크);
            제목.Location = new Point(16, 13);
            Controls.Add(제목);

            건수 = 라벨("", 15, true, Ui.강조);
            Controls.Add(건수);

            날짜 = 라벨(today.ToString("M'/'d (ddd)", new CultureInfo("ko-KR")), 12, false, Ui.아주흐림);
            Controls.Add(날짜);

            경고 = 라벨("⚠ 자료 확인", 12, true, Color.FromArgb(0xb4, 0x53, 0x09));
            경고.Cursor = Cursors.Hand;
            경고.Visible = !string.IsNullOrEmpty(warningText);
            경고.Click += delegate
            {
                MessageBox.Show(this, warningText, "납부 기한 알림 - 자료 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Controls.Add(경고);

            // 작은 접기 버튼. 접으면 제목 줄만 작업 표시줄 위에 남고, 다시 누르거나 제목을 누르면 펼친다.
            접기버튼 = 알약("▾", false, 10);
            접기버튼.Size = new Size(24, 22);
            접기버튼.Location = new Point(폭 - 12 - 24, 11);
            접기버튼.Click += delegate { 접기(!접힘); };
            Controls.Add(접기버튼);

            // 설정 버튼: 웹 화면(항목 관리·설정)을 연다. 웹 서버가 꺼져 있으면 부르는 쪽이 띄운다.
            // 이모지 ⚙ 는 컬러 글꼴로 까맣게 뭉개져 Windows 기호 글꼴(Segoe MDL2 Assets)의 톱니를 쓴다. 없으면 글자로.
            bool 기호글꼴 = Ui.글꼴있음("Segoe MDL2 Assets");
            설정버튼 = 알약(기호글꼴 ? "" : "설정", false, 11);
            if (기호글꼴) 설정버튼.Font = new Font("Segoe MDL2 Assets", 8f, FontStyle.Regular, GraphicsUnit.Point);
            설정버튼.Size = new Size(기호글꼴 ? 24 : 40, 22);
            설정버튼.Location = new Point(접기버튼.Left - 4 - 설정버튼.Width, 11);
            설정버튼.Click += delegate { if (웹열기 != null) 웹열기(); };
            Controls.Add(설정버튼);
            var 풍선 = new ToolTip();
            풍선.SetToolTip(설정버튼, "웹 화면 열기 (항목 관리·설정)");
            풍선.SetToolTip(접기버튼, "접기 / 펼치기");
            제목.Click += delegate { if (접힘) 접기(false); };
            건수.Click += delegate { if (접힘) 접기(false); };
            Click += delegate { if (접힘) 접기(false); };
            접힌풍선 = new ToolTip();

            // ── 카드 ──
            카드 = new CardPanel();
            카드.반경 = 10;
            카드.띠폭 = 3;
            카드.Location = new Point(12, 44);
            카드.Size = new Size(카드폭, 카드높이);
            Controls.Add(카드);

            상태 = 라벨("", 11, true, Ui.강조);
            상태.Location = new Point(13, 10);
            카드.Controls.Add(상태);

            기관 = 라벨("", 11, false, Ui.흐린글씨);
            카드.Controls.Add(기관);

            이름 = 라벨("", 16, true, Ui.잉크);
            이름.AutoSize = false;
            이름.AutoEllipsis = true;
            이름.Location = new Point(12, 27);
            이름.Size = new Size(150, 24);
            카드.Controls.Add(이름);

            기한 = 라벨("", 11, false, Ui.흐린글씨);
            카드.Controls.Add(기한);

            금액 = 라벨("", 13, true, Ui.잉크);
            카드.Controls.Add(금액);

            단계점 = new StepDots();
            단계점.점 = 9;
            단계점.라벨px = 10;
            단계점.높이맞추기();
            단계점.Location = new Point(13, 56);
            단계점.Width = 카드폭 - 26;
            카드.Controls.Add(단계점);

            진행 = 알약("", true, 12);
            진행.Location = new Point(12, 106);
            진행.Click += delegate { 적용후넘김(현재행(), "진행"); };
            카드.Controls.Add(진행);

            대기 = 알약("오늘은 대기", false, 12);
            대기.Click += delegate { 적용후넘김(현재행(), "대기"); };
            카드.Controls.Add(대기);

            처리표시 = 라벨("", 11, true, Ui.흐린글씨);
            처리표시.BackColor = Ui.양피지;
            처리표시.Padding = new Padding(8, 4, 8, 4);
            처리표시.Location = new Point(12, 110);
            카드.Controls.Add(처리표시);

            되돌리기 = 알약("되돌리기", false, 12);
            되돌리기.Click += delegate { 되돌리기누름(); };
            카드.Controls.Add(되돌리기);

            메뉴 = new ContextMenuStrip();
            메뉴.Font = Ui.글꼴(12);
            Ui.메뉴꾸미기(메뉴);
            더보기 = 알약("⋯", false, 13);
            더보기.Size = new Size(40, 28);
            더보기.Click += delegate { 메뉴채우기(); 메뉴.Show(더보기, new Point(0, 더보기.Height)); };
            카드.Controls.Add(더보기);

            // 카드를 옆으로 끌어 넘긴다. 버튼 위에서는 끌지 않는다.
            끌기연결(카드);
            foreach (Control c in 카드.Controls) if (!(c is Button)) 끌기연결(c);

            // ── 넘기기 줄 ──
            이전 = 알약("‹", false, 14);
            이전.Size = new Size(26, 22);
            이전.Click += delegate { 이동(index - 1); };
            Controls.Add(이전);

            점 = new PageDots();
            점.처리됨 = delegate(int i) { return rows[i].오늘처리됨(today); };
            점.지남 = delegate(int i) { return rows[i].Occ.남은영업일(cal, today) < 0; };
            점.눌림 += delegate(int i) { 이동(i); };
            Controls.Add(점);

            다음 = 알약("›", false, 14);
            다음.Size = new Size(26, 22);
            다음.Click += delegate { 이동(index + 1); };
            Controls.Add(다음);

            // ── 바닥 ──
            바닥 = new Panel();
            바닥.BackColor = Ui.양피지;
            바닥.Location = new Point(0, 높이 - 38);
            바닥.Size = new Size(폭, 38);
            바닥.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Ui.연한선)) e.Graphics.DrawLine(pen, 0, 0, 폭, 0);
            };
            Controls.Add(바닥);

            summaryLabel = 라벨("", 12, false, Ui.흐린글씨);
            summaryLabel.BackColor = Ui.양피지;
            summaryLabel.Location = new Point(16, 11);
            바닥.Controls.Add(summaryLabel);

            밀린건 = 라벨("", 12, true, Ui.위험);
            밀린건.BackColor = Ui.양피지;
            밀린건.Cursor = Cursors.Hand;
            밀린건.Visible = this.overdue.Count > 0;
            밀린건.Click += delegate
            {
                MessageBox.Show(this, BuildOverdueText(this.overdue), "기한이 지난 미처리 건", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            바닥.Controls.Add(밀린건);

            closeButton = 알약("닫기", true, 12);
            closeButton.Size = new Size(60, 26);
            closeButton.Location = new Point(폭 - 16 - 60, 6);
            closeButton.Click += delegate { TryClose(); };
            바닥.Controls.Add(closeButton);

            넘김타이머 = new Timer();
            넘김타이머.Interval = 자동넘김ms;
            넘김타이머.Tick += delegate
            {
                넘김타이머.Stop();
                int n = 다음남은건(index);
                if (n >= 0) 이동(n);
            };

            // 첫 장은 아직 안 고른 건 중 기한이 가장 이른 건. rows 는 이미 기한 순이다.
            int first = 다음남은건(-1);
            index = first >= 0 ? first : 0;
            RefreshState();
        }

        // ══ 창 모양 ══════════════════════════════════════════════

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW — 제목줄 없는 창에도 그림자
                return cp;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11: 시스템 둥근 모서리(테두리·그림자 포함). 안 되면 창 모양을 직접 깎는다 (Windows 10).
            bool 둥근모서리 = false;
            try
            {
                int round = 2;   // DWMWCP_ROUND
                둥근모서리 = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)) == 0 && Environment.OSVersion.Version.Build >= 22000;
            }
            catch { }
            시스템둥근모서리 = 둥근모서리;
            모양맞추기();
        }

        bool 시스템둥근모서리;
        readonly PillButton 접기버튼;
        readonly PillButton 설정버튼;
        readonly ToolTip 접힌풍선;

        /// <summary>설정(⚙) 버튼을 누르면 부른다. 웹 화면을 여는 일은 Program 이 맡는다.</summary>
        public Action 웹열기;

        /// <summary>설정 버튼에 적힌 글자 (시험에서 버튼을 찾을 때 쓴다).</summary>
        public string 설정버튼문구 { get { return 설정버튼.Text; } }

        void 모양맞추기()
        {
            if (시스템둥근모서리 || !IsHandleCreated) return;
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 24, 24));
        }

        // 창을 띄우기 전에는 자식 Visible 이 늘 false 라 따로 든다 (시험용).
        bool 부가보임값 = true;

        /// <summary>머리의 날짜·경고가 보이는지. 접으면 false — 제목·건수와 설정·펼치기 버튼만 남는다.</summary>
        public bool 머리부가보임 { get { return 부가보임값; } }

        /// <summary>제목 줄만 남기고 접었는지.</summary>
        public bool 접힘 { get; private set; }

        // ── 접기 애니메이션 ── 높이를 ease-out 곡선으로 바꾼다. 아래 끝은 작업 표시줄에 붙은 채.
        public const int 접기ms = 200;
        Timer 접기타이머;
        System.Diagnostics.Stopwatch 접기시계;
        int 시작높이, 목표높이;

        /// <summary>애니메이션이 도는 중인지.</summary>
        public bool 애니중 { get { return 접기타이머 != null && 접기타이머.Enabled; } }

        /// <summary>
        /// 접거나 펼친다. 아래 끝은 작업 표시줄에 붙은 채로 두고 높이만 바꾼다 — 자리 고정 규칙 그대로.
        /// 접어도 닫힌 것이 아니다. 고르지 않은 건이 남아 있으면 계속 떠 있는다.
        /// 창이 보이고 Windows 의 화면 효과가 켜져 있으면 부드럽게 움직인다.
        /// </summary>
        public void 접기(bool 접을지)
        {
            접기(접을지, Visible && IsHandleCreated && SystemInformation.UIEffectsEnabled);
        }

        public void 접기(bool 접을지, bool 움직임)
        {
            if (등장중) 등장단계(1);   // 떠오르는 도중이면 먼저 제자리에 놓고 접는다 (아래 끝 기준이 흔들리지 않게)
            접힘 = 접을지;
            접기버튼.Text = 접을지 ? "▴" : "▾";
            // 접으면 '기한 알림 N건' 과 설정·펼치기 버튼만 남긴다 (두 버튼은 숨기지 않는다, 사용자 요청 2026-09-16). 줄 빈 곳이나 제목을 눌러도 펼쳐진다.
            부가보임값 = !접을지;
            날짜.Visible = !접을지;
            경고.Visible = !접을지 && !string.IsNullOrEmpty(warningText);
            Cursor = 접을지 ? Cursors.Hand : Cursors.Default;
            제목.Cursor = Cursor;
            건수.Cursor = Cursor;
            접힌풍선.SetToolTip(제목, 접을지 ? "눌러서 펼치기" : null);
            접힌풍선.SetToolTip(건수, 접을지 ? "눌러서 펼치기" : null);

            시작높이 = Height;
            목표높이 = 접을지 ? 접힌높이 : 높이;
            if (!움직임 || 시작높이 == 목표높이)
            {
                if (접기타이머 != null) 접기타이머.Stop();
                높이적용(목표높이);
                return;
            }
            if (접기타이머 == null)
            {
                접기타이머 = new Timer();
                접기타이머.Interval = 10;
                접기타이머.Tick += delegate { 애니단계(접기시계.ElapsedMilliseconds / (double)접기ms); };
            }
            // 도는 중에 다시 누르면 지금 높이에서 반대로 되돌아간다.
            접기시계 = System.Diagnostics.Stopwatch.StartNew();
            접기타이머.Start();
        }

        /// <summary>애니메이션의 t(0~1) 지점으로 높이를 맞춘다. 1 이상이면 끝낸다 (시험에서도 부른다).</summary>
        public void 애니단계(double t)
        {
            double e = 부드럽게(t);
            높이적용((int)Math.Round(시작높이 + (목표높이 - 시작높이) * e));
            if (t >= 1 && 접기타이머 != null) 접기타이머.Stop();
        }

        // ── 등장 애니메이션 ── 처음 뜰 때 작업 표시줄 쪽에서 16px 떠오르며 투명→불투명 (ADR-0015 3-6).
        public const int 등장ms = 220;
        public const int 등장거리 = 16;
        Timer 등장타이머;
        System.Diagnostics.Stopwatch 등장시계;
        int 등장최종Top;
        bool 등장중값;

        /// <summary>창을 띄울 때 등장 애니메이션을 쓸지. Windows '애니메이션 효과' 가 꺼져 있으면 쓰지 않는다.</summary>
        public bool 등장사용 = true;

        public bool 등장중 { get { return 등장중값; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 창이 화면에 그려지기 전(OnLoad)에 투명·아래로 옮겨 두어야 제자리에서 한 번 번쩍이지 않는다.
            if (등장사용 && SystemInformation.UIEffectsEnabled) 등장시작();
        }

        /// <summary>등장 애니메이션을 시작한다. 지금 자리가 최종 자리다 (시험에서도 부른다).</summary>
        public void 등장시작()
        {
            // 이미 떠오르는 중이면 지금 Top 은 중간 위치다 — 처음 기억한 최종 자리를 그대로 쓴다.
            if (!등장중값) 등장최종Top = Top;
            등장중값 = true;
            Opacity = 0;
            Top = 등장최종Top + 등장거리;
            if (등장타이머 == null)
            {
                등장타이머 = new Timer();
                등장타이머.Interval = 10;
                등장타이머.Tick += delegate { 등장단계(등장시계.ElapsedMilliseconds / (double)등장ms); };
            }
            등장시계 = System.Diagnostics.Stopwatch.StartNew();
            등장타이머.Start();
        }

        /// <summary>등장 애니메이션의 t(0~1) 지점. 1 이상이면 제자리·불투명으로 끝낸다.</summary>
        public void 등장단계(double t)
        {
            if (!등장중값) return;
            double e = 부드럽게(t);
            if (t >= 1)
            {
                if (등장타이머 != null) 등장타이머.Stop();
                등장중값 = false;
                Top = 등장최종Top;
                Opacity = 1;
                return;
            }
            Opacity = e;
            Top = 등장최종Top + (int)Math.Round(등장거리 * (1 - e));
        }

        /// <summary>ease-out cubic — 처음엔 빠르고 끝에서 천천히 멈춘다.</summary>
        public static double 부드럽게(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t;
            return 1 - u * u * u;
        }

        void 높이적용(int h)
        {
            if (h == Height) return;
            int bottom = Bounds.Bottom;
            Bounds = new Rectangle(Left, bottom - h, 폭, h);
            모양맞추기();
            // 커지면서 새로 드러난 카드 영역이 그려지기 전에 검게 비치지 않도록, 자식까지 지금 바로 다시 그린다.
            if (IsHandleCreated)
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }

        const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;

        [DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Ui.테두리)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        // ══ 공개 동작 (시험에서도 쓴다) ═════════════════════════

        public int 현재번호 { get { return index; } }
        public int 건수값 { get { return rows.Count; } }

        /// <summary>i 번째 건으로 넘긴다. 범위 밖이면 가장자리에 멈춘다.</summary>
        public void 이동(int i)
        {
            if (rows.Count == 0) return;
            index = Math.Max(0, Math.Min(rows.Count - 1, i));
            RefreshState();
        }

        /// <summary>from 다음부터 한 바퀴 돌며 아직 안 고른 건을 찾는다. 없으면 -1.</summary>
        public int 다음남은건(int from)
        {
            for (int k = 1; k <= rows.Count; k++)
            {
                int j = ((from + k) % rows.Count + rows.Count) % rows.Count;
                if (!rows[j].오늘처리됨(today)) return j;
            }
            return -1;
        }

        // 창을 띄우기 전에는 자식 컨트롤의 Visible 이 늘 false 라, 보일지 여부를 따로 들고 있는다 (시험용).
        bool 진행보임;
        bool 점보임값;

        public string 진행버튼문구 { get { return 진행보임 ? 진행.Text : null; } }
        public bool 점보임 { get { return 점보임값; } }

        /// <summary>단계 진행/대기/되돌리기 후 화면 전체를 다시 그린다.</summary>
        public void RefreshState()
        {
            int 남음 = 0;
            foreach (AlertRow r in rows) if (!r.오늘처리됨(today)) 남음++;

            건수.Text = rows.Count + "건";
            건수.Location = new Point(제목.Right, 13);
            날짜.Location = new Point(설정버튼.Left - 8 - 날짜.PreferredWidth, 17);
            경고.Location = new Point(날짜.Left - 8 - 경고.PreferredWidth, 17);
            경고.BringToFront();   // 건수 라벨과 겹쳐도 ⚠ 가 가려지지 않게

            카드그리기();

            점.설정(rows.Count, index);
            점.Location = new Point((폭 - 점.Width) / 2, 카드.Bottom + 6);
            이전.Location = new Point(점.Left - 30, 카드.Bottom + 4);
            다음.Location = new Point(점.Right + 4, 카드.Bottom + 4);
            Ui.버튼활성(이전, index > 0);
            Ui.버튼활성(다음, index < rows.Count - 1);
            bool 여러건 = rows.Count > 1;
            점보임값 = 여러건;
            점.Visible = 여러건; 이전.Visible = 여러건; 다음.Visible = 여러건;

            summaryLabel.Text = 남음 == 0 ? "모든 건을 확인했습니다" : string.Format("남은 {0}건 · 모두 고르면 닫힘", 남음);
            밀린건.Text = this.overdue.Count > 0 ? string.Format("밀린 {0}건", this.overdue.Count) : "";
            밀린건.Location = new Point(summaryLabel.Right + 6, 11);
            Ui.버튼활성(closeButton, 남음 == 0);
        }

        void 카드그리기()
        {
            if (rows.Count == 0) return;
            AlertRow row = 현재행();
            PaymentItem it = row.Occ.Item;
            int 남은 = row.Occ.남은영업일(cal, today);
            bool done = row.최종단계도달;
            bool handled = row.오늘처리됨(today);
            bool 지남 = 남은 < 0;

            string st;
            if (done) st = "처리 완료";
            else if (남은 > 0) st = "D-" + 남은 + "영업일";
            else if (남은 == 0) st = "오늘이 기한";
            else st = "기한 " + (-남은) + "영업일 지남";
            상태.Text = st;
            상태.ForeColor = handled ? Ui.흐린글씨 : (지남 ? Ui.위험 : Ui.강조);

            기관.Text = it.기관;
            기관.Location = new Point(카드폭 - 12 - 기관.PreferredWidth, 10);

            금액.Text = 금액표시(row.Occ);
            금액.ForeColor = AmountRules.금액(row.Occ).HasValue ? Ui.잉크 : Ui.강조;
            금액.Location = new Point(카드폭 - 12 - 금액.PreferredWidth, 31);

            이름.Text = it.비용명;
            기한.Text = row.Occ.보정기한일.ToString("MM-dd (ddd)", new CultureInfo("ko-KR"));
            int 이름폭 = Math.Min(TextRenderer.MeasureText(it.비용명, 이름.Font).Width + 2, 금액.Left - 12 - 12 - 기한.PreferredWidth - 6);
            이름.Width = Math.Max(60, 이름폭);
            기한.Location = new Point(이름.Right + 4, 35);

            // 끝난 단계는 회색, 지금 할 단계는 파랑. 끝난 건은 전부 회색 (ADR-0005).
            단계점.설정(Stages.For(it), row.Status.단계 + 1, done);

            카드.띠색 = handled ? Ui.테두리 : (지남 ? Ui.위험 : Ui.강조);
            카드.테두리색 = handled ? Ui.연한선 : Ui.테두리;

            // ── 버튼: 아직 안 골랐으면 [행동] [오늘은 대기] [⋯], 골랐으면 [처리 표시] [되돌리기] [⋯] ──
            진행보임 = !handled && !done;
            진행.Visible = 진행보임;
            대기.Visible = 진행보임;
            처리표시.Visible = handled || done;
            되돌리기.Visible = (handled || done) && row.Status.단계 > 0;

            if (진행보임)
            {
                진행.Text = row.다음행동 ?? "";
                진행.Width = Math.Max(76, TextRenderer.MeasureText(진행.Text, 진행.Font).Width + 26);
                진행.Height = 28;
                대기.Size = new Size(TextRenderer.MeasureText(대기.Text, 대기.Font).Width + 24, 28);
                대기.Location = new Point(진행.Right + 5, 106);
            }
            if (처리표시.Visible)
            {
                bool 대기함 = !row.오늘단계변경 && !done;
                처리표시.Text = done ? "모두 끝남" : (대기함 ? "내일 다시 알림" : "처리함");
                처리표시.Location = new Point(12, 110);
            }
            더보기.Location = new Point(카드폭 - 12 - 더보기.Width, 106);
            if (되돌리기.Visible)
            {
                되돌리기.Size = new Size(TextRenderer.MeasureText(되돌리기.Text, 되돌리기.Font).Width + 24, 28);
                되돌리기.Location = new Point(더보기.Left - 5 - 되돌리기.Width, 106);
            }
        }

        AlertRow 현재행() { return rows[Math.Max(0, Math.Min(index, rows.Count - 1))]; }

        // ══ 동작 ═════════════════════════════════════════════════

        void 적용후넘김(AlertRow row, string 동작)
        {
            if (동작 == "진행" && row.최종단계도달) return;
            if (!적용(row, 동작)) return;
            if (동작 == "진행") row.오늘단계변경 = true;
            RefreshState();
            // 고른 건은 잠깐 보여 준 뒤 다음 남은 건으로 넘긴다 (시안 v4, 사용자 확정 2026-09-15).
            if (다음남은건(index) >= 0) { 넘김타이머.Stop(); 넘김타이머.Start(); }
        }

        void 되돌리기누름()
        {
            AlertRow row = 현재행();
            if (row.Status.단계 <= 0) return;
            string[] names = row.단계목록;
            string 확인 = string.Format("{0} ({1})\r\n\r\n'{2}' 를 취소하고 '{3}' 까지 끝난 것으로 되돌릴까요?",
                row.Occ.Item.비용명, row.Occ.Item.기관, names[row.Status.단계], names[row.Status.단계 - 1]);
            if (MessageBox.Show(this, 확인, "되돌리기", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (!적용(row, "되돌리기")) return;
            row.오늘단계변경 = false;
            RefreshState();
        }

        /// <summary>동작을 적용한다. 다른 창이 먼저 바꿨으면 알리고 최신 단계로 맞춘 뒤 false.</summary>
        bool 적용(AlertRow row, string 동작)
        {
            if (단계변경 == null)
            {
                if (동작 == "진행") { row.Status.단계++; row.Status.변경일시 = DateTime.Now; row.Status.최종확인일 = today; }
                else if (동작 == "대기") row.Status.최종확인일 = today;
                else { row.Status.단계--; row.Status.변경일시 = DateTime.Now; row.Status.최종확인일 = null; }
                return true;
            }

            try
            {
                StatusRecord st = 단계변경(row, 동작);
                row.Status.단계 = st.단계;
                row.Status.변경일시 = st.변경일시;
                row.Status.최종확인일 = st.최종확인일;
                return true;
            }
            catch (StageConflictException ce)
            {
                row.Status.단계 = ce.현재단계;
                MessageBox.Show(this, ce.Message, "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshState();
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "저장하지 못했습니다. 다시 눌러 주세요.\r\n\r\n" + ex.Message,
                    "납부 기한 알림 - 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void 메뉴채우기()
        {
            메뉴.Items.Clear();
            AlertRow row = 현재행();
            int n = store.CountFor(row.Occ.연도, row.Occ.Item.Id);
            메뉴.Items.Add("증빙 첨부…", null, delegate { AttachFile(row); });
            var 열기 = new ToolStripMenuItem(n > 0 ? string.Format("증빙 폴더 열기 ({0}건)", n) : "증빙 폴더 열기");
            열기.Click += delegate { OpenFolder(row); };
            메뉴.Items.Add(열기);
            var 되돌 = new ToolStripMenuItem("한 단계 되돌리기");
            되돌.Enabled = row.Status.단계 > 0;
            되돌.Click += delegate { 되돌리기누름(); };
            메뉴.Items.Add(되돌);
            if (!string.IsNullOrEmpty(row.Occ.Item.홈페이지주소))
            {
                메뉴.Items.Add(new ToolStripSeparator());
                메뉴.Items.Add((string.IsNullOrEmpty(row.Occ.Item.홈페이지명) ? "홈페이지" : row.Occ.Item.홈페이지명) + " 열기", null,
                    delegate { 열기시도(row.Occ.Item.홈페이지주소); });
            }
            string port = Path.Combine(Path.GetTempPath(), "PaymentAlert.web-port");
            if (File.Exists(port))
            {
                메뉴.Items.Add(new ToolStripSeparator());
                메뉴.Items.Add("웹 화면에서 보기", null, delegate
                {
                    try { 열기시도("http://localhost:" + File.ReadAllText(port).Trim() + "/#alerts"); } catch { }
                });
            }
            Ui.메뉴항목정리(메뉴);
        }

        /// <summary>⋯ 버튼의 메뉴 (시험용).</summary>
        public ContextMenuStrip 더보기메뉴 { get { return 메뉴; } }

        void 열기시도(string target)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(target);
                psi.UseShellExecute = true;
                using (System.Diagnostics.Process.Start(psi)) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "열지 못했습니다.\r\n" + ex.Message, "납부 기한 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>증빙 파일을 골라 보관소에 복사한다. 지금 지점으로 기록된다.</summary>
        void AttachFile(AlertRow row)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = row.Occ.Item.표시명 + " — 증빙 첨부 (" + row.현재단계명 + ")";
                dlg.Filter = "증빙 파일 (*.pdf;*.jpg;*.png;*.xlsx;*.hwp;*.docx)" +
                             "|*.pdf;*.jpg;*.jpeg;*.png;*.xlsx;*.xls;*.hwp;*.hwpx;*.docx|모든 파일 (*.*)|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var failed = new List<string>();
                foreach (string f in dlg.FileNames)
                {
                    try { store.Attach(row.Occ.연도, row.Occ.Item.Id, row.현재단계명, f, Attachment.증빙, "팝업"); }
                    catch (Exception ex) { failed.Add(Path.GetFileName(f) + " — " + ex.Message); }
                }
                if (failed.Count > 0)
                    MessageBox.Show(this, string.Format("{0}건은 첨부하지 못했습니다.\r\n\r\n{1}", failed.Count, string.Join("\r\n", failed.ToArray())),
                        "증빙 첨부", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshState();
            }
        }

        void OpenFolder(AlertRow row)
        {
            string folder = store.FolderFor(row.Occ.연도, row.Occ.Item.Id);
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "폴더를 열지 못했습니다.\r\n" + folder + "\r\n\r\n" + ex.Message,
                    "증빙", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ══ 끌어 넘기기·키보드·닫기 ══════════════════════════════

        int 끌기시작 = int.MinValue;

        void 끌기연결(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) 끌기시작 = Cursor.Position.X; };
            c.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (끌기시작 == int.MinValue) return;
                int dx = Cursor.Position.X - 끌기시작;
                끌기시작 = int.MinValue;
                if (dx <= -40) 이동(index + 1);
                else if (dx >= 40) 이동(index - 1);
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Right) { 이동(index + 1); return true; }
            if (keyData == Keys.Left) { 이동(index - 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4)) return true;
            if (keyData == Keys.Escape) return true;
            return base.ProcessDialogKey(keyData);
        }

        void TryClose()
        {
            foreach (AlertRow r in rows)
                if (!r.오늘처리됨(today)) return;
            allowClose = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 사용자가 닫으려는 경우에만 막는다. 시스템 종료는 막지 않는다.
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                넘김타이머.Dispose();
                if (접기타이머 != null) 접기타이머.Dispose();
                if (등장타이머 != null) 등장타이머.Dispose();
                메뉴.Dispose();
            }
            base.Dispose(disposing);
        }

        // ══ 도우미 ═══════════════════════════════════════════════

        static Label 라벨(string text, float px, bool bold, Color color)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Ui.글꼴(px, bold);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.UseMnemonic = false;
            return l;
        }

        static PillButton 알약(string text, bool 주요, float px)
        {
            var b = new PillButton();
            b.Text = text;
            Ui.알약(b, 주요);
            b.Font = Ui.글꼴(px, true);
            b.Size = new Size(80, 28);
            return b;
        }

        static string 금액표시(Occurrence occ)
        {
            decimal? a = AmountRules.금액(occ);
            if (a.HasValue) return string.Format("{0:N0}원", a.Value);
            return AmountRules.미확인(occ) ? "금액 미확인" : "";
        }

        public static string BuildOverdueText(List<AlertRow> overdue)
        {
            var names = new List<string>();
            int shown = 0;
            foreach (AlertRow r in overdue)
            {
                if (shown >= 5) break;
                names.Add("· " + r.Occ.Item.비용명 + " (" + r.Occ.보정기한일.ToString("yy-MM-dd") + ", " + r.현재단계명 + ")");
                shown++;
            }
            string tail = overdue.Count > shown ? string.Format("\r\n외 {0}건", overdue.Count - shown) : "";
            return string.Format(
                "기한이 지나고 5영업일이 넘은 미처리 건이 {0}건 있습니다.\r\n\r\n{1}{2}\r\n\r\n" +
                "매일 묻지는 않지만 끝내야 조용해집니다. 웹 화면 '받은 알림' 에서 처리할 수 있습니다.",
                overdue.Count, string.Join("\r\n", names.ToArray()), tail);
        }
    }

    /// <summary>
    /// 넘기기 점 (시안 v4). 지금 보는 건 = 길쭉한 파랑(기한 지남이면 빨강), 아직 안 고른 건 = 빈 점,
    /// 기한 지난 건 = 꽉 찬 빨강, 고른 건 = 꽉 찬 회색. 12건을 넘으면 '3 / 15' 로 적는다.
    /// </summary>
    class PageDots : Control
    {
        const int 지름 = 8, 긴폭 = 20, 간격 = 6;
        int count, current;
        public Func<int, bool> 처리됨 = delegate { return false; };
        public Func<int, bool> 지남 = delegate { return false; };
        public event Action<int> 눌림;

        public PageDots()
        {
            Height = 12;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public bool 글자로 { get { return count > 12; } }

        public void 설정(int count, int current)
        {
            this.count = count;
            this.current = current;
            Width = 글자로 ? 60 : Math.Max(긴폭, count * 지름 + Math.Max(0, count - 1) * 간격 + (긴폭 - 지름));
            Invalidate();
        }

        /// <summary>i 번째 점의 상태 이름. 시험에서 쓴다.</summary>
        public string 상태(int i)
        {
            if (i == current) return 지남(i) && !처리됨(i) ? "현재-지남" : "현재";
            if (처리됨(i)) return "처리됨";
            return 지남(i) ? "지남" : "남음";
        }

        int X(int i) { return i * (지름 + 간격) + (i > current ? 긴폭 - 지름 : 0); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (count == 0) return;
            if (글자로)
            {
                TextRenderer.DrawText(g, string.Format("{0} / {1}", current + 1, count), Ui.글꼴(11, true), ClientRectangle, Ui.흐린글씨,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int y = (Height - 지름) / 2;
            for (int i = 0; i < count; i++)
            {
                string s = 상태(i);
                int w = i == current ? 긴폭 : 지름;
                var r = new RectangleF(X(i) + 0.5f, y + 0.5f, w - 1, 지름 - 1);
                using (var path = 알약(r))
                {
                    Color fill, line;
                    if (s == "현재") { fill = Ui.강조; line = Ui.강조; }
                    else if (s == "현재-지남" || s == "지남") { fill = Ui.위험; line = Ui.위험; }
                    else if (s == "처리됨") { fill = Ui.아주흐림; line = Ui.아주흐림; }
                    else { fill = Ui.캔버스; line = Color.FromArgb(0xc7, 0xc7, 0xcc); }
                    using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                    using (var pen = new Pen(line, 1.5f)) g.DrawPath(pen, path);
                }
            }
        }

        static GraphicsPath 알약(RectangleF r)
        {
            var p = new GraphicsPath();
            float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (글자로 || 눌림 == null) return;
            for (int i = 0; i < count; i++)
            {
                int w = i == current ? 긴폭 : 지름;
                if (e.X >= X(i) - 3 && e.X <= X(i) + w + 3) { 눌림(i); return; }
            }
        }
    }
}
