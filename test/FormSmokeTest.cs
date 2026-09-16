using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// 팝업을 실제로 띄우지 않고 생성·갱신·닫힘판정만 시험한다.
    /// 레이아웃 코드에서 터지는 문제를 잡기 위한 것.
    /// </summary>
    static class FormSmokeTest
    {
        static int passed = 0, failed = 0;

        static void Check(string name, object actual, object expected)
        {
            string a = Convert.ToString(actual), e = Convert.ToString(expected);
            if (a == e) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  기대=" + e + " 실제=" + a); }
        }
        static void CheckTrue(string name, bool c)
        {
            if (c) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        static void 누름(Button b)
        {
            // 창을 띄우기 전에는 PerformClick 이 무시되므로 Click 처리기를 직접 부른다.
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(b, new object[] { EventArgs.Empty });
        }

        /// <summary>감춰진 이벤트 처리기(OnMouseEnter 등)를 직접 부른다 — 창을 띄우지 않고 상태 전환을 본다.</summary>
        static void 부름(Control c, string 이름, EventArgs e)
        {
            c.GetType().GetMethod(이름, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new Type[] { e.GetType() }, null).Invoke(c, new object[] { e });
        }

        /// <summary>모든 버튼 공통 규칙 (ADR-0015 3-3): 사각 자국 없음, 상태 전환, 기본 버튼 테 없음.</summary>
        static void 버튼공통()
        {
            Console.WriteLine("\n[GUI-6] 버튼 공통: 둘레는 부모 그림 그대로, 상태 전환이 모두 같다");
            var 판 = new Panel();
            판.BackColor = Color.White;               // BackColor 와 실제 그림이 다른 부모 (카드·바닥 줄과 같은 경우)
            판.Size = new Size(120, 60);
            판.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.Clear(Color.Red); };
            var b = new PillButton();
            b.Text = "보통";
            Ui.알약(b, false);
            b.Bounds = new Rectangle(10, 10, 80, 28);
            판.Controls.Add(b);
            using (var bmp = new Bitmap(판.Width, 판.Height))
            {
                판.DrawToBitmap(bmp, new Rectangle(0, 0, 판.Width, 판.Height));
                Check("버튼 사각 모서리는 부모가 그린 색 (사각 자국 없음)", bmp.GetPixel(11, 11).ToArgb(), Color.Red.ToArgb());
                Check("알약 안쪽은 흰색", bmp.GetPixel(18, 24).ToArgb(), Ui.캔버스.ToArgb());   // 왼쪽 반원 안, 글자에서 먼 곳
            }

            Check("처음은 보통", b.상태, "보통");
            부름(b, "OnMouseEnter", EventArgs.Empty);
            Check("마우스 올림", b.상태, "올림");
            부름(b, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0));
            Check("누름", b.상태, "눌림");
            부름(b, "OnMouseCaptureChanged", EventArgs.Empty);
            Check("메뉴·대화상자가 마우스를 가져가면 누름이 풀림", b.상태, "올림");
            부름(b, "OnMouseLeave", EventArgs.Empty);
            Check("마우스가 나가면 보통", b.상태, "보통");
            부름(b, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0));
            Check("오른쪽 누름은 누름으로 보지 않음", b.상태, "보통");
            부름(b, "OnMouseEnter", EventArgs.Empty);
            b.Enabled = false;
            Check("꺼지면 꺼짐 (올림이 남지 않음)", b.상태, "꺼짐");
            Check("꺼진 버튼은 손 모양 커서가 아님", b.Cursor, Cursors.Default);
            b.Enabled = true;
            Check("다시 켜면 보통", b.상태, "보통");
            b.NotifyDefault(true);
            bool 기본 = (bool)typeof(ButtonBase).GetProperty("IsDefault",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(b, null);
            CheckTrue("기본 버튼 굵은 테 없음", !기본);
            CheckTrue("초점이 없으면 초점 테 없음", !b.초점테보임);
            판.Dispose();
        }

        /// <summary>창을 띄우기 전이라 Visible 은 늘 false — 컨트롤이 보이기를 요청받았는지(내부 상태)를 읽는다.</summary>
        static bool 보이기요청(Control c)
        {
            if (c == null) return false;
            var m = typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new Type[] { typeof(int) }, null);
            return (bool)m.Invoke(c, new object[] { 0x00000002 });   // STATE_VISIBLE
        }

        /// <summary>중첩 패널 안까지 뒤져 T 형 컨트롤을 처음 하나 찾는다.</summary>
        static T 찾기<T>(Control root) where T : Control
        {
            foreach (Control c in root.Controls)
            {
                if (c is T) return (T)c;
                T found = 찾기<T>(c);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>버튼이 중첩 패널 안에 있어도 찾는다. 배치가 바뀌어도 시험이 깨지지 않게.</summary>
        static Button FindButton(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                Button b = c as Button;
                if (b != null && b.Text == text) return b;
                Button found = FindButton(c, text);
                if (found != null) return found;
            }
            return null;
        }

        static AlertRow 행(PaymentItem it, int year, DateTime due, int stage)
        {
            var o = new Occurrence { Item = it, 연도 = year, 원기한일 = due, 보정기한일 = due, 알림일 = due.AddDays(-5) };
            return new AlertRow { Occ = o, Status = new StatusRecord { 연도 = year, Id = it.Id, 단계 = stage } };
        }

        /// <summary>코너형 팝업 (ADR-0009, 시안 v5 A안 ADR-0016): 자리·크기·넘기기·자동 넘김·점 상태·닫기.</summary>
        static void 코너팝업(BusinessDayCalendar cal, List<PaymentItem> master, AttachmentStore store)
        {
            Console.WriteLine("\n[GUI-2] 코너형 팝업 — 자리와 크기");
            var wa = new System.Drawing.Rectangle(0, 0, 1920, 1032);
            var r = AlertForm.자리(wa);
            Check("폭 340", r.Width, 340);
            Check("높이 212 (시안 v5)", r.Height, 212);
            Check("작업 표시줄 바로 위 (틈 없음)", r.Bottom, 1032);
            Check("오른쪽 12px", wa.Right - r.Right, 12);

            DateTime today = new DateTime(2026, 9, 16);
            PaymentItem a = master[0], b = master[2], c = master[4];
            var rows = new List<AlertRow> {
                행(a, 2026, new DateTime(2026, 9, 11), 0),   // 기한 지남
                행(b, 2026, new DateTime(2026, 9, 18), 0),
                행(c, 2026, new DateTime(2026, 9, 21), 0)
            };
            var form = new AlertForm(rows, new List<AlertRow>(), cal, today, null, store);
            form.CreateControl();
            Check("제목줄 없음", form.FormBorderStyle, FormBorderStyle.None);
            Check("창 크기", form.Size.Width + "x" + form.Size.Height, "340x212");
            Check("X 버튼 없음", form.ControlBox, false);
            CheckTrue("항상 위", form.TopMost);
            Check("첫 장은 기한이 가장 이른 건", form.현재번호, 0);
            Check("건수", form.건수값, 3);

            Console.WriteLine("\n[GUI-3] 접기: 제목 줄만 남고 아래 끝은 작업 표시줄에 붙은 채");
            int bottom = form.Bounds.Bottom;
            CheckTrue("처음엔 펼침", !form.접힘);
            CheckTrue("접기 버튼이 있음", FindButton(form, "▾") != null);
            form.접기(true);
            CheckTrue("접힘", form.접힘);
            Check("접힌 높이", form.Height, AlertForm.접힌높이);
            Check("아래 끝 그대로", form.Bounds.Bottom, bottom);
            Check("폭 그대로", form.Width, AlertForm.폭);
            var 숨긴라벨 = new Label(); 숨긴라벨.Visible = false;
            CheckTrue("보이기 요청 읽기 도구 확인 (숨김=false, 기본=true)", !보이기요청(숨긴라벨) && 보이기요청(new Label()));
            CheckTrue("접으면 '!' 알림 버튼 숨김", !form.머리부가보임);
            CheckTrue("접혀도 설정 버튼은 보임", 보이기요청(FindButton(form, form.설정버튼문구)));
            CheckTrue("접혀도 펼치기(▴) 버튼은 보임", FindButton(form, "▴") != null && 보이기요청(FindButton(form, "▴")));
            CheckTrue("접힌 줄은 눌러서 펼칠 수 있게 손 모양", form.Cursor == Cursors.Hand);
            form.접기(false);
            Check("펼친 높이", form.Height, AlertForm.높이);
            CheckTrue("펼치면 '!' 알림 버튼 자리 복귀", form.머리부가보임);

            Console.WriteLine("\n[GUI-7] 접기·펼치기 애니메이션 (ease-out, 아래 끝 고정)");
            Check("곡선 시작 0", AlertForm.부드럽게(0), 0d);
            Check("곡선 끝 1", AlertForm.부드럽게(1), 1d);
            CheckTrue("곡선은 처음이 빠르다 (t=0.5 → 0.875)", Math.Abs(AlertForm.부드럽게(0.5) - 0.875) < 1e-9);
            CheckTrue("범위 밖은 잘림", AlertForm.부드럽게(-1) == 0 && AlertForm.부드럽게(2) == 1);
            bool 늘어남 = true; double 앞 = 0;
            for (int k = 1; k <= 20; k++) { double v = AlertForm.부드럽게(k / 20.0); if (v < 앞) 늘어남 = false; 앞 = v; }
            CheckTrue("곡선은 줄지 않는다", 늘어남);
            form.접기(true, true);
            CheckTrue("애니메이션이면 바로 줄지 않음", form.Height == AlertForm.높이 && form.애니중);
            CheckTrue("상태는 바로 접힘 (버튼 문구·머리)", form.접힘 && !form.머리부가보임);
            form.애니단계(0.5);
            CheckTrue("중간 높이", form.Height > AlertForm.접힌높이 && form.Height < AlertForm.높이);
            Check("중간에도 아래 끝 고정", form.Bounds.Bottom, bottom);
            form.접기(false, true);
            form.애니단계(0.5);
            CheckTrue("도는 중에 되돌리면 지금 높이에서 다시 커짐", form.Height > AlertForm.접힌높이 && form.Height < AlertForm.높이);
            form.애니단계(1);
            Check("끝나면 정확히 펼친 높이", form.Height, AlertForm.높이);
            CheckTrue("끝나면 멈춤", !form.애니중);
            Check("끝나도 아래 끝 고정", form.Bounds.Bottom, bottom);
            form.접기(true);
            Check("창을 띄우기 전에는 곧바로 접힘", form.Height, AlertForm.접힌높이);
            form.접기(false);
            Check("펼쳐도 아래 끝 그대로", form.Bounds.Bottom, bottom);

            Console.WriteLine("\n[GUI-8] 처음 뜰 때 작업 표시줄 쪽에서 떠오르며 나타난다");
            int 최종Top = form.Top;
            form.등장시작();
            CheckTrue("시작은 투명", form.Opacity == 0 && form.등장중);
            Check("시작은 16px 아래", form.Top, 최종Top + AlertForm.등장거리);
            form.등장단계(0.5);
            CheckTrue("중간은 반쯤 보임", form.Opacity > 0 && form.Opacity < 1);
            CheckTrue("중간은 올라오는 중", form.Top > 최종Top && form.Top < 최종Top + AlertForm.등장거리);
            form.등장시작();
            form.등장단계(0.3);
            form.접기(true, false);
            CheckTrue("떠오르는 중에 접으면 먼저 제자리·불투명", !form.등장중 && form.Opacity == 1);
            Check("그래서 접힌 창의 아래 끝이 제자리", form.Bounds.Bottom, bottom);
            form.접기(false, false);
            Check("끝나면 제자리", form.Top, 최종Top);
            form.등장시작();
            form.등장단계(1);
            CheckTrue("끝나면 불투명·멈춤", form.Opacity == 1 && !form.등장중);
            Check("끝난 뒤 다시 불러도 그대로", form.Top, 최종Top);
            form.등장단계(0.2);
            Check("끝난 뒤 단계 호출은 무시", form.Top, 최종Top);

            Console.WriteLine("\n[GUI-4] 설정 버튼은 웹 화면 열기를 부른다");
            Button gear = FindButton(form, form.설정버튼문구);
            CheckTrue("설정 버튼 글자가 비어 있지 않음 (톱니 기호 또는 '설정')", form.설정버튼문구 == "\uE713" || form.설정버튼문구 == "설정");
            CheckTrue("설정 버튼이 있음", gear != null);
            int 불림 = 0;
            form.웹열기 = delegate { 불림++; };
            누름(gear);
            Check("누르면 웹 열기 한 번", 불림, 1);
            form.웹열기 = null;
            누름(gear);
            CheckTrue("연결이 없어도 오류 없음", true);
            CheckTrue("설정 버튼이 접기 버튼 왼쪽", gear.Right <= FindButton(form, "▾").Left);

            Console.WriteLine("\n[GUI-5] ⋯ 메뉴는 팝업과 같은 모양 (기본 사각 테두리·아이콘 띠 없음)");
            ContextMenuStrip 메뉴 = form.더보기메뉴;
            CheckTrue("아이콘 여백 띠 없음", !메뉴.ShowImageMargin && !메뉴.ShowCheckMargin);
            CheckTrue("직접 그리기", !(메뉴.Renderer is ToolStripProfessionalRenderer) && !(메뉴.Renderer is ToolStripSystemRenderer));
            Check("흰 바탕", 메뉴.BackColor.ToArgb(), Ui.캔버스.ToArgb());

            Console.WriteLine("\n[GUI-9] 시안 v5 A안: 한 장에 한 건, 빨강은 상태 글 한 곳");
            form.이동(0);
            CheckTrue("상태 글은 'N영업일 지남'", form.상태문구.EndsWith("영업일 지남"));
            Check("지난 건 상태 글만 빨강", form.상태색.ToArgb(), Ui.위험.ToArgb());
            CheckTrue("기한일은 상태 옆 한 줄 (· 9/11)", form.부가문구.StartsWith("· 9/11"));
            Check("진행 막대는 끝낸 지점/전체", form.막대문구, "1/" + Stages.For(a).Length);
            Check("머리에는 위치", form.위치문구, "1/3");
            CheckTrue("자료 경고·밀린 건이 없으면 '!' 없음", !form.알림있음);
            CheckTrue("다 고르기 전에는 완료 장 아님", !form.완료보임);
            form.이동(1);
            Check("넘기면 위치도 바뀜", form.위치문구, "2/3");
            CheckTrue("아직 기한 전이면 검정 D-n", form.상태문구.StartsWith("D-") && form.상태색.ToArgb() == Ui.잉크.ToArgb());
            form.접기(true, false);
            Check("접으면 머리에 전체 건수", form.위치문구, "3건");
            form.접기(false, false);

            Console.WriteLine("\n[GUI-2] 넘기기");
            form.이동(1);
            Check("오른쪽으로", form.현재번호, 1);
            form.이동(99);
            Check("끝에서 멈춤", form.현재번호, 2);
            form.이동(-5);
            Check("처음에서 멈춤", form.현재번호, 0);

            Console.WriteLine("\n[GUI-2] 버튼은 행동 문구 (ADR-0005)");
            Check("납부만 시작 → 전표 발행", form.진행버튼문구, Stages.다음행동(a.진행흐름, 0));

            Console.WriteLine("\n[GUI-2] 점 상태");
            PageDots dots = null;
            dots = 찾기<PageDots>(form);
            CheckTrue("점이 있음", dots != null);
            Check("지금 보는 건이 기한 지남 → 길쭉한 점 (색은 흑백, 빨강은 상태 글에)", dots.상태(0), "현재-지남");
            Check("아직 안 고른 건 → 빈 점", dots.상태(1), "남음");

            Console.WriteLine("\n[GUI-2] 고르면 다음 남은 건으로, 다 고르면 닫기");
            rows[0].Status.최종확인일 = today;          // 첫 건 대기
            form.RefreshState();
            Check("고른 건은 회색", dots.상태(0) == "현재-지남" ? "현재" : dots.상태(0), "현재");
            Check("다음 남은 건 = 1", form.다음남은건(0), 1);
            form.이동(1);
            Check("넘긴 뒤 첫 건 점은 처리됨", dots.상태(0), "처리됨");
            rows[1].Status.단계 = 1; rows[1].오늘단계변경 = true;
            Check("한 바퀴 돌아 남은 건 = 2", form.다음남은건(2), 2);
            Button close = FindButton(form, "닫기");
            Check("남은 건이 있으면 닫기 꺼짐", close.Enabled, false);
            rows[2].Status.최종확인일 = today;
            form.RefreshState();
            Check("남은 건 없음 → -1", form.다음남은건(0), -1);
            Check("다 고르면 닫기 켜짐", close.Enabled, true);

            Console.WriteLine("\n[GUI-10] 바닥 넘기기 줄: ‹ 다음 미리보기 › (ADR-0017)");
            form.이동(2);
            Check("마지막 카드로", form.현재번호, 2);
            Check("마지막 건 안내", form.미리보기문구, "마지막 건입니다");
            CheckTrue("마지막에서 › 꺼짐, ‹ 켜짐", !form.다음가능 && form.이전가능);
            form.넘기기(1);
            Check("끝에서는 넘어가지 않음 (한 바퀴 돌지 않음)", form.현재번호, 2);
            form.이동(0);
            CheckTrue("처음에서 ‹ 꺼짐, › 켜짐", !form.이전가능 && form.다음가능);
            CheckTrue("미리보기는 다음 건 이름", form.미리보기문구.StartsWith("다음 " + rows[1].Occ.Item.비용명));
            CheckTrue("다음 건이 고른 건이면 그 상태", form.미리보기문구.EndsWith("처리함"));
            form.넘기기(-1);
            Check("처음에서 ‹ 는 그대로", form.현재번호, 0);
            form.넘기기(1);
            Check("› 로 한 칸", form.현재번호, 1);
            CheckTrue("다음 건이 대기한 건이면 '내일 다시'", form.미리보기문구.EndsWith("내일 다시"));
            form.이동(2);
            CheckTrue("점은 위치 표시만 (누르지 않음)", !dots.누를수있음);

            Console.WriteLine("\n[GUI-9] 다 고르면 잠깐 뒤 완료 장");
            CheckTrue("고른 직후에는 아직 카드", !form.완료보임);
            form.넘김실행();
            CheckTrue("넘김 시각이 되면 완료 장", form.완료보임);
            Check("완료 장 설명", form.완료설명문구, "진행 1건 · 오늘은 대기 2건");
            Check("완료 장 머리는 전체 건수", form.위치문구, "3건");
            CheckTrue("완료 장에서도 점은 보임 (눌러서 다시 보기)", form.점보임);
            Check("완료 장에서는 '지금' 점이 없음", dots.상태(1), "처리됨");
            Check("완료 장 미리보기 줄", form.미리보기문구, "3건 모두 골랐습니다");
            CheckTrue("완료 장에서 ‹ 켜짐, › 꺼짐", form.이전가능 && !form.다음가능);
            int 보던 = form.현재번호;
            form.넘기기(-1);
            CheckTrue("완료 장에서 ‹ 는 보던 카드로", !form.완료보임 && form.현재번호 == 보던);
            form.넘김실행();
            CheckTrue("다시 완료 장", form.완료보임);
            form.이동(1);
            CheckTrue("점을 누르면 카드로 돌아감", !form.완료보임);
            rows[1].Status.단계 = 0; rows[1].오늘단계변경 = false; rows[1].Status.최종확인일 = null;
            form.RefreshState();
            form.넘김실행();
            CheckTrue("남은 건이 생기면 완료 장 대신 그 건으로", !form.완료보임 && form.현재번호 == 1);
            form.Dispose();

            Console.WriteLine("\n[GUI-2] 한 건이면 점을 숨기고, 13건 이상이면 글자로");
            var one = new AlertForm(new List<AlertRow> { 행(a, 2026, new DateTime(2026, 9, 18), 0) }, null, cal, today, "경고 문구", store);
            one.CreateControl();
            CheckTrue("한 건이면 점 숨김", !one.점보임);
            CheckTrue("여러 건이면 점 보임 (앞 창)", true);
            one.Dispose();

            var many = new List<AlertRow>();
            for (int i = 0; i < 15; i++) many.Add(행(master[i % master.Count], 2026, new DateTime(2026, 9, 18).AddDays(i), 0));
            var big = new AlertForm(many, null, cal, today, null, store);
            big.CreateControl();
            PageDots d2 = null;
            d2 = 찾기<PageDots>(big);
            CheckTrue("15건이면 'n / 15' 글자", d2.글자로);
            CheckTrue("점 줄이 창 폭 안", d2.Width < AlertForm.폭 - 80);
            big.Dispose();
        }

        /// <summary>밀기 움직임 (ADR-0017): 전환·끌기·되돌아오기·끝에서 버티기.</summary>
        static void 밀기()
        {
            Console.WriteLine("\n[GUI-11] 밀기 움직임");
            var s = new SlideOverlay();
            s.Size = new Size(340, 136);
            int 끝결과 = 99, 불림 = 0;
            s.전환(new Bitmap(340, 136), new Bitmap(340, 136), 1, delegate(int r) { 끝결과 = r; 불림++; });
            CheckTrue("다음 쪽 전환 시작: 위치 0, 도는 중", s.위치 == 0 && s.도는중);
            s.단계(0.5);
            CheckTrue("중간: 왼쪽으로 밀리는 중 (-340 < 위치 < 0)", s.위치 < 0 && s.위치 > -340);
            s.단계(1);
            CheckTrue("끝: 한 폭 밀리고 멈춤", s.위치 == -340 && !s.도는중);
            CheckTrue("끝 알림 한 번, 결과 +1", 불림 == 1 && 끝결과 == 1);
            s.전환(new Bitmap(340, 136), new Bitmap(340, 136), -1, null);
            s.단계(0.5);
            CheckTrue("이전 쪽은 오른쪽으로", s.위치 > 0);
            s.단계(1);

            Console.WriteLine("\n[GUI-11] 끌기");
            s.끌기시작(null, new Bitmap(340, 136), new Bitmap(340, 136));
            CheckTrue("끄는 중", s.끄는중);
            s.끌기(-50);
            Check("다음 쪽은 손을 그대로 따라옴", s.위치, -50d);
            s.끌기(90);
            Check("이전 카드가 없는 쪽은 1/3 만 (버티기)", s.위치, 30d);
            s.끌기(-50);
            Check("짧게 끌고 놓으면 제자리", s.놓기(0, null), 0);
            s.단계(1);
            Check("제자리 복귀", s.위치, 0d);

            s.끌기시작(new Bitmap(340, 136), new Bitmap(340, 136), new Bitmap(340, 136));
            s.끌기(-85);
            int 결과 = 99;
            Check("80px 넘게 끌면 다음으로", s.놓기(0, delegate(int r) { 결과 = r; }), 1);
            s.단계(1);
            CheckTrue("끝나면 -폭 위치, 결과 알림 +1", s.위치 == -340 && 결과 == 1);

            s.끌기시작(new Bitmap(340, 136), new Bitmap(340, 136), new Bitmap(340, 136));
            s.끌기(20);
            Check("짧아도 빠르게 튕기면 이전으로", s.놓기(0.8, null), -1);
            s.단계(1);

            s.끌기시작(new Bitmap(340, 136), new Bitmap(340, 136), null);
            s.끌기(-200);
            Check("다음 카드가 없으면 멀리 끌어도 제자리", s.놓기(-2, null), 0);
            s.단계(1);
            s.Dispose();
        }

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Application.EnableVisualStyles();

            var cal = new BusinessDayCalendar(
                new DateTime[] { new DateTime(2026,1,1) },
                new int[] { 2025, 2026, 2027 });

            var warnings = new List<string>();
            // 사내 자료가 아니라 저장소에 포함된 픽스처를 쓴다. 클론 직후에도 돌아야 한다.
            List<PaymentItem> master = Repository.LoadMaster("test\\fixtures\\master-fixture.tsv", warnings);

            DateTime today = new DateTime(2026, 3, 26);   // 픽스처에서 알림이 걸리는 날
            var occs = Scheduler.BuildOccurrences(master, cal, today);
            var statusMap = new Dictionary<string, StatusRecord>();
            RowSet set = Scheduler.BuildRows(occs, statusMap, cal, today);
            List<AlertRow> rows = set.Rows;

            Console.WriteLine("\n[GUI] 2026-03-26 기준 표시 대상 " + rows.Count + "건");
            foreach (AlertRow r in rows)
                Console.WriteLine("    - " + r.Occ.Item.표시명 + "  현재:" + r.현재단계명 + " -> 다음:" + r.다음단계명);

            Console.WriteLine("    (기한초과 요약 " + set.Overdue.Count + "건)");
            CheckTrue("표시 대상이 1건 이상", rows.Count >= 1);

            Console.WriteLine("\n[GUI] 폼 생성");
            string tmpRoot = Path.Combine(Path.GetTempPath(), "pa_att_test");
            var store = new AttachmentStore(Path.Combine(tmpRoot, "attachments.tsv"), tmpRoot);
            store.Load();
            var form = new AlertForm(rows, set.Overdue, cal, today, "공휴일 자료 시험용 경고 문구입니다.", store);
            form.CreateControl();

            CheckTrue("폼 생성됨", form != null);
            CheckTrue("컨트롤이 배치됨", form.Controls.Count >= 4);
            CheckTrue("창 높이가 양수", form.ClientSize.Height > 100);
            Check("X 버튼 비활성 (AC-25)", form.ControlBox, false);
            CheckTrue("항상 위 표시", form.TopMost);

            Button close = FindButton(form, "닫기");
            CheckTrue("닫기 버튼 존재", close != null);
            Check("초기 상태에서 닫기 비활성 (AC-11)", close.Enabled, false);

            Console.WriteLine("\n[GUI] 단계 진행 후 닫기 활성화 판정");
            foreach (AlertRow r in rows)
            {
                r.Status.단계++;
                r.Status.변경일시 = DateTime.Now;
                r.Status.최종확인일 = today;
                r.오늘단계변경 = true;
            }
            form.RefreshState();
            Check("모든 건 처리 후 닫기 활성", close.Enabled, true);

            Console.WriteLine("\n[GUI] 한 건만 되돌리면 다시 비활성");
            rows[0].Status.단계--;
            rows[0].오늘단계변경 = false;
            rows[0].Status.최종확인일 = null;
            form.RefreshState();
            Check("미처리 1건 발생 -> 닫기 비활성", close.Enabled, false);

            Console.WriteLine("\n[GUI] 오늘은 대기로도 닫기 가능해야 함");
            rows[0].Status.최종확인일 = today;
            form.RefreshState();
            Check("대기 처리 후 닫기 활성", close.Enabled, true);

            form.Dispose();

            코너팝업(cal, master, store);
            버튼공통();
            밀기();

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine(string.Format("  통과 {0}건 / 실패 {1}건", passed, failed));
            Console.WriteLine(new string('=', 50));
            return failed == 0 ? 0 : 1;
        }
    }
}
