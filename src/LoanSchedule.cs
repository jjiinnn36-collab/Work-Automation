using System;
using System.Collections.Generic;
using System.Globalization;

namespace PaymentAlert
{
    /// <summary>스케줄의 이자 한 줄.</summary>
    public sealed class LoanRow
    {
        public DateTime 날짜;
        public decimal 금액;
    }

    /// <summary>분기 한 번의 지급. 지급일은 ERP 스케줄 날짜 그대로다.</summary>
    public sealed class LoanPayment
    {
        public int 회차;
        public DateTime 지급일;
        public DateTime 기간시작;          // 직전 지급일(첫 회차는 차입일)
        public decimal 금액;
        public readonly List<LoanRow> 행 = new List<LoanRow>();
        public bool 예상과다름;
    }

    /// <summary>차입 한 건 (시트 하나).</summary>
    public sealed class LoanPlan
    {
        public string 시트 = "";
        public string 거래처 = "";
        public string 차입명 = "", 약칭 = "";   // 사용자가 넣는 이름 (파일에는 없음)
        public DateTime 차입일;
        public decimal 액면;
        public decimal? 이율;              // % 단위 (3.969 = 3.969%)
        public DateTime? 만기;
        public decimal 이자합계;
        public readonly List<LoanPayment> 지급 = new List<LoanPayment>();
        public readonly List<string> 경고 = new List<string>();

        /// <summary>액면 × 이율 ÷ 4. 이율이 없으면 null.</summary>
        public decimal? 분기예상
        {
            get { return 이율.HasValue ? Math.Round(액면 * 이율.Value / 100m / 4m, 0, MidpointRounding.AwayFromZero) : (decimal?)null; }
        }
    }

    /// <summary>
    /// ERP 차입관리 지급스케줄(양식: 차입관리스케줄 양식.xlsx)을 분기 지급으로 묶는다 (ADR-0023).
    ///
    /// 스케줄은 매월 말일의 경과이자 행과, 분기 달에 '지급일까지' 끊은 조각 행으로 되어 있다.
    /// 이자는 분기마다 한 번, 차입일로부터 3·6·9·12개월 되는 날에 경과이자를 한꺼번에 낸다.
    /// 그래서 지급일 = 그 날짜 근처(±7일)의 이자 행 날짜(조각 행을 우선), 지급액 = 직전 지급일 다음 행부터 지급일 행까지의 합이다.
    /// 날짜는 ERP 가 정한 그대로 쓴다 — 주말이라 ERP 가 앞당긴 날짜(예: 9/4 토 → 9/3)를 바꾸지 않는다.
    /// </summary>
    public static class LoanSchedule
    {
        public const string 구분신규 = "신규";
        public const string 구분이자 = "이자지급";
        public const string 구분상환 = "상환";
        const int 지급일허용일 = 7;

        public sealed class 형식오류 : Exception
        {
            public 형식오류(string message) : base(message) { }
        }

        /// <summary>이 시트가 차입 스케줄 양식인가 (머리글 행이 있는가).</summary>
        public static bool 양식인가(Sheet sheet)
        {
            return 머리글찾기(sheet) >= 0;
        }

        static int 머리글찾기(Sheet sheet)
        {
            for (int r = 0; r < sheet.행.Count && r < 15; r++)
            {
                string[] row = sheet.행[r];
                if (열(row, "기준일자") >= 0 && 열(row, "현금흐름구분") >= 0 && 열(row, "액면이자금액") >= 0) return r;
            }
            return -1;
        }

        static int 열(string[] row, string name)
        {
            for (int i = 0; i < row.Length; i++)
                if (정리(row[i]) == name) return i;
            return -1;
        }

        static string 정리(string s)
        {
            return (s ?? "").Replace(" ", "").Replace(" ", "").Trim();
        }

        static string 칸(string[] row, int i)
        {
            return i >= 0 && i < row.Length ? (row[i] ?? "").Trim() : "";
        }

