using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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
    /// 알약 버튼. Region 을 깎으면 경계가 계단으로 남아, 안티에일리어싱으로 직접 그린다.
    /// 시안의 .btn — 완전한 알약, 1px 테두리, 눌림 때 scale(.95) 대신 색만 바꾼다.
    /// </summary>
    class PillButton : Button
    {
        public bool 주요;
        bool 눌림;

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

        protected override void OnMouseDown(MouseEventArgs e) { 눌림 = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { 눌림 = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;


            // Button 은 Color.Transparent 를 제대로 처리하지 않아 모서리에 사각 자국이 남는다.
            // 부모 배경색으로 먼저 칠해 알약 바깥이 카드와 같은 색이 되게 한다.
            Color 뒷배경 = Parent != null ? Parent.BackColor : Ui.캔버스;
            using (var bg = new SolidBrush(뒷배경)) g.FillRectangle(bg, ClientRectangle);
            Color 채움, 테두리, 글씨;
            if (!Enabled)      { 채움 = Ui.연한선;  테두리 = Ui.테두리; 글씨 = Ui.아주흐림; }
            else if (주요)      { 채움 = Ui.강조;    테두리 = Ui.강조;   글씨 = Ui.캔버스; }
            else               { 채움 = 눌림 ? Ui.양피지 : Ui.캔버스; 테두리 = Ui.테두리; 글씨 = ForeColor; }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = new GraphicsPath())
            {
                int d = r.Height;
                p.AddArc(r.X, r.Y, d, d, 90, 180);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                p.CloseFigure();
                using (var br = new SolidBrush(채움)) g.FillPath(br, p);
                using (var pen = new Pen(테두리, 1f)) g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, 글씨,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
