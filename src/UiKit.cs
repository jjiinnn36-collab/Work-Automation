using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaymentAlert
{
    /// <summary>
    /// 팝업 화면의 색·글꼴·부품. 값은 웹 시안(mockup-web.html)의 토큰을 그대로 옮긴 것이다.
    /// 두 화면이 같은 값을 쓰지 않으면 같은 제품으로 보이지 않는다.
    /// </summary>
    static class Ui
    {
        // ── 색 ── 웹 화면(shadcn/ui neutral 테마, ADR-0007)의 토큰을 sRGB 로 옮긴 값. 두 화면이 같은 제품으로 보이게 한다.
        public static readonly Color 캔버스   = Color.FromArgb(0xff, 0xff, 0xff);  // --background
        public static readonly Color 양피지   = Color.FromArgb(0xf5, 0xf5, 0xf5);  // --muted
        public static readonly Color 펄       = Color.FromArgb(0xfa, 0xfa, 0xfa);  // --sidebar
        public static readonly Color 테두리   = Color.FromArgb(0xe5, 0xe5, 0xe5);  // --border
        public static readonly Color 연한선   = Color.FromArgb(0xf0, 0xf0, 0xf0);
        public static readonly Color 잉크     = Color.FromArgb(0x0a, 0x0a, 0x0a);  // --foreground
        public static readonly Color 흐린글씨 = Color.FromArgb(0x73, 0x73, 0x73);  // --muted-foreground
        public static readonly Color 아주흐림 = Color.FromArgb(0xa1, 0xa1, 0xa1);
        public static readonly Color 주요     = Color.FromArgb(0x17, 0x17, 0x17);  // --primary
        public static readonly Color 강조     = Color.FromArgb(0x15, 0x5d, 0xfc);  // --action (지금 할 일)
        public static readonly Color 강조올림 = Color.FromArgb(0x14, 0x47, 0xe6);  // 주요 버튼 마우스 올림
        public static readonly Color 강조눌림 = Color.FromArgb(0x19, 0x3c, 0xb8);  // 주요 버튼 누름
        public static readonly Color 초점테   = Color.FromArgb(0x8e, 0xc5, 0xff);  // --ring (키보드 초점)
        public static readonly Color 위험     = Color.FromArgb(0xe7, 0x00, 0x0b);  // --destructive
        public static readonly Color 경고글씨 = Color.FromArgb(0x96, 0x5a, 0x00);

        // ── 글꼴 ──
        // 웹 시안은 Pretendard 를 쓴다. 깔려 있으면 그것을 쓰고, 없으면 맑은 고딕으로 떨어진다.
        // 웹은 무게 400/600 을 쓰지만 맑은 고딕에는 중간 무게가 없어 Regular/Bold 로 맞춘다.
        static string 글꼴이름;
        static string 글꼴찾기()
        {
            if (글꼴이름 != null) return 글꼴이름;
            string[] 후보 = { "Pretendard Variable", "Pretendard", "맑은 고딕", "Malgun Gothic" };
            using (var 설치됨 = new InstalledFontCollection())
            {
                foreach (string 이름 in 후보)
                    foreach (FontFamily f in 설치됨.Families)
                        if (string.Equals(f.Name, 이름, StringComparison.OrdinalIgnoreCase))
                        { 글꼴이름 = 이름; return 글꼴이름; }
            }
            글꼴이름 = "맑은 고딕";
            return 글꼴이름;
        }

        /// <summary>웹 시안은 px 로 적혀 있다. WinForms 는 pt 를 쓰므로 96dpi 기준으로 바꾼다.</summary>
        /// <summary>이 PC 에 그 이름의 글꼴이 설치돼 있는지.</summary>
        public static bool 글꼴있음(string 이름)
        {
            using (var 설치됨 = new InstalledFontCollection())
                foreach (FontFamily f in 설치됨.Families)
                    if (string.Equals(f.Name, 이름, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Font 글꼴(float px, bool 굵게)
        {
            return new Font(글꼴찾기(), px * 0.75f,
                            굵게 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }
        public static Font 글꼴(float px) { return 글꼴(px, false); }

        // ── 알약 버튼 ──

        // ── 알약 버튼 ──
        // 그리기는 PillButton 이 직접 한다. 여기서는 역할과 글꼴만 정한다.
        public static void 알약(PillButton b, bool 주요)
        {
            b.주요 = 주요;
            b.Font = 글꼴(14, true);
            b.ForeColor = 주요 ? 캔버스 : 잉크;
        }

        // ── 컨텍스트 메뉴 ──
        // Windows 기본 메뉴는 각진 사각 테두리·회색 아이콘 여백 띠라 둥근 팝업과 따로 논다.
        // 흰 바탕·연한 테두리·둥근 선택 강조로 직접 그리고, Windows 11 에서는 창 모서리도 둥글게 한다.

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void 메뉴꾸미기(ContextMenuStrip m)
        {
            var 그리기 = new 메뉴그리기();
            m.Renderer = 그리기;
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.BackColor = 캔버스;
            m.ForeColor = 잉크;
            m.Padding = new Padding(2, 4, 2, 4);
            m.HandleCreated += delegate
            {
                try
                {
                    int small = 3;   // DWMWCP_ROUNDSMALL
                    그리기.시스템테두리 = Environment.OSVersion.Version.Build >= 22000 &&
                        DwmSetWindowAttribute(m.Handle, 33, ref small, sizeof(int)) == 0;
                }
                catch { }
            };
        }

        /// <summary>메뉴 항목을 채운 뒤 부른다 — 항목 높이를 넉넉하게.</summary>
        public static void 메뉴항목정리(ContextMenuStrip m)
        {
            foreach (ToolStripItem i in m.Items)
                if (i is ToolStripMenuItem) i.Padding = new Padding(6, 5, 12, 5);
        }

        /// <summary>PillButton 은 Enabled 를 보고 스스로 흐리게 그린다.</summary>
        public static void 버튼활성(Button b, bool 켜짐)
        {
            b.Enabled = 켜짐;
        }
    }

    /// <summary>
    /// 1px 테두리와 둥근 모서리를 가진 카드. 왼쪽에 심각도 띠를 그릴 수 있다.
    /// WinForms 의 BorderStyle.FixedSingle 은 색을 지정할 수 없어 직접 그린다.
    /// </summary>
    class CardPanel : Panel
    {
        Color _테두리색 = Ui.테두리;
        public Color 테두리색 { get { return _테두리색; } set { _테두리색 = value; Invalidate(); } }
        Color _띠색 = Color.Empty;   // Empty 면 띠를 그리지 않는다
        public Color 띠색 { get { return _띠색; } set { _띠색 = value; Invalidate(); } }
        public int 반경 = 11;              // 웹 시안의 --r-md
        public int 띠폭 = 3;

        public CardPanel()
        {
            BackColor = Ui.캔버스;
            // 배경을 직접 칠하므로 깜빡임을 막는다.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var 바깥 = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = 둥근사각(바깥, 반경))
            {
                using (var br = new SolidBrush(BackColor)) g.FillPath(br, p);

                if (_띠색 != Color.Empty)
                {
                    // 띠는 카드 왼쪽 안쪽에 붙는다. 카드 모양으로 잘라 모서리를 넘지 않게 한다.
                    Region 이전 = g.Clip;
                    g.SetClip(p);
                    using (var br = new SolidBrush(_띠색))
                        g.FillRectangle(br, 0, 0, 띠폭, Height);
                    g.Clip = 이전;
                }

                using (var pen = new Pen(_테두리색, 1f)) g.DrawPath(pen, p);
            }
            base.OnPaint(e);
        }

        static GraphicsPath 둥근사각(Rectangle r, int 반경)
        {
            var p = new GraphicsPath();
            if (반경 <= 0) { p.AddRectangle(r); return p; }
            int d = 반경 * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>
    /// 팝업 바닥의 "다음 · 이름 · 기한" 한 줄 (ADR-0017). 줄 전체를 누를 수 있고, 올리면 옅은 바탕.
    /// 앞말·뒷말은 흐리게, 이름은 굵게 — 한 Label 로는 섞을 수 없어 직접 그린다.
    /// </summary>
    class NextLine : Control
    {
        string 앞, 이름, 뒤;
        bool 올림;
        static readonly Font 보통글꼴 = Ui.글꼴(12);
        static readonly Font 굵은글꼴 = Ui.글꼴(12, true);

        public NextLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            앞 = 이름 = 뒤 = "";
        }

        public void 설정(string 앞말, string 이름말, string 뒷말)
        {
            앞 = 앞말 ?? ""; 이름 = 이름말 ?? ""; 뒤 = 뒷말 ?? "";
            Invalidate();
        }

        public string 문구 { get { return (앞 + (앞.Length > 0 && 이름.Length > 0 ? " " : "") + 이름 + 뒤).Trim(); } }

        protected override void OnMouseEnter(EventArgs e) { 올림 = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { 올림 = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (올림 && Enabled)
                using (var br = new SolidBrush(Ui.양피지))
                using (var p = new GraphicsPath())
                {
                    var r = new Rectangle(0, 2, Width - 1, Height - 5);
                    int d = 8;
                    p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    p.CloseFigure();
                    g.FillPath(br, p);
                }
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            int x = 6;
            Color 흐림 = Enabled ? Ui.흐린글씨 : Ui.아주흐림;
            if (앞.Length > 0)
            {
                TextRenderer.DrawText(g, 앞, 보통글꼴, new Rectangle(x, 0, Width - x, Height), 흐림, f);
                x += TextRenderer.MeasureText(g, 앞, 보통글꼴, Size.Empty, TextFormatFlags.NoPadding).Width + 5;
            }
            if (이름.Length > 0)
            {
                int 뒤폭 = TextRenderer.MeasureText(g, 뒤, 보통글꼴, Size.Empty, TextFormatFlags.NoPadding).Width;
                int 이름폭 = Math.Min(TextRenderer.MeasureText(g, 이름, 굵은글꼴, Size.Empty, TextFormatFlags.NoPadding).Width,
                                     Math.Max(20, Width - x - 뒤폭 - 4));
                TextRenderer.DrawText(g, 이름, 굵은글꼴, new Rectangle(x, 0, 이름폭, Height), Ui.잉크, f);
                x += 이름폭;
            }
            if (뒤.Length > 0)
                TextRenderer.DrawText(g, 뒤, 보통글꼴, new Rectangle(x, 0, Math.Max(0, Width - x), Height), 흐림, f);
        }
    }

    /// <summary>
    /// 건을 넘길 때 카드가 밀려나고 들어오는 움직임 (ADR-0017).
    /// 카드 모습을 그림으로 떠서 덮어 그린다 — WinForms 컨트롤을 직접 옮기면 깜빡이고 느리다.
    /// 위치 = 지금 카드의 x. 이전 카드는 위치-폭, 다음 카드는 위치+폭에 그린다.
    /// </summary>
    class SlideOverlay : Control
    {
        Bitmap 이전, 지금, 다음;
        double 시작, 목표;
        System.Diagnostics.Stopwatch 시계;
        readonly Timer 타이머;
        int 결과;
        Action<int> 끝;
        double 마지막dx, 속도;
        long 마지막때;

        public const int 기본ms = 220;
        public const int 넘김거리 = 80;       // 이만큼 끌면 넘어간다
        public const double 튕김속도 = 0.5;   // px/ms — 이보다 빠르게 놓으면 넘어간다
        int 걸리는ms = 기본ms;

        public double 위치 { get; private set; }
        public bool 도는중 { get { return 타이머.Enabled; } }
        public bool 끄는중 { get; private set; }

        public SlideOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Visible = false;
            타이머 = new Timer();
            타이머.Interval = 10;
            타이머.Tick += delegate { 단계(시계.ElapsedMilliseconds / (double)걸리는ms); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { 타이머.Dispose(); 그림비우기(); }
            base.Dispose(disposing);
        }

        void 그림비우기()
        {
            if (이전 != null) 이전.Dispose();
            if (지금 != null) 지금.Dispose();
            if (다음 != null) 다음.Dispose();
            이전 = 지금 = 다음 = null;
        }

        /// <summary>옛 카드에서 새 카드로 민다. 방향 +1 = 새 카드가 오른쪽에서, -1 = 왼쪽에서.</summary>
        public void 전환(Bitmap 옛, Bitmap 새, int 방향, Action<int> 끝나면)
        {
            그림비우기();
            지금 = 옛;
            if (방향 > 0) 다음 = 새; else 이전 = 새;
            위치 = 0;
            결과 = 방향 > 0 ? 1 : -1;
            끝 = 끝나면;
            움직이기(방향 > 0 ? -Width : Width);
        }

        /// <summary>끌기 시작: 이전·다음 카드가 없으면 null (끝에서는 버틴다).</summary>
        public void 끌기시작(Bitmap 앞카드, Bitmap 이카드, Bitmap 뒷카드)
        {
            타이머.Stop();
            그림비우기();
            이전 = 앞카드; 지금 = 이카드; 다음 = 뒷카드;
            위치 = 0; 속도 = 0; 마지막dx = 0; 마지막때 = Environment.TickCount;
            끄는중 = true;
            Visible = true;
            BringToFront();
            Invalidate();
        }

        /// <summary>누른 곳에서 dx 만큼 끌었다. 넘어갈 카드가 없는 쪽이면 1/3 만 따라온다.</summary>
        public void 끌기(double dx)
        {
            long now = Environment.TickCount;
            long dt = Math.Max(1, now - 마지막때);
            속도 = (dx - 마지막dx) / dt;
            마지막dx = dx; 마지막때 = now;
            bool 막힘 = (dx > 0 && 이전 == null) || (dx < 0 && 다음 == null);
            위치 = 막힘 ? dx / 3 : dx;
            Invalidate();
            Update();
        }

        /// <summary>
        /// 손을 뗐다. 넘어가면 +1(다음)/-1(이전), 제자리면 0 을 돌려주고 그쪽으로 마저 움직인다.
        /// 끝나면 끝나면(결과) 을 부른다.
        /// </summary>
        public int 놓기(double? 놓는속도, Action<int> 끝나면)
        {
            double v = 놓는속도.HasValue ? 놓는속도.Value : 속도;
            끄는중 = false;
            int r = 0;
            if ((위치 <= -넘김거리 || v < -튕김속도) && 다음 != null) r = 1;
            else if ((위치 >= 넘김거리 || v > 튕김속도) && 이전 != null) r = -1;
            결과 = r;
            끝 = 끝나면;
            움직이기(r == 1 ? -Width : r == -1 ? Width : 0);
            return r;
        }

        void 움직이기(double 도착)
        {
            시작 = 위치;
            목표 = 도착;
            // 남은 거리에 비례해 짧게 — 거의 다 끌어 놓았으면 금방 끝난다.
            double 비율 = Width > 0 ? Math.Abs(목표 - 시작) / Width : 1;
            걸리는ms = (int)Math.Max(90, 기본ms * Math.Min(1, 비율 + 0.2));
            Visible = true;
            BringToFront();
            시계 = System.Diagnostics.Stopwatch.StartNew();
            타이머.Start();
            Invalidate();
        }

        /// <summary>t(0~1) 지점으로 맞춘다. 1 이상이면 끝낸다 (시험에서도 부른다).</summary>
        public void 단계(double t)
        {
            double e = AlertForm.부드럽게(t);
            위치 = 시작 + (목표 - 시작) * e;
            if (t >= 1)
            {
                타이머.Stop();
                위치 = 목표;
                Action<int> k = 끝;
                끝 = null;
                if (k != null) k(결과);
                Visible = false;
                그림비우기();
                return;
            }
            Invalidate();
            Update();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.캔버스);
            int x = (int)Math.Round(위치);
            if (지금 != null) g.DrawImageUnscaled(지금, x, 0);
            if (이전 != null) g.DrawImageUnscaled(이전, x - Width, 0);
            if (다음 != null) g.DrawImageUnscaled(다음, x + Width, 0);
        }
    }

    /// <summary>
    /// 가는 막대로 그린 진행 표시 (팝업 시안 v5, ADR-0016). 단계마다 한 칸, 끝낸 칸은 짙게.
    /// 단계 이름은 싣지 않는다 — 다음 할 일은 버튼이 말하고, 이름은 풍선(설명)으로 본다.
    /// </summary>
    class StepBar : Control
    {
        string[] stages = new string[0];
        int 채움;
        bool 끝났음;

        static readonly Color 채운칸 = Color.FromArgb(0x3a, 0x3a, 0x3a);
        static readonly Color 빈칸 = Color.FromArgb(0xe8, 0xe8, 0xe8);
        static readonly Font 글꼴 = Ui.글꼴(11);

        public StepBar()
        {
            Height = 14;
            // 투명 바탕은 SupportsTransparentBackColor 를 켠 뒤에야 넣을 수 있다.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        /// <summary>채운수 = 끝낸 지점 수(시작 지점 포함). 끝난 건은 모두 채운다.</summary>
        public void 설정(string[] 단계들, int 채운수, bool 완료)
        {
            stages = 단계들 ?? new string[0];
            끝났음 = 완료;
            채움 = 완료 ? stages.Length : Math.Max(0, Math.Min(stages.Length, 채운수));
            Invalidate();
        }

        public int 채운칸수 { get { return 채움; } }
        public string 문구 { get { return stages.Length == 0 ? "" : 채움 + "/" + stages.Length; } }

        /// <summary>풍선에 보일 단계 설명: 끝낸 곳까지 ✓, 다음 할 곳에 ▶.</summary>
        public string 설명
        {
            get
            {
                var parts = new string[stages.Length];
                for (int i = 0; i < stages.Length; i++)
                    parts[i] = (i < 채움 ? "✓ " : (i == 채움 && !끝났음 ? "▶ " : "")) + stages[i];
                return string.Join("  →  ", parts);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int n = stages.Length;
            if (n == 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            string t = 문구;
            int 글폭 = TextRenderer.MeasureText(t, 글꼴).Width;
            int 막대폭 = Width - 글폭 - 6;
            const int 두께 = 4, 틈 = 3;
            int y = (Height - 두께) / 2;
            float 칸 = (막대폭 - 틈 * (n - 1)) / (float)n;
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(i * (칸 + 틈), y, 칸, 두께);
                using (var p = new GraphicsPath())
                {
                    float d = 두께;
                    p.AddArc(r.X, r.Y, d, d, 90, 180);
                    p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                    p.CloseFigure();
                    using (var br = new SolidBrush(i < 채움 ? 채운칸 : 빈칸)) g.FillPath(br, p);
                }
            }
            TextRenderer.DrawText(g, t, 글꼴, new Rectangle(Width - 글폭, 0, 글폭, Height), Ui.아주흐림,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// 진행 단계 표시. 웹 시안의 .flow / .st / .lk 를 그대로 옮긴 것이다.
    /// 지나온 단계는 회색 채움, 지금 해야 할 단계는 파란 채움, 남은 단계는 빈 원.
    /// 각 점 위에 단계 이름을 적고, 해야 할 단계만 파랑·굵게로 강조한다.
    /// </summary>
    class StepDots : Panel
    {
        string[] stages = new string[0];
        int 해야할단계 = 0;     // 이 인덱스가 파란 점이 된다
        bool 끝났음;

        public int 점 = 13;          // 도형 지름

        public float 라벨px = 12;    // 라벨 글자 크기(px)
        const float 선굵기 = 1.5f;
        int 라벨높이 { get { return (int)Math.Ceiling(라벨px * 1.35f); } }
        const int 라벨간격 = 4;

        /// <summary>도형 크기·라벨 크기를 바꾼 뒤 높이를 맞춘다.</summary>
        public void 높이맞추기() { Height = 라벨높이 + 라벨간격 + 점 + 1; }

        public StepDots()
        {
            Height = 16 + 6 + 13;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                     | ControlStyles.SupportsTransparentBackColor, true);
        }

        public void 설정(string[] 단계들, int 해야할, bool 완료)
        {
            stages = 단계들 ?? new string[0];
            해야할단계 = 해야할;
            끝났음 = 완료;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (stages.Length == 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int n = stages.Length;
            int 점y = 라벨높이 + 라벨간격;
            int 중심y = 점y + 점 / 2;

            // 첫 점은 왼쪽 끝, 마지막 점은 오른쪽 끝. 사이를 균등하게 나눈다.
            var 점x = new int[n];
            for (int i = 0; i < n; i++)
                점x[i] = n == 1 ? 0 : (int)Math.Round((double)(Width - 점) * i / (n - 1));

            // 연결선 먼저 — 점 아래로 깔린다.
            for (int i = 0; i < n - 1; i++)
            {
                bool 지나옴 = 끝났음 || i < 해야할단계;
                using (var pen = new Pen(지나옴 ? Ui.흐린글씨 : Ui.테두리, 선굵기))
                    g.DrawLine(pen, 점x[i] + 점, 중심y, 점x[i + 1], 중심y);
            }

            for (int i = 0; i < n; i++)
            {
                bool 지나옴 = 끝났음 || i < 해야할단계;
                bool 지금 = !끝났음 && i == 해야할단계;

                Color 채움, 선;
                if (지금) { 채움 = Ui.강조; 선 = Ui.강조; }
                else if (지나옴) { 채움 = Ui.흐린글씨; 선 = Ui.흐린글씨; }
                else { 채움 = Ui.캔버스; 선 = Ui.테두리; }

                var 원 = new Rectangle(점x[i], 점y, 점 - 1, 점 - 1);
                using (var br = new SolidBrush(채움)) g.FillEllipse(br, 원);
                using (var pen = new Pen(선, 선굵기)) g.DrawEllipse(pen, 원);

                // 라벨 — 점 중심에 맞추고, 패널 밖으로 나가지 않게 가둔다.
                using (Font f = Ui.글꼴(라벨px, 지금))
                {
                    Color 글씨 = 지금 ? Ui.강조 : (지나옴 ? Ui.흐린글씨 : Ui.아주흐림);
                    SizeF sz = g.MeasureString(stages[i], f);
                    float x = 점x[i] + 점 / 2f - sz.Width / 2f;
                    if (x < 0) x = 0;
                    if (x + sz.Width > Width) x = Width - sz.Width;
                    using (var br = new SolidBrush(글씨))
                        g.DrawString(stages[i], f, br, x, 0);
                }
            }
        }
    }

    /// <summary>
    /// 컨텍스트 메뉴 그리기 (Ui.메뉴꾸미기). 선택된 항목은 안쪽으로 들인 둥근 사각형으로 칠한다.
    /// </summary>
    class 메뉴그리기 : ToolStripRenderer
    {
        /// <summary>Windows 11 이 둥근 모서리와 테두리를 그려 주면 true — 그때는 사각 테두리를 그리지 않는다.</summary>
        public bool 시스템테두리;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Ui.캔버스)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (시스템테두리) return;
            using (var pen = new Pen(Ui.테두리))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(3, 1, e.Item.Width - 7, e.Item.Height - 3);
            const int d = 10;
            using (var p = new GraphicsPath())
            using (var b = new SolidBrush(Ui.양피지))
            {
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Ui.연한선)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.잉크 : Ui.아주흐림;
            base.OnRenderItemText(e);
        }
    }

    /// <summary>
    /// 알약 버튼. Region 을 깎으면 경계가 계단으로 남아, 안티에일리어싱으로 직접 그린다.
    /// 시안의 .btn — 완전한 알약, 1px 테두리, 눌림 때 scale(.95) 대신 색만 바꾼다.
    /// </summary>
    class PillButton : Button
    {
        public bool 주요;
        /// <summary>테두리·바탕 없이 글자만 (두 번째 선택지·아이콘). 올리거나 누르면 옅은 바탕이 생긴다.</summary>
        public bool 글자형;
        bool 눌림;
        bool 올림;

        /// <summary>
        /// 지금 그리는 상태: 꺼짐 / 눌림 / 올림 / 보통. 모든 버튼이 같은 규칙으로 바뀐다 (ADR-0015 3-3).
        /// 키보드 초점은 상태와 따로 테(초점테)로 덧그린다.
        /// </summary>
        public string 상태
        {
            get
            {
                if (!Enabled) return "꺼짐";
                if (눌림) return "눌림";
                if (올림) return "올림";
                return "보통";
            }
        }

        /// <summary>키보드로 초점이 왔을 때만 테를 그린다. 마우스로 누른 뒤에는 그리지 않는다.</summary>
        public bool 초점테보임 { get { return Focused && ShowFocusCues && Enabled; } }

        // 폼의 기본 버튼(Enter)이 되면 Windows 가 굵은 테를 요구한다. 이 앱은 기본 버튼을 쓰지 않는다.
        public override void NotifyDefault(bool value) { base.NotifyDefault(false); }

        public PillButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Ui.글꼴(14, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                     | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { 눌림 = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { 눌림 = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { 올림 = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { 올림 = false; Invalidate(); base.OnMouseLeave(e); }
        // 메뉴·대화상자가 마우스를 가져가면 MouseUp 이 오지 않는다 — 눌림·올림이 남지 않게 푼다.
        protected override void OnMouseCaptureChanged(EventArgs e) { 눌림 = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnLostFocus(EventArgs e) { 눌림 = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) { 눌림 = false; 올림 = false; }
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) { 눌림 = false; 올림 = false; } base.OnVisibleChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;


            // 알약 바깥은 부모가 실제로 그린 그림을 그대로 깐다.
            // 예전에는 부모 BackColor 한 색으로 사각형을 칠해, 부모가 그린 테두리·선이 버튼 둘레에서
            // 사각형으로 끊기고 가장자리에 사각 외곽선이 보였다 (사용자 제보 2026-09-16).
            if (Parent != null)
            {
                GraphicsState 저장 = g.Save();
                g.TranslateTransform(-Left, -Top);
                var 영역 = new PaintEventArgs(g, Bounds);
                InvokePaintBackground(Parent, 영역);
                InvokePaint(Parent, 영역);
                g.Restore(저장);
            }
            Color 채움, 테두리, 글씨;
            string s = 상태;
            if (s == "꺼짐")   { 채움 = Ui.연한선; 테두리 = Ui.테두리; 글씨 = Ui.아주흐림; }
            else if (주요)     { 채움 = s == "눌림" ? Ui.강조눌림 : s == "올림" ? Ui.강조올림 : Ui.강조; 테두리 = 채움; 글씨 = Ui.캔버스; }
            else               { 채움 = s == "눌림" ? Ui.테두리 : s == "올림" ? Ui.양피지 : Ui.캔버스; 테두리 = Ui.테두리; 글씨 = ForeColor; }

            if (글자형 && !주요)
            {
                // 보통·꺼짐은 바탕 없이 글자만. 올림·누름만 옅은 바탕.
                if (s == "꺼짐") 채움 = Color.Empty;
                else if (s == "보통") 채움 = Color.Empty;
                테두리 = Color.Empty;
            }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = 알약모양(r))
            {
                if (채움 != Color.Empty) using (var br = new SolidBrush(채움)) g.FillPath(br, p);
                if (테두리 != Color.Empty) using (var pen = new Pen(테두리, 1f)) g.DrawPath(pen, p);
            }
            if (초점테보임)
            {
                // 사각 점선 대신 알약을 따라가는 2px 테 (웹의 focus-visible ring 과 같은 색)
                using (var p = 알약모양(new Rectangle(1, 1, Width - 3, Height - 3)))
                using (var pen = new Pen(Ui.초점테, 2f))
                    g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, 글씨,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        static GraphicsPath 알약모양(Rectangle r)
        {
            var p = new GraphicsPath();
            int d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }
    }
}