        /// <summary>시트를 해석한다. 쓸 수 없는 시트면 형식오류.</summary>
        public static LoanPlan Parse(Sheet sheet)
        {
            int h = 머리글찾기(sheet);
            if (h < 0) throw new 형식오류("차입 스케줄 양식이 아닙니다 (기준일자·현금흐름구분·액면이자금액 열이 없음).");
            string[] head = sheet.행[h];
            int c날짜 = 열(head, "기준일자"), c구분 = 열(head, "현금흐름구분"), c이자 = 열(head, "액면이자금액");
            int c액면 = 열(head, "액면금액"), c이율 = 열(head, "액면이자율"), c거래처 = 열(head, "거래처명");

            var plan = new LoanPlan();
            plan.시트 = sheet.이름 ?? "";
            bool 신규있음 = false;
            decimal? 합계행 = null;
            var rows = new List<LoanRow>();

            for (int r = h + 1; r < sheet.행.Count; r++)
            {
                string[] row = sheet.행[r];
                string 구분 = 정리(칸(row, c구분));
                string 날짜글 = 칸(row, c날짜);
                int 줄 = r + 1;

                if (구분.Length == 0 && 날짜글.Length == 0)
                {
                    // 합계행(Σ): 이자 합계만 검산에 쓴다.
                    decimal t;
                    if (금액읽기(칸(row, c이자), out t) && t > 0) 합계행 = t;
                    continue;
                }

                DateTime d;
                if (!날짜읽기(날짜글, out d))
                    throw new 형식오류(줄 + "행: 기준일자 '" + 날짜글 + "' 를 날짜로 읽을 수 없습니다.");

                if (plan.거래처.Length == 0) plan.거래처 = 칸(row, c거래처);

                if (구분 == 구분신규)
                {
                    if (신규있음) throw new 형식오류(줄 + "행: '신규' 행이 두 번 있습니다. 차입건마다 시트를 나눠 주세요.");
                    신규있음 = true;
                    plan.차입일 = d;
                    decimal face;
                    if (!금액읽기(칸(row, c액면), out face) || face <= 0)
                        throw new 형식오류(줄 + "행: 액면금액을 읽을 수 없습니다.");
                    plan.액면 = face;
                    decimal rate;
                    if (비율읽기(칸(row, c이율), out rate)) plan.이율 = rate;
                }
                else if (구분 == 구분이자)
                {
                    decimal amt;
                    if (!금액읽기(칸(row, c이자), out amt))
                        throw new 형식오류(줄 + "행: 액면이자금액을 읽을 수 없습니다.");
                    if (amt < 0) throw new 형식오류(줄 + "행: 액면이자금액이 음수입니다.");
                    var lr = new LoanRow();
                    lr.날짜 = d;
                    lr.금액 = amt;
                    rows.Add(lr);
                }
                else if (구분 == 구분상환)
                {
                    if (!plan.만기.HasValue) plan.만기 = d;
                }
                else
                {
                    plan.경고.Add(줄 + "행: 모르는 현금흐름구분 '" + 구분 + "' 은 건너뜁니다.");
                }
            }

            if (!신규있음) throw new 형식오류("'신규' 행(차입일·액면금액)이 없습니다.");
            if (rows.Count == 0) throw new 형식오류("'이자지급' 행이 없습니다.");
            if (plan.거래처.Length == 0) plan.거래처 = plan.시트;

            rows.Sort(delegate(LoanRow a, LoanRow b) { return a.날짜.CompareTo(b.날짜); });
            // 차입일 당일 행은 받는다 — 월말에 차입하면 그날 하루치 경과이자 행이 차입일 날짜로 생긴다.
            if (rows[0].날짜 < plan.차입일)
                throw new 형식오류("이자 행(" + D(rows[0].날짜) + ")이 차입일(" + D(plan.차입일) + ") 이전입니다.");
            foreach (LoanRow x in rows) plan.이자합계 += x.금액;

            분기로묶기(plan, rows);

            if (합계행.HasValue && 합계행.Value != plan.이자합계)
                plan.경고.Add(string.Format(CultureInfo.InvariantCulture,
                    "합계행의 이자({0:N0})와 이자 행의 합({1:N0})이 다릅니다.", 합계행.Value, plan.이자합계));
            return plan;
        }

