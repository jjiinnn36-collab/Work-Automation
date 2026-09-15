using System;
using System.Collections.Generic;

namespace PaymentAlert
{
    /// <summary>진행흐름 종류.</summary>
    public enum Flow { 신고납부, 납부만, 제출만 }

    /// <summary>
    /// 진행흐름별 지점 정의. 지점 이름은 '달성한 것' 이다 (AC-W73).
    /// 단계 값 stage 는 마지막으로 끝낸 지점의 번호이고, 지금 해야 할 일은 stage + 1 지점이다 (AC-W70).
    /// </summary>
    public static class Stages
    {
        public static readonly string[] 신고납부 = { "신고서 작성", "신고", "전표발행", "납부" };
        public static readonly string[] 납부만 = { "고지서수령", "전표발행", "납부" };
        public static readonly string[] 제출만 = { "제출자료 작성", "제출" };

        // 각 지점에 도달하기 위해 하는 행동. 0 번 지점은 시작 계기라 행동이 없다 (AC-W50).
        static readonly string[] 신고납부행동 = { "", "신고하기", "전표 발행", "납부하기" };
        static readonly string[] 납부만행동 = { "", "전표 발행", "납부하기" };
        static readonly string[] 제출만행동 = { "", "제출하기" };

        public const string 끝남문구 = "모두 끝났습니다";

        public static string[] For(Flow flow)
        {
            switch (flow)
            {
                case Flow.신고납부: return 신고납부;
                case Flow.납부만: return 납부만;
                case Flow.제출만: return 제출만;
                default: throw new ArgumentOutOfRangeException("flow");
            }
        }

        static string[] ActionsFor(Flow flow)
        {
            switch (flow)
            {
                case Flow.신고납부: return 신고납부행동;
                case Flow.납부만: return 납부만행동;
                default: return 제출만행동;
            }
        }

        /// <summary>stage 까지 끝낸 건에서 지금 해야 할 행동. 다 끝났으면 null.</summary>
        public static string 다음행동(Flow flow, int stage)
        {
            string[] a = ActionsFor(flow);
            int next = stage + 1;
            return next >= 1 && next < a.Length ? a[next] : null;
        }

        public static Flow Parse(string text)
        {
            string t = (text ?? "").Trim();
            if (t == "신고납부") return Flow.신고납부;
            if (t == "납부만") return Flow.납부만;
            if (t == "제출만") return Flow.제출만;
            throw new FormatException("알 수 없는 진행흐름: '" + text + "' (신고납부 / 납부만 / 제출만 중 하나여야 합니다)");
        }

        /// <summary>해당 흐름의 마지막 단계 인덱스.</summary>
        public static int FinalIndex(Flow flow) { return For(flow).Length - 1; }

        /// <summary>
        /// 2026-09-16 이전에 쓰던 상태형 이름 → 지금 이름. 옛 증빙 기록을 옮길 때 쓴다.
        /// </summary>
        public static readonly Dictionary<string, string> 옛이름 = new Dictionary<string, string>
        {
            { "신고 전", "신고서 작성" }, { "신고완료", "신고" }, { "전표결재", "전표발행" }, { "납부완료", "납부" },
            { "납부 전", "고지서수령" }, { "제출 전", "제출자료 작성" }, { "제출완료", "제출" }
        };
    }

    /// <summary>
    /// 금액규칙은 고정 / 변동 두 가지다 (AC-W25).
    /// 고정은 매년 같은 금액을 마스터에 두고, 변동은 해마다 연도별 금액으로만 관리한다 (AC-W26a).
    /// 납부가 없는 흐름(제출만)은 금액 자체가 해당 없다 (AC-W27).
    /// </summary>
    public static class AmountRules
    {
        public const string 고정 = "고정";
        public const string 변동 = "변동";

        /// <summary>
        /// 옛 규칙 값을 두 가지로 줄인다. 고정금액이 적혀 있던 건은 고정으로 본다 —
        /// 적어 둔 금액을 버리지 않기 위해서다.
        /// </summary>
        public static string Normalize(string rule, decimal? fixedAmount)
        {
            string r = (rule ?? "").Trim();
            if (r == 고정 || fixedAmount.HasValue) return 고정;
            return 변동;
        }

        public static bool IsValid(string rule) { return rule == 고정 || rule == 변동; }

        /// <summary>그 해 금액. 연도별로 확인한 금액 → (고정 규칙이면) 마스터 금액 순. 모르면 null.</summary>
        public static decimal? 금액(Occurrence o)
        {
            if (!o.Item.납부있음) return null;
            if (o.실제금액 != null) return o.실제금액.금액;
            if (o.Item.금액규칙 == 고정) return o.Item.고정금액;
            return null;
        }

        /// <summary>납부할 돈이 있는데 금액을 모르는 건. 빈칸으로 두면 0원으로 오해한다.</summary>
        public static bool 미확인(Occurrence o)
        {
            return o.Item.납부있음 && !금액(o).HasValue;
        }
    }

    /// <summary>납부 마스터의 한 행. 매년 반복되는 기한 정의.</summary>
    public class PaymentItem
    {
        public string Id;
        public string 기관;
        public string 비용명;
        public Flow 진행흐름;
        public int 월;
        public bool 말일;          // true면 그 달의 마지막 날
        public int 일;             // 말일이 false일 때만 유효
        public int 알림영업일;      // 기한 며칠(영업일) 전에 알릴지
        public string 금액규칙 = AmountRules.변동;
        public decimal? 고정금액;
        public string 비고 = "";
        public string 홈페이지명 = "";
        public string 홈페이지주소 = "";

