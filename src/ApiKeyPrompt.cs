using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PaymentAlert
{
    /// <summary>
    /// 첫 실행 때 공휴일 자동 내려받기용 공공데이터포털 인증키를 받는 작은 창.
    /// 다른 PC 에 배포할 때 holidays.tsv 를 함께 주지 않아도, 사용자가 키만 넣으면 공휴일을 자동으로 받는다 (사용자 요청 2026-09-20).
    /// </summary>
    public static class ApiKeyPrompt
    {
        // '한국천문연구원_특일 정보' 데이터 상세 페이지
        const string 포털주소 = "https://www.data.go.kr/tcs/dss/selectApiDataDetailView.do?publicDataPk=15012690";

        /// <summary>키를 받으면 그 문자열, '나중에' 를 누르면 나중에=true 로 null 을 돌려준다.</summary>
        public static string 물어보기(out bool 나중에)
        {
            나중에 = false;
            using (var f = new Form())
            {
                f.Text = "공휴일 자료 받기 — 처음 한 번";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = true;
                f.AutoScaleMode = AutoScaleMode.Dpi;
                f.ClientSize = new Size(500, 292);
                f.Font = 글꼴(9.5f);
                f.BackColor = Color.White;

                const int 여백 = 20, 폭 = 460;

                // AutoSize + MaximumSize 로 폭에 맞춰 줄바꿈하고 필요한 만큼 세로로 늘어난다 (DPI 에서도 잘림 없음).
                var 안내 = new Label();
                안내.AutoSize = true;
                안내.MaximumSize = new Size(폭, 0);
                안내.Location = new Point(여백, 18);
                안내.Text = "주말·공휴일을 반영해 납부 기한을 계산하려면 공공데이터포털 인증키가 필요합니다.\r\n\r\n"
                          + "'한국천문연구원_특일 정보' 활용신청 후 받은 인증키(일반 인증키·서비스키)를 아래에 붙여 넣으세요.";
                f.Controls.Add(안내);

                var 링크 = new LinkLabel();
                링크.AutoSize = true;
                링크.Location = new Point(여백, 안내.Bottom + 10);
                링크.Text = "공공데이터포털에서 인증키 받기 (특일 정보)";
                링크.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(포털주소) { UseShellExecute = true }); } catch { } };
                f.Controls.Add(링크);

                var 라벨 = new Label();
                라벨.AutoSize = true;
                라벨.Location = new Point(여백, 링크.Bottom + 12);
                라벨.Text = "인증키";
                f.Controls.Add(라벨);

                var 입력 = new TextBox();
                입력.Location = new Point(여백, 라벨.Bottom + 4);
                입력.Size = new Size(폭, 26);
                입력.Font = 글꼴(10f);
                f.Controls.Add(입력);

                var 힌트 = new Label();
                힌트.AutoSize = true;
                힌트.MaximumSize = new Size(폭, 0);
                힌트.ForeColor = Color.FromArgb(120, 120, 120);
                힌트.Location = new Point(여백, 입력.Bottom + 8);
                힌트.Text = "키는 이 PC 의 자료 폴더에만 저장됩니다. 지금 넣지 않으면 나중에 설정 화면에서 넣을 수 있습니다.";
                힌트.Font = 글꼴(8.5f);
                f.Controls.Add(힌트);

                int 버튼y = 힌트.Bottom + 16;
                var 받기 = new Button();
                받기.Text = "입력하고 받기";
                받기.Size = new Size(120, 30);
                받기.Location = new Point(여백 + 폭 - 120, 버튼y);
                받기.DialogResult = DialogResult.OK;
                f.Controls.Add(받기);
                f.AcceptButton = 받기;

                var 나중 = new Button();
                나중.Text = "나중에";
                나중.Size = new Size(90, 30);
                나중.Location = new Point(받기.Left - 10 - 90, 버튼y);
                나중.DialogResult = DialogResult.Cancel;
                f.Controls.Add(나중);
                f.CancelButton = 나중;

                f.ClientSize = new Size(여백 + 폭 + 여백, 버튼y + 30 + 여백);

                DialogResult dr = f.ShowDialog();
                if (dr == DialogResult.OK)
                {
                    string key = (입력.Text ?? "").Trim();
                    if (key.Length > 0) return key;
                }
                나중에 = true;
                return null;
            }
        }

        static Font 글꼴(float pt)
        {
            try { return new Font("맑은 고딕", pt); }
            catch { return new Font(FontFamily.GenericSansSerif, pt); }
        }
    }
}
