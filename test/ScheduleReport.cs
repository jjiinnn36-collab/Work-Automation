using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// 실제 공휴일 자료로 지정 연도의 전체 일정을 출력한다.
    /// 사람이 눈으로 검산하기 위한 것. run-tests.bat 에서 호출한다.
    /// </summary>
    static class ScheduleReport
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var ko = new CultureInfo("ko-KR");

            int year = 2026;
            if (args.Length > 0) int.TryParse(args[0], out year);

            Holidays.Cache cache = Holidays.Load("data\\holidays.tsv");
            Console.WriteLine(string.Format(
                "\n공휴일 자료: {0}건, 대상 연도 {1}",
                cache.Dates.Count, string.Join("/", ToStrings(cache.Years))));

            if (!cache.Years.Contains(year))
            {
                Console.WriteLine("경고: " + year + "년 공휴일 자료가 없습니다.");
            }

            var cal = new BusinessDayCalendar(cache.Dates.Keys, cache.Years);
            var warnings = new List<string>();
            // 실제 납부 자료로 연간 일정을 출력한다. 저장소에는 없으므로(사내 정보)
            // 클론 직후에는 픽스처로 대신 출력해 동작만 확인할 수 있게 한다.
            string masterPath = "data\\payment-master.tsv";
            if (!File.Exists(masterPath))
            {
                masterPath = "test\\fixtures\\master-fixture.tsv";
                Console.WriteLine("data\\payment-master.tsv 가 없어 픽스처로 대신 출력합니다.");
            }
            List<PaymentItem> master = Repository.LoadMaster(masterPath, warnings);
            foreach (string w in warnings) Console.WriteLine("  경고: " + w);

            var amountWarn = new List<string>();
            var amounts = Repository.LoadAmounts("data\\amounts.tsv", amountWarn);
            foreach (string w in amountWarn) Console.WriteLine("  경고: " + w);
            Console.WriteLine("금액 자료: " + amounts.Count + "건");

            var occs = Scheduler.BuildOccurrences(master, cal, new DateTime(year, 6, 15), amounts);
            var list = new List<Occurrence>();
            foreach (Occurrence o in occs) if (o.연도 == year) list.Add(o);
            list.Sort(delegate(Occurrence a, Occurrence b) { return a.알림일.CompareTo(b.알림일); });

            Console.WriteLine("\n" + year + "년 납부 기한 일정 (실제 공휴일 반영)");
            Console.WriteLine(new string('-', 100));
            Console.WriteLine(string.Format("{0,-13} {1,-18} {2,-14} {3,-14} {4,-14} {5,-8} {6}",
                "id", "비용명", "원기한", "보정기한", "알림일", "흐름", "영업일/금액"));
            Console.WriteLine(new string('-', 100));

            string prevAlert = "";
            foreach (Occurrence o in list)
            {
                string alert = o.알림일.ToString("MM-dd(ddd)", ko);
                string dup = (alert == prevAlert) ? "  <- 같은 날 중복" : "";
                prevAlert = alert;

                string moved = o.원기한일 == o.보정기한일 ? "" : " *";

                string money = o.실제금액 != null
                    ? string.Format("{0,15:N0}원", o.실제금액.금액)
                    : "              -";
                Console.WriteLine(string.Format("{0,-13} {1,-18} {2,-14} {3,-14} {4,-14} {5,-8} {6}일 {7}{8}",
                    o.Item.Id, o.Item.비용명,
                    o.원기한일.ToString("MM-dd(ddd)", ko),
                    o.보정기한일.ToString("MM-dd(ddd)", ko) + moved,
                    alert,
                    o.Item.진행흐름, o.Item.알림영업일, money, dup));
            }

            Console.WriteLine(new string('-', 100));
            Console.WriteLine("* 표시 = 주말/공휴일이어서 다음 영업일로 밀린 건");

            // 같은 날 여러 건이 겹치는지 확인
            var byAlert = new Dictionary<string, List<string>>();
            foreach (Occurrence o in list)
            {
                string k = o.알림일.ToString("MM-dd");
                if (!byAlert.ContainsKey(k)) byAlert[k] = new List<string>();
                byAlert[k].Add(o.Item.비용명);
            }
            Console.WriteLine("\n알림이 겹치는 날");
            bool any = false;
            var keys = new List<string>(byAlert.Keys);
            keys.Sort();
            foreach (string k in keys)
            {
                if (byAlert[k].Count < 2) continue;
                any = true;
                Console.WriteLine("  " + k + "  " + byAlert[k].Count + "건 : " + string.Join(", ", byAlert[k].ToArray()));
            }
            if (!any) Console.WriteLine("  없음");

            // 검산: 알림일부터 기한까지 영업일 수가 설정과 맞는지
            Console.WriteLine("\n역산 검증");
            int bad = 0;
            foreach (Occurrence o in list)
            {
                int n = cal.BusinessDaysBetween(o.알림일, o.보정기한일);
                if (n != o.Item.알림영업일)
                {
                    bad++;
                    Console.WriteLine(string.Format("  불일치: {0} 설정 {1}일 / 실제 {2}일",
                        o.Item.Id, o.Item.알림영업일, n));
                }
            }
            Console.WriteLine(bad == 0
                ? "  전체 " + list.Count + "건 모두 설정한 영업일 수와 일치"
                : "  " + bad + "건 불일치");

            return bad == 0 ? 0 : 1;
        }

        static string[] ToStrings(IEnumerable<int> years)
        {
            var l = new List<string>();
            foreach (int y in years) l.Add(y.ToString());
            l.Sort();
            return l.ToArray();
        }
    }
}