        static void 분기로묶기(LoanPlan plan, List<LoanRow> rows)
        {
            DateTime 마지막행 = rows[rows.Count - 1].날짜;
            DateTime 직전 = plan.차입일;
            // 이 날짜 '다음' 행부터 이번 회차에 넣는다. 첫 회차는 차입일 당일 행도 넣어야 하므로 하루 앞.
            DateTime 하한 = plan.차입일.AddDays(-1);
            int used = 0;
            decimal? 예상 = plan.분기예상;

            for (int k = 1; ; k++)
            {
                DateTime 기준 = plan.차입일.AddMonths(3 * k);
                if (기준 > 마지막행.AddDays(지급일허용일)) break;

                LoanRow 지급행 = null;
                foreach (LoanRow x in rows)
                {
                    if (x.날짜 <= 직전) continue;   // 지급일은 차입일·직전 지급일보다 뒤
                    if (Math.Abs((x.날짜 - 기준).TotalDays) > 지급일허용일) continue;
                    if (지급행 == null || 더나은지급행(x, 지급행, 기준)) 지급행 = x;
                }
                if (지급행 == null)
                    throw new 형식오류(k + "회차 지급일(" + D(기준) + " 무렵)의 이자 행을 찾지 못했습니다.");

                var p = new LoanPayment();
                p.회차 = k;
                p.지급일 = 지급행.날짜;
                p.기간시작 = 직전;
                foreach (LoanRow x in rows)
                {
                    if (x.날짜 <= 하한 || x.날짜 > 지급행.날짜) continue;
                    p.행.Add(x);
                    p.금액 += x.금액;
                    used++;
                }
                if (예상.HasValue && Math.Abs(p.금액 - 예상.Value) > Math.Max(10m, 예상.Value * 0.02m))
                {
                    p.예상과다름 = true;
                    plan.경고.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}회차 이자 {1:N0}원이 액면×이율÷4({2:N0}원)와 다릅니다. 스케줄을 확인하세요.", k, p.금액, 예상.Value));
                }
                plan.지급.Add(p);
                직전 = 지급행.날짜;
                하한 = 지급행.날짜;
            }

            if (used != rows.Count)
                throw new 형식오류(string.Format(CultureInfo.InvariantCulture,
                    "마지막 지급일({0}) 뒤에 이자 행이 {1}개 남습니다. 분기 지급일과 맞지 않는 스케줄입니다.", D(직전), rows.Count - used));
        }

        /// <summary>
        /// 지급일 후보 비교: 월말이 아닌 행(지급일까지 끊은 조각)이 먼저, 같으면 기준일에 가까운 것.
        /// 차입일이 월말 근처면 조각이 없고 월말 행이 곧 지급일이다.
        /// </summary>
        static bool 더나은지급행(LoanRow a, LoanRow b, DateTime 기준)
        {
            bool aEnd = 월말(a.날짜), bEnd = 월말(b.날짜);
            if (aEnd != bEnd) return !aEnd;
            return Math.Abs((a.날짜 - 기준).TotalDays) < Math.Abs((b.날짜 - 기준).TotalDays);
        }

        static bool 월말(DateTime d) { return d.Day == DateTime.DaysInMonth(d.Year, d.Month); }

        static string D(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        // ── 값 읽기 ─────────────────────────────────────────────

        static readonly string[] 날짜형식 = { "yyyy-MM-dd", "yyyyMMdd", "yyyy.MM.dd", "yyyy/MM/dd", "yyyy-M-d", "yyyy.M.d", "yyyy/M/d" };

        /// <summary>글자 날짜 또는 엑셀 날짜 일련번호(1900 체계).</summary>
        public static bool 날짜읽기(string s, out DateTime d)
        {
            string t = (s ?? "").Trim();
            if (t.Length >= 10 && t.IndexOf(' ') > 0) t = t.Substring(0, t.IndexOf(' '));   // "2026-09-04 00:00:00"
            if (DateTime.TryParseExact(t, 날짜형식, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return true;
            double serial;
            if (t.Length <= 12 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out serial) &&
                serial >= 20000 && serial < 80000)
            {
                d = DateTime.FromOADate(Math.Floor(serial));
                return true;
            }
            d = DateTime.MinValue;
            return false;
        }

        /// <summary>"12,500,000" · "12500000" · "12500000.0" · "12,500,000원". 원 단위 아래는 받지 않는다.</summary>
        public static bool 금액읽기(string s, out decimal v)
        {
            string t = (s ?? "").Replace(",", "").Replace("원", "").Replace(" ", "").Trim();
            v = 0;
            if (t.Length == 0) return false;
            if (!decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            if (v != Math.Truncate(v)) return false;
            return true;
        }

        /// <summary>이율: "3.969" · "3.9689999999999999" · "3.969%". 소수 셋째 자리까지 쓴다.</summary>
        static bool 비율읽기(string s, out decimal v)
        {
            string t = (s ?? "").Replace("%", "").Trim();
            v = 0;
            double d;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d <= 0 || d >= 100) return false;
            v = Math.Round((decimal)d, 4, MidpointRounding.AwayFromZero);
            return true;
        }
    }
}
