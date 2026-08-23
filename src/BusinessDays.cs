using System;
using System.Collections.Generic;

namespace PaymentAlert
{
    /// <summary>주말과 공휴일을 제외한 영업일 계산.</summary>
    public class BusinessDayCalendar
    {
        readonly HashSet<DateTime> holidays;
        readonly HashSet<int> coveredYears;

        public BusinessDayCalendar(IEnumerable<DateTime> holidayDates, IEnumerable<int> years)
        {
            holidays = new HashSet<DateTime>();
            foreach (DateTime d in holidayDates) holidays.Add(d.Date);
            coveredYears = new HashSet<int>();
            foreach (int y in years) coveredYears.Add(y);
        }

        /// <summary>해당 연도의 공휴일 자료를 갖고 있는가.</summary>
        public bool HasYear(int year) { return coveredYears.Contains(year); }

        public bool IsHoliday(DateTime d) { return holidays.Contains(d.Date); }

        public bool IsBusinessDay(DateTime d)
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) return false;
            return !IsHoliday(d);
        }

        /// <summary>영업일이 아니면 다음 영업일로 민다. 영업일이면 그대로.</summary>
        public DateTime NextBusinessDayOrSame(DateTime d)
        {
            DateTime x = d.Date;
            int guard = 0;
            while (!IsBusinessDay(x))
            {
                x = x.AddDays(1);
                if (++guard > 400) throw new InvalidOperationException("영업일을 찾지 못했습니다. 공휴일 자료를 확인하세요.");
            }
            return x;
        }

        /// <summary>from에서 n영업일 앞선 날짜. n=3이면 3영업일 전.</summary>
        public DateTime SubtractBusinessDays(DateTime from, int n)
        {
            DateTime x = from.Date;
            int moved = 0;
            int guard = 0;
            while (moved < n)
            {
                x = x.AddDays(-1);
                if (IsBusinessDay(x)) moved++;
                if (++guard > 1000) throw new InvalidOperationException("영업일 역산에 실패했습니다.");
            }
            return x;
        }

        /// <summary>from(제외)부터 to(포함)까지의 영업일 수. to가 과거면 음수.</summary>
        public int BusinessDaysBetween(DateTime from, DateTime to)
        {
            DateTime a = from.Date, b = to.Date;
            if (a == b) return 0;

            int sign = b > a ? 1 : -1;
            DateTime lo = sign > 0 ? a : b;
            DateTime hi = sign > 0 ? b : a;

            int count = 0;
            DateTime x = lo.AddDays(1);
            while (x <= hi)
            {
                if (IsBusinessDay(x)) count++;
                x = x.AddDays(1);
            }
            return count * sign;
        }
    }
}
