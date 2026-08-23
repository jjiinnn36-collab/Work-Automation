using System;
using System.Collections.Generic;

namespace PaymentAlert
{
    /// <summary>진행흐름 종류.</summary>
    public enum Flow { 신고납부, 납부만, 제출만 }

    /// <summary>진행흐름별 단계 정의.</summary>
    public static class Stages
    {
        public static readonly string[] 신고납부 = { "신고 전", "신고완료", "전표결재", "납부완료" };
        public static readonly string[] 납부만 = { "납부 전", "전표결재", "납부완료" };
        public static readonly string[] 제출만 = { "제출 전", "제출완료" };

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
        public string 금액규칙;
        public decimal? 고정금액;
        public string 비고;

        public string 표시명
        {
            get { return 비용명 + " (" + 기관 + ")"; }
        }

        /// <summary>지정 연도의 원 기한일. 말일이면 윤년을 반영한다.</summary>
        public DateTime 원기한일(int year)
        {
            int d = 말일 ? DateTime.DaysInMonth(year, 월) : 일;
            return new DateTime(year, 월, d);
        }
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

        public string Key { get { return 연도 + "\t" + Id; } }
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

        /// <summary>오늘 처리되었는가. 닫기 가능 판정에 쓰인다.</summary>
        public bool 오늘처리됨(DateTime today)
        {
            if (오늘단계변경) return true;
            if (최종단계도달) return true;
            return Status.최종확인일.HasValue && Status.최종확인일.Value.Date == today.Date;
        }
    }
}
