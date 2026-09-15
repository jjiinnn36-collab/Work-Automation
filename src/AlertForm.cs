using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PaymentAlert
{
    /// <summary>
    /// 알림 팝업. 표시된 모든 건을 처리하기 전에는 닫히지 않는다(AC-11, AC-25).
    /// </summary>
    public class AlertForm : Form
    {
        readonly List<AlertRow> rows;
        readonly List<AlertRow> overdue;
        readonly BusinessDayCalendar cal;
        readonly DateTime today;
        readonly AttachmentStore store;

        readonly Panel listPanel;
        readonly PillButton closeButton;
        readonly Label summaryLabel;
        readonly Dictionary<AlertRow, RowView> views = new Dictionary<AlertRow, RowView>();

        bool allowClose;

        static readonly Color 배경 = Ui.캔버스;
        static readonly Color 완료색 = Ui.흐린글씨;
        static readonly Color 긴급색 = Ui.위험;
        static readonly Color 흐린글씨 = Ui.흐린글씨;

        public AlertForm(List<AlertRow> rows, List<AlertRow> overdue,
                         BusinessDayCalendar cal, DateTime today, string warningText,
                         AttachmentStore store)
        {
            this.rows = rows;
            this.overdue = overdue ?? new List<AlertRow>();
            this.cal = cal;
            this.today = today;
            this.store = store;

            Text = "납부 기한 알림 - " + today.ToString("yyyy-MM-dd");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = 배경;
            Font = Ui.글꼴(14);
            ForeColor = Ui.잉크;

            const int 좌 = 22;          // 웹 시안 카드의 안쪽 여백
            const int 폭 = 636;         // 680 - 좌우 22
            int y = 22;

            var title = new Label();
            title.Text = string.Format("확인이 필요한 건이 {0}건 있습니다.", rows.Count);
            title.Font = Ui.글꼴(21, true);
            title.ForeColor = Ui.잉크;
            title.AutoSize = true;
            title.Location = new Point(좌, y);
            Controls.Add(title);
            y += title.PreferredHeight + 5;

            var sub = new Label();
            sub.Text = "각 건마다 다음 단계 또는 오늘은 대기를 선택해야 닫을 수 있습니다.";
            sub.Font = Ui.글꼴(12);
            sub.ForeColor = 흐린글씨;
            sub.AutoSize = true;
            sub.Location = new Point(좌, y);
            Controls.Add(sub);
            y += sub.PreferredHeight + 14;

            if (!string.IsNullOrEmpty(warningText))
            {
                // 시안의 경고 카드 — 노란 띠 대신 펄 바탕에 경고색 글씨.
                var warn = new Label();
                warn.Text = warningText;
                warn.Font = Ui.글꼴(12);
                warn.ForeColor = Ui.경고글씨;
                warn.BackColor = Ui.펄;
                warn.Padding = new Padding(14, 10, 14, 10);
                warn.MaximumSize = new Size(폭, 0);
                warn.AutoSize = true;
                warn.Location = new Point(좌, y);
                Controls.Add(warn);
                y += warn.Height + 14;
            }

            listPanel = new Panel();
            listPanel.Location = new Point(좌, y);
            listPanel.Width = 폭;
            listPanel.AutoScroll = true;
            listPanel.BackColor = 배경;
            Controls.Add(listPanel);

            int ry = 0;
            foreach (AlertRow r in rows)
            {
                var view = new RowView(this, r, cal, today, store);
                view.Panel.Location = new Point(0, ry);
                listPanel.Controls.Add(view.Panel);
                views[r] = view;
                ry += view.Panel.Height + 10;
            }
            if (ry > 0) ry -= 10;       // 마지막 카드 아래 간격은 뺀다

            // 목록 높이를 고정하면 행이 커질 때 마지막 행이 잘린다.
            // 화면에서 쓸 수 있는 높이를 계산해 거기에 맞춘다.
            int 아래여백 = 170;   // 기한초과 요약 + 바닥 띠 + 창 테두리
            int 최대목록 = Screen.PrimaryScreen.WorkingArea.Height - y - 아래여백;
            if (최대목록 < 200) 최대목록 = 200;
            listPanel.Height = Math.Max(Math.Min(ry, 최대목록), 60);
            y += listPanel.Height + 16;

            if (this.overdue.Count > 0)
            {
                var od = new Label();
                od.Text = BuildOverdueText(this.overdue);
                od.Font = Ui.글꼴(12);
                od.ForeColor = Ui.경고글씨;
                od.MaximumSize = new Size(폭, 0);
                od.AutoSize = true;
                od.Location = new Point(좌, y);
                Controls.Add(od);
                y += od.Height + 14;
            }

            // ── 바닥 띠 ── 시안처럼 양피지 바탕에 요약과 닫기를 둔다.
            var footer = new Panel();
            footer.BackColor = Ui.양피지;
            footer.Location = new Point(0, y);
            footer.Size = new Size(680, 64);
            Controls.Add(footer);

            var 구분선 = new Panel();
            구분선.BackColor = Ui.연한선;
            구분선.Location = new Point(0, 0);
            구분선.Size = new Size(680, 1);
            footer.Controls.Add(구분선);

            summaryLabel = new Label();
            summaryLabel.Font = Ui.글꼴(12);
            summaryLabel.AutoSize = true;
            summaryLabel.Location = new Point(좌, 24);
            footer.Controls.Add(summaryLabel);

            closeButton = new PillButton();
            closeButton.Text = "닫기";
            closeButton.Size = new Size(108, 38);
            closeButton.Location = new Point(680 - 좌 - 108, 13);
            closeButton.Click += delegate { TryClose(); };
            Ui.알약(closeButton, true);
            footer.Controls.Add(closeButton);

            ClientSize = new Size(680, y + 64);
            RefreshState();
        }

        static string BuildOverdueText(List<AlertRow> overdue)
        {
            var names = new List<string>();
            int shown = 0;
            foreach (AlertRow r in overdue)
            {
                if (shown >= 3) break;
                names.Add(r.Occ.Item.비용명 + "(" + r.Occ.보정기한일.ToString("yy-MM-dd") + ")");
                shown++;
            }
            string tail = overdue.Count > shown
                ? string.Format(" 외 {0}건", overdue.Count - shown) : "";
            return string.Format(
                "기한이 지난 미처리 건이 {0}건 있습니다: {1}{2}\r\n" +
                "이 건들은 여기서 처리를 강제하지 않습니다. data\\run.log 에 전체 목록이 있습니다.",
                overdue.Count, string.Join(", ", names.ToArray()), tail);
        }

        /// <summary>
        /// 누른 즉시 DB 에 쓰는 통로 (ADR-0004). 인자는 행과 동작(진행/대기/되돌리기),
        /// 돌려주는 값은 DB 에 기록된 뒤의 상태. 비어 있으면 메모리에서만 바꾼다 (화면 시험용).
        /// </summary>
        public Func<AlertRow, string, StatusRecord> 단계변경;

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

        /// <summary>단계 진행/대기/되돌리기 후 화면 전체를 다시 그린다.</summary>
        public void RefreshState()
        {
            int 남음 = 0;
            foreach (AlertRow r in rows)
            {
                views[r].Refresh();
                if (!r.오늘처리됨(today)) 남음++;
            }

            Ui.버튼활성(closeButton, 남음 == 0);
            summaryLabel.Text = 남음 == 0
                ? "모든 건을 확인했습니다. 닫아도 됩니다."
                : string.Format("아직 확인하지 않은 건이 {0}건 남았습니다.", 남음);
            summaryLabel.ForeColor = 남음 == 0 ? 완료색 : 긴급색;
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

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4)) return true;
            if (keyData == Keys.Escape) return true;
            return base.ProcessDialogKey(keyData);
        }

        // 한 건을 그리는 내부 뷰
        class RowView
        {
            readonly AlertForm owner;
            readonly AlertRow row;
            readonly BusinessDayCalendar cal;
            readonly DateTime today;
            readonly AttachmentStore store;

            public readonly CardPanel Panel;
            readonly Label 심각도;
            readonly Label 기관;
            readonly Label 기한;
            readonly Label 제목;
            readonly Label 금액;
            readonly StepDots 단계점;
            readonly PillButton 진행;
            readonly PillButton 대기;
            readonly PillButton 되돌리기;
            readonly Label 증빙;
            readonly PillButton 첨부;
            readonly PillButton 열기;

            public RowView(AlertForm owner, AlertRow row, BusinessDayCalendar cal, DateTime today,
                           AttachmentStore store)
            {
                this.owner = owner;
                this.row = row;
                this.cal = cal;
                this.today = today;
                this.store = store;

                // 시안의 알림 카드 — 1px 테두리, 11px 모서리, 왼쪽 심각도 띠.
                Panel = new CardPanel();
                Panel.Width = 616;
                Panel.Height = 224;
                Panel.Padding = new Padding(20, 16, 20, 16);

                const int 좌 = 20;
                const int 폭 = 576;     // 616 - 좌우 20

                심각도 = new Label();
                심각도.AutoSize = true;
                심각도.Font = Ui.글꼴(12, true);
                심각도.Location = new Point(좌, 16);
                Panel.Controls.Add(심각도);

                기관 = new Label();
                기관.AutoSize = true;
                기관.Font = Ui.글꼴(12);
                기관.ForeColor = 흐린글씨;
                기관.TextAlign = ContentAlignment.TopRight;
                기관.Location = new Point(좌, 16);
                Panel.Controls.Add(기관);

                기한 = new Label();
                기한.AutoSize = true;
                기한.Font = Ui.글꼴(12);
                기한.ForeColor = 흐린글씨;
                기한.Location = new Point(좌, 40);
                Panel.Controls.Add(기한);

                제목 = new Label();
                제목.AutoSize = true;
                제목.Font = Ui.글꼴(21, true);
                제목.ForeColor = Ui.잉크;
                제목.Location = new Point(좌 - 2, 58);
                Panel.Controls.Add(제목);

                금액 = new Label();
                금액.AutoSize = true;
                금액.Font = Ui.글꼴(17, true);
                금액.ForeColor = Ui.잉크;
                금액.Location = new Point(좌, 92);
                Panel.Controls.Add(금액);

                단계점 = new StepDots();
                단계점.Location = new Point(좌, 116);
                단계점.Width = 폭;
                Panel.Controls.Add(단계점);

                // ── 조치 버튼 ── 다음 단계만 파란 알약, 나머지는 흰 알약.
                진행 = new PillButton();
                진행.Size = new Size(104, 36);
                진행.Location = new Point(좌, 172);
                진행.Click += delegate { Advance(); };
                Ui.알약(진행, true);
                Panel.Controls.Add(진행);

                대기 = new PillButton();
                대기.Text = "오늘은 대기";
                대기.Size = new Size(116, 36);
                대기.Location = new Point(좌 + 110, 172);
                대기.Click += delegate { Defer(); };
                Ui.알약(대기, false);
                Panel.Controls.Add(대기);

                되돌리기 = new PillButton();
                되돌리기.Text = "되돌리기";
                되돌리기.Size = new Size(96, 36);
                되돌리기.Location = new Point(좌 + 232, 172);
                되돌리기.Click += delegate { Revert(); };
                Ui.알약(되돌리기, false);
                되돌리기.ForeColor = 흐린글씨;
                Panel.Controls.Add(되돌리기);

                증빙 = new Label();
                증빙.AutoSize = true;
                증빙.Font = Ui.글꼴(12);
                증빙.ForeColor = Ui.아주흐림;
                증빙.Location = new Point(좌 + 336, 183);
                Panel.Controls.Add(증빙);

                첨부 = new PillButton();
                첨부.Text = "증빙 첨부";
                첨부.Size = new Size(92, 36);
                첨부.Location = new Point(좌 + 388, 172);
                첨부.Click += delegate { AttachFile(); };
                Ui.알약(첨부, false);
                Panel.Controls.Add(첨부);

                열기 = new PillButton();
                열기.Text = "증빙 열기";
                열기.Size = new Size(92, 36);
                열기.Location = new Point(좌 + 486, 172);
                열기.Click += delegate { OpenFolder(); };
                Ui.알약(열기, false);
                Panel.Controls.Add(열기);
            }

            /// <summary>증빙 파일을 골라 보관소에 복사한다. 현재 단계로 기록된다.</summary>
            void AttachFile()
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = row.Occ.Item.표시명 + " — 증빙 첨부 (" + row.현재단계명 + ")";
                    dlg.Filter = "증빙 파일 (*.pdf;*.jpg;*.png;*.xlsx;*.hwp;*.docx)" +
                                 "|*.pdf;*.jpg;*.jpeg;*.png;*.xlsx;*.xls;*.hwp;*.hwpx;*.docx|모든 파일 (*.*)|*.*";
                    dlg.Multiselect = true;
                    if (dlg.ShowDialog(owner) != DialogResult.OK) return;

                    int ok = 0;
                    var failed = new List<string>();
                    foreach (string f in dlg.FileNames)
                    {
                        try
                        {
                            store.Attach(row.Occ.연도, row.Occ.Item.Id, row.현재단계명, f);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            failed.Add(System.IO.Path.GetFileName(f) + " — " + ex.Message);
                        }
                    }

                    if (failed.Count > 0)
                    {
                        MessageBox.Show(owner,
                            string.Format("{0}건은 첨부하지 못했습니다.\r\n\r\n{1}",
                                failed.Count, string.Join("\r\n", failed.ToArray())),
                            "증빙 첨부", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    if (ok > 0) owner.RefreshState();
                }
            }

            /// <summary>증빙 폴더를 탐색기로 연다.</summary>
            void OpenFolder()
            {
                string folder = store.FolderFor(row.Occ.연도, row.Occ.Item.Id);
                try
                {
                    if (!System.IO.Directory.Exists(folder))
                        System.IO.Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "폴더를 열지 못했습니다.\r\n" + folder + "\r\n\r\n" + ex.Message,
                        "증빙", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            void Advance()
            {
                if (row.최종단계도달) return;
                if (!owner.적용(row, "진행")) return;
                row.오늘단계변경 = true;
                owner.RefreshState();
            }

            void Defer()
            {
                if (!owner.적용(row, "대기")) return;
                owner.RefreshState();
            }

            void Revert()
            {
                if (row.Status.단계 <= 0) return;
                if (!owner.적용(row, "되돌리기")) return;
                owner.RefreshState();
            }

            public void Refresh()
            {
                PaymentItem it = row.Occ.Item;
                제목.Text = it.비용명;   // 표시명은 "비용명 (기관)" 이라 기관 라벨과 중복된다
                금액.Text = 금액표시(row.Occ);

                기관.Text = it.기관;
                기관.Location = new Point(Panel.Width - 20 - 기관.PreferredWidth, 16);

                int 남은 = row.Occ.남은영업일(cal, today);
                string dtext;
                Color dcolor;
                if (남은 > 0)
                {
                    dtext = string.Format("D-{0}영업일", 남은);
                    dcolor = 남은 <= 1 ? 긴급색 : 흐린글씨;
                }
                else if (남은 == 0)
                {
                    dtext = "오늘이 기한입니다";
                    dcolor = 긴급색;
                }
                else
                {
                    dtext = string.Format("기한 {0}영업일 지남", -남은);
                    dcolor = 긴급색;
                }

                bool done = row.최종단계도달;
                bool handled = row.오늘처리됨(today);

                // 심각도는 카드 맨 위 한 줄과 왼쪽 띠, 두 곳에서 같은 말을 한다.
                심각도.Text = done ? "처리 완료" : dtext;
                심각도.ForeColor = done ? 흐린글씨 : dcolor;
                Panel.띠색 = done ? Ui.테두리 : (남은 <= 0 ? Ui.위험 : Ui.아주흐림);

                기한.Text = "기한 " + row.Occ.보정기한일.ToString("yyyy-MM-dd");
                기한.ForeColor = 흐린글씨;

                // 지금 눌러야 할 단계가 파란 점이 된다. 끝난 건은 전부 회색.
                단계점.설정(Stages.For(it.진행흐름), row.Status.단계 + 1, done);

                Panel.BackColor = done ? Ui.펄 : (handled ? Ui.펄 : Ui.캔버스);
                Panel.테두리색 = done ? Ui.연한선 : Ui.테두리;

                // ── 버튼 ──
                진행.Visible = !done;
                진행.Text = done ? "" : row.다음행동;
                if (!done)
                {
                    // 글자 길이에 맞춰 알약 폭을 잡는다. 고정폭이면 단계 이름이 잘린다.
                    int w = TextRenderer.MeasureText(진행.Text, 진행.Font).Width + 34;
                    if (w < 96) w = 96;
                    진행.Width = w;
                    대기.Location = new Point(진행.Left + w + 6, 진행.Top);
                    되돌리기.Location = new Point(대기.Left + 대기.Width + 6, 진행.Top);
                }
                else
                {
                    되돌리기.Location = new Point(20, 진행.Top);
                }

                대기.Visible = !done;
                Ui.버튼활성(대기, !handled);
                되돌리기.Visible = row.Status.단계 > 0;
                되돌리기.ForeColor = 흐린글씨;

                var atts = store.For(row.Occ.연도, row.Occ.Item.Id);
                if (atts.Count == 0)
                {
                    증빙.Text = "증빙 없음";
                    증빙.ForeColor = Ui.아주흐림;
                    Ui.버튼활성(열기, false);
                }
                else
                {
                    var stages = new List<string>();
                    foreach (Attachment a in atts)
                        if (!stages.Contains(a.단계)) stages.Add(a.단계);
                    증빙.Text = string.Format("증빙 {0}건", atts.Count);
                    증빙.ForeColor = 흐린글씨;
                    Ui.버튼활성(열기, true);
                }
                증빙.Location = new Point(첨부.Left - 증빙.PreferredWidth - 10, 진행.Top + 11);
            }

            static string 금액표시(Occurrence occ)
            {
                // 그 해 고지서에서 확인한 금액 → 고정 규칙의 마스터 금액 순 (AmountRules).
                decimal? a = AmountRules.금액(occ);
                if (a.HasValue) return string.Format("{0:N0}원", a.Value);
                return AmountRules.미확인(occ) ? "금액 미확인" : "금액 없음";
            }
        }
    }
}