        /// <summary>
        /// 분할납부 묶음 이름. 같은 값을 가진 항목은 한 문서에서 온 회차들이다 (AC-W33).
        /// 비어 있으면 단독 항목.
        /// </summary>
        public string 묶음 = "";

        public string 표시명
        {
            get { return 비용명 + " (" + 기관 + ")"; }
        }

        /// <summary>돈을 내는 건인가. 제출만 흐름은 금액이 해당 없다.</summary>
        public bool 납부있음 { get { return 진행흐름 != Flow.제출만; } }

        /// <summary>지정 연도의 원 기한일. 말일이면 윤년을 반영한다.</summary>
        public DateTime 원기한일(int year)
        {
            int d = 말일 ? DateTime.DaysInMonth(year, 월) : 일;
            return new DateTime(year, 월, d);
        }
    }

    /// <summary>
    /// 그 해의 실제 납부금액. 고지서·통보문에서 확인한 값을 담는다.
    /// 마스터의 고정금액과 달리 연도별로 달라지므로 따로 관리한다.
    /// </summary>
    public class AmountRecord
    {
        public int 연도;
        public string Id;
        public decimal 금액;
        public string 출처 = "";
        public DateTime? 확인일;
        public string 비고 = "";

        public string Key { get { return 연도 + "\t" + Id; } }
    }

    /// <summary>
    /// 단계별 증빙 파일. 신고서·납부 영수증 등을 붙여 둔다.
    /// 원본이 옮겨지거나 지워져도 남도록 증빙 폴더로 복사해 보관한다.
    /// </summary>
    public class Attachment
    {
        public const string 증빙 = "증빙";
        public const string 받은문서 = "받은문서";

        public int 연도;
        public string Id;
        public string 단계;        // 첨부 시점의 단계명
        public string 저장파일;     // 증빙 폴더 기준 상대 경로
        public string 원본파일명;
        public DateTime 첨부일시;

        /// <summary>받은문서 = 고지서·통보문처럼 받은 것 / 증빙 = 처리 후 붙여 둔 것 (AC-W103).</summary>
        public string 종류 = 증빙;

        public string Key { get { return 연도 + "\t" + Id; } }
    }

    /// <summary>특정 연도에 실제로 발생하는 하나의 기한 이벤트.</summary>
    public class Occurrence
    {
        public PaymentItem Item;
        public int 연도;
        public DateTime 원기한일;
        public DateTime 보정기한일;   // 주말·공휴일이면 다음 영업일
        public DateTime 알림일;
        public bool 공휴일자료없음;   // 해당 연도 공휴일 정보가 없어 보수적으로 계산함
        public AmountRecord 실제금액;  // 그 해 확인된 금액. 없으면 null

        public string Key { get { return 연도 + "\t" + Item.Id; } }

        /// <summary>보정 기한까지 남은 영업일 수. 지났으면 음수.</summary>
        public int 남은영업일(BusinessDayCalendar cal, DateTime today)
        {
            return cal.BusinessDaysBetween(today, 보정기한일);
        }
    }

    /// <summary>진행 상태 한 건.</summary>
    public class StatusRecord
    {
        public int 연도;
        public string Id;
        public int 단계;              // Stages 배열의 인덱스
        public DateTime? 변경일시;
        public DateTime? 최종확인일;
        public string 메모 = "";

        /// <summary>
        /// 이번 실행에서 사용자가 실제로 바꾼 기록인지. 파일에 저장하지 않는다.
        /// 저장할 때 이 표시가 있는 것만 덮어써서, 팝업과 보드가 동시에 떠 있어도
        /// 한쪽이 다른 쪽의 변경을 지우지 않게 한다.
        /// </summary>
        public bool 변경됨;

        public string Key { get { return 연도 + "\t" + Id; } }
    }

    /// <summary>누가 언제 무엇을 바꿨는지 한 줄 (ADR-0004).</summary>
    public class StatusEvent
    {
        public long Rid;
        public DateTime 시각;
        public int 연도;
        public string Id;
        public string 동작;        // 진행 / 대기 / 되돌리기 / 금액 / 금액삭제 / 첨부 / 첨부삭제
        public int? 이전단계;
        public int? 이후단계;
        public string 출처;        // 팝업 / 웹 / 보드
        public string 내용 = "";
    }

    /// <summary>화면에 표시되는 한 줄. Occurrence + 현재 상태.</summary>
    public class AlertRow
    {
        public Occurrence Occ;
        public StatusRecord Status;

        /// <summary>이 세션에서 단계를 바꿨는가.</summary>
        public bool 오늘단계변경;

        public Flow Flow { get { return Occ.Item.진행흐름; } }
        public string[] 단계목록 { get { return Stages.For(Flow); } }
        public int 단계 { get { return Status.단계; } }
        public string 현재단계명 { get { return 단계목록[단계]; } }
        public bool 최종단계도달 { get { return 단계 >= Stages.FinalIndex(Flow); } }

        public string 다음단계명
        {
            get { return 최종단계도달 ? null : 단계목록[단계 + 1]; }
        }

        /// <summary>지금 누를 행동 (예: 전표 발행). 끝났으면 null.</summary>
        public string 다음행동 { get { return Stages.다음행동(Flow, 단계); } }

        /// <summary>오늘 처리되었는가. 닫기 가능 판정에 쓰인다.</summary>
        public bool 오늘처리됨(DateTime today)
        {
            if (오늘단계변경) return true;
            if (최종단계도달) return true;
            return Status.최종확인일.HasValue && Status.최종확인일.Value.Date == today.Date;
        }
    }
}
