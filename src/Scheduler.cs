using System;
using System.Collections.Generic;

namespace PaymentAlert
{
    /// <summary>오늘 표시할 항목 선정 결과.</summary>
    public class RowSet
    {
        /// <summary>팝업에 표시하고 처리를 강제하는 건.</summary>
        public List<AlertRow> Rows = new List<AlertRow>();

        /// <summary>유예 기간까지 지난 미처리 건. 요약으로만 알린다.</summary>
        public List<AlertRow> Overdue = new List<AlertRow>();
    }

    /// <summary>마스터와 상태를 조합해 오늘 표시할 항목을 결정한다.</summary>
    public static class Scheduler
    {
        /// <summary>공휴일 자료가 없는 연도에 적용할 안전 여유(일).</summary>
        const int 안전여유일 = 2;

        /// <summary>
        /// 기한이 지난 뒤에도 이만큼(영업일)은 계속 강제 표시한다.
        /// 이 기간을 넘기면 요약으로만 알린다. 이미 늦은 건을 매일 붙잡으면
        /// 사용자가 프로그램 자체를 꺼버리고, 그러면 앞으로의 기한까지 놓친다.
        /// </summary>
        const int 기한후유예영업일 = 5;

        /// <summary>계산 대상 연도. 작년 미완료 건도 요약에는 잡힌다.</summary>
        public static List<int> TargetYears(DateTime today)
        {
            return new List<int> { today.Year - 1, today.Year, today.Year + 1 };
        }

        public static List<Occurrence> BuildOccurrences(
            List<PaymentItem> items, BusinessDayCalendar cal, DateTime today)
        {
            var result = new List<Occurrence>();
            foreach (int year in TargetYears(today))
            {
                foreach (PaymentItem item in items)
                {
                    var occ = new Occurrence();
                    occ.Item = item;
                    occ.연도 = year;
                    occ.원기한일 = item.원기한일(year);
                    occ.보정기한일 = cal.NextBusinessDayOrSame(occ.원기한일);
                    occ.알림일 = cal.SubtractBusinessDays(occ.보정기한일, item.알림영업일);

                    // 공휴일 자료가 없으면 영업일을 과다 계산해 알림이 늦어질 수 있다.
                    // 늦는 쪽이 위험하므로 여유를 두고 앞당긴다.
                    if (!cal.HasYear(year))
                    {
                        occ.공휴일자료없음 = true;
                        occ.알림일 = occ.알림일.AddDays(-안전여유일);
                    }

                    result.Add(occ);
                }
            }
            return result;
        }

        /// <summary>
        /// 오늘 팝업에 띄울 행과, 유예를 넘긴 미처리 건을 나눠 돌려준다.
        /// 강제 표시 조건: 알림일 &lt;= 오늘 &lt;= 기한 + 유예, 최종 단계 아님, 오늘 미확인.
        /// </summary>
        public static RowSet BuildRows(
            List<Occurrence> occurrences,
            Dictionary<string, StatusRecord> statusMap,
            BusinessDayCalendar cal,
            DateTime today)
        {
            var set = new RowSet();

            foreach (Occurrence occ in occurrences)
            {
                if (occ.알림일.Date > today.Date) continue;      // 아직 알릴 때가 아님

                StatusRecord st;
                if (!statusMap.TryGetValue(occ.Key, out st))
                {
                    st = new StatusRecord();
                    st.연도 = occ.연도;
                    st.Id = occ.Item.Id;
                    st.단계 = 0;
                    statusMap[occ.Key] = st;
                }

                // 단계 값이 범위를 벗어나면 보정한다. 잘못된 파일 때문에 죽지 않는다.
                int last = Stages.FinalIndex(occ.Item.진행흐름);
                if (st.단계 < 0) st.단계 = 0;
                if (st.단계 > last) st.단계 = last;

                if (st.단계 >= last) continue;                   // 이미 끝난 건

                var row = new AlertRow();
                row.Occ = occ;
                row.Status = st;

                // 기한 + 유예를 넘겼으면 강제 표시하지 않고 요약으로 돌린다.
                DateTime cutoff = AddBusinessDays(cal, occ.보정기한일, 기한후유예영업일);
                if (today.Date > cutoff.Date)
                {
                    set.Overdue.Add(row);
                    continue;
                }

                // AC-24b: 오늘 이미 확인한 건은 다시 묻지 않는다.
                if (st.최종확인일.HasValue && st.최종확인일.Value.Date == today.Date) continue;

                set.Rows.Add(row);
            }

            set.Rows.Sort(ByDueDate);
            set.Overdue.Sort(ByDueDate);
            return set;
        }

        static int ByDueDate(AlertRow a, AlertRow b)
        {
            int c = a.Occ.보정기한일.CompareTo(b.Occ.보정기한일);
            if (c != 0) return c;
            return string.Compare(a.Occ.Item.Id, b.Occ.Item.Id, StringComparison.Ordinal);
        }

        static DateTime AddBusinessDays(BusinessDayCalendar cal, DateTime from, int n)
        {
            DateTime x = from.Date;
            int moved = 0, guard = 0;
            while (moved < n)
            {
                x = x.AddDays(1);
                if (cal.IsBusinessDay(x)) moved++;
                if (++guard > 1000) break;
            }
            return x;
        }
    }
}
