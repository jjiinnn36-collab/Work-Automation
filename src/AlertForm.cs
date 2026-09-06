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

        readonly Panel listPanel;
        readonly Button closeButton;
        readonly Label summaryLabel;
        readonly Dictionary<AlertRow, RowView> views = new Dictionary<AlertRow, RowView>();

        bool allowClose;

        static readonly Color 배경 = Color.FromArgb(250, 250, 252);
        static readonly Color 완료색 = Color.FromArgb(28, 132, 74);
        static readonly Color 긴급색 = Color.FromArgb(198, 40, 40);
        static readonly Color 흐린글씨 = Color.FromArgb(110, 112, 120);

        public AlertForm(List<AlertRow> rows, List<AlertRow> overdue,
                         BusinessDayCalendar cal, DateTime today, string warningText)
        {
            this.rows = rows;
            this.overdue = overdue ?? new List<AlertRow>();
            this.cal = cal;
            this.today = today;

            Text = "납부 기한 알림 - " + today.ToString("yyyy-MM-dd");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = 배경;
            Font = new Font("맑은 고딕", 9.75f);

            int y = 12;

            var title = new Label();
            title.Text = string.Format("확인이 필요한 건이 {0}건 있습니다.", rows.Count);
            title.Font = new Font("맑은 고딕", 12f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(16, y);
            Controls.Add(title);
            y += title.PreferredHeight + 6;

            var sub = new Label();
            sub.Text = "각 건마다 다음 단계 또는 오늘은 대기를 선택해야 닫을 수 있습니다.";
            sub.ForeColor = 흐린글씨;
            sub.AutoSize = true;
            sub.Location = new Point(16, y);
            Controls.Add(sub);
            y += sub.PreferredHeight + 10;

            if (!string.IsNullOrEmpty(warningText))
            {
                var warn = new Label();
                warn.Text = "[주의] " + warningText;
                warn.ForeColor = Color.FromArgb(150, 90, 0);
                warn.BackColor = Color.FromArgb(255, 248, 225);
                warn.BorderStyle = BorderStyle.FixedSingle;
                warn.Padding = new Padding(8, 6, 8, 6);
                warn.MaximumSize = new Size(648, 0);
                warn.AutoSize = true;
                warn.Location = new Point(16, y);
                Controls.Add(warn);
                y += warn.Height + 12;
            }

            listPanel = new Panel();
            listPanel.Location = new Point(16, y);
            listPanel.Width = 648;
            listPanel.AutoScroll = true;
            listPanel.BackColor = 배경;
            Controls.Add(listPanel);

            int ry = 0;
            foreach (AlertRow r in rows)
            {
                var view = new RowView(this, r, cal, today);
                view.Panel.Location = new Point(0, ry);
                listPanel.Controls.Add(view.Panel);
                views[r] = view;
                ry += view.Panel.Height + 8;
            }

            listPanel.Height = Math.Max(Math.Min(ry, 420), 60);
            y += listPanel.Height + 10;

            if (this.overdue.Count > 0)
            {
                var od = new Label();
                od.Text = BuildOverdueText(this.overdue);
                od.ForeColor = Color.FromArgb(150, 90, 0);
                od.MaximumSize = new Size(648, 0);
                od.AutoSize = true;
                od.Location = new Point(16, y);
                Controls.Add(od);
                y += od.Height + 8;
            }

            summaryLabel = new Label();
            summaryLabel.AutoSize = true;
            summaryLabel.Location = new Point(16, y + 8);
            Controls.Add(summaryLabel);

            closeButton = new Button();
            closeButton.Text = "닫기";
            closeButton.Size = new Size(120, 34);
            closeButton.Location = new Point(544, y);
            closeButton.Click += delegate { TryClose(); };
            Controls.Add(closeButton);

            ClientSize = new Size(680, y + 50);
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

        /// <summary>단계 진행/대기/되돌리기 후 화면 전체를 다시 그린다.</summary>
        public void RefreshState()
        {
            int 남음 = 0;
            foreach (AlertRow r in rows)
            {
                views[r].Refresh();
                if (!r.오늘처리됨(today)) 남음++;
            }

            closeButton.Enabled = (남음 == 0);
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

            public readonly Panel Panel;
            readonly Label 제목;
            readonly Label 기한;
            readonly Label 단계;
            readonly Label 상태표시;
            readonly Button 진행;
            readonly Button 대기;
            readonly Button 되돌리기;

            public RowView(AlertForm owner, AlertRow row, BusinessDayCalendar cal, DateTime today)
            {
                this.owner = owner;
                this.row = row;
                this.cal = cal;
                this.today = today;

                Panel = new Panel();
                Panel.Width = 626;
                Panel.Height = 86;
                Panel.BorderStyle = BorderStyle.FixedSingle;
                Panel.BackColor = Color.White;

                상태표시 = new Label();
                상태표시.AutoSize = false;
                상태표시.Size = new Size(26, 26);
                상태표시.Location = new Point(10, 10);
                상태표시.Font = new Font("맑은 고딕", 12f, FontStyle.Bold);
                Panel.Controls.Add(상태표시);

                제목 = new Label();
                제목.AutoSize = true;
                제목.Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold);
                제목.Location = new Point(40, 10);
                Panel.Controls.Add(제목);

                기한 = new Label();
                기한.AutoSize = true;
                기한.Location = new Point(40, 34);
                Panel.Controls.Add(기한);

                단계 = new Label();
                단계.AutoSize = true;
                단계.Location = new Point(40, 57);
                Panel.Controls.Add(단계);

                진행 = new Button();
                진행.Size = new Size(104, 30);
                진행.Location = new Point(398, 14);
                진행.Click += delegate { Advance(); };
                Panel.Controls.Add(진행);

                대기 = new Button();
                대기.Text = "오늘은 대기";
                대기.Size = new Size(104, 30);
                대기.Location = new Point(508, 14);
                대기.Click += delegate { Defer(); };
                Panel.Controls.Add(대기);

                되돌리기 = new Button();
                되돌리기.Text = "되돌리기";
                되돌리기.Size = new Size(104, 26);
                되돌리기.Location = new Point(508, 50);
                되돌리기.Click += delegate { Revert(); };
                Panel.Controls.Add(되돌리기);
            }

            void Advance()
            {
                if (row.최종단계도달) return;
                row.Status.단계++;
                row.Status.변경일시 = DateTime.Now;
                row.Status.최종확인일 = today;
                row.오늘단계변경 = true;
                owner.RefreshState();
            }

            void Defer()
            {
                row.Status.최종확인일 = today;
                owner.RefreshState();
            }

            void Revert()
            {
                if (row.Status.단계 <= 0) return;
                row.Status.단계--;
                row.Status.변경일시 = DateTime.Now;
                owner.RefreshState();
            }

            public void Refresh()
            {
                PaymentItem it = row.Occ.Item;
                제목.Text = it.표시명;

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
                    dtext = string.Format("기한 {0}영업일 초과", -남은);
                    dcolor = 긴급색;
                }

                기한.Text = string.Format("기한 {0}  |  {1}  |  {2}",
                    row.Occ.보정기한일.ToString("yyyy-MM-dd"), dtext, 금액표시(row.Occ));
                기한.ForeColor = dcolor;

                bool done = row.최종단계도달;
                bool handled = row.오늘처리됨(today);

                상태표시.Text = done ? "V" : "-";
                상태표시.ForeColor = done ? 완료색 : 흐린글씨;

                if (done)
                {
                    단계.Text = row.현재단계명;
                    단계.ForeColor = 완료색;
                    단계.Font = new Font("맑은 고딕", 9.75f, FontStyle.Bold);
                    Panel.BackColor = Color.FromArgb(242, 250, 245);
                }
                else
                {
                    단계.Text = string.Format("현재: {0}   ->   다음: {1}", row.현재단계명, row.다음단계명);
                    단계.ForeColor = Color.FromArgb(40, 42, 50);
                    단계.Font = new Font("맑은 고딕", 9.75f, FontStyle.Regular);
                    Panel.BackColor = handled ? Color.FromArgb(248, 249, 251) : Color.White;
                }

                진행.Visible = !done;
                진행.Text = done ? "" : row.다음단계명;
                대기.Visible = !done;
                대기.Enabled = !handled;
                되돌리기.Visible = row.Status.단계 > 0;
                되돌리기.Location = new Point(508, done ? 14 : 50);
            }

            static string 금액표시(Occurrence occ)
            {
                // 그 해 고지서에서 확인한 실제 금액이 있으면 그것이 우선이다.
                if (occ.실제금액 != null)
                    return string.Format("{0:N0}원", occ.실제금액.금액);

                PaymentItem it = occ.Item;
                if (it.고정금액.HasValue)
                    return string.Format("{0:N0}원", it.고정금액.Value);
                if (it.금액규칙 == "해당없음" || it.금액규칙.Length == 0)
                    return "금액 없음";
                return "금액 미확인 (" + it.금액규칙 + ")";
            }
        }
    }
}
