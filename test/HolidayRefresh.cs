using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// 공휴일 자료를 API로 강제 갱신한다. 프로그램의 실제 갱신 코드를 그대로 탄다.
    /// 사용법: HolidayRefresh.exe [연도...]   (기본 2025 2026 2027)
    /// </summary>
    static class HolidayRefresh
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string path = "data\\holidays.tsv";
            string keyPath = "data\\apikey.txt";

            var years = new List<int>();
            foreach (string a in args)
            {
                int y;
                if (int.TryParse(a, out y)) years.Add(y);
            }
            if (years.Count == 0) { years.Add(2025); years.Add(2026); years.Add(2027); }

            if (!File.Exists(keyPath))
            {
                Console.WriteLine("data\\apikey.txt 가 없습니다.");
                return 1;
            }
            string key = File.ReadAllText(keyPath, Encoding.UTF8).Trim();

            Holidays.Cache cache = Holidays.Load(path);
            Console.WriteLine(string.Format("갱신 전: {0}건, 최종 갱신 {1}",
                cache.Dates.Count,
                cache.Updated.HasValue ? cache.Updated.Value.ToString("yyyy-MM-dd") : "없음"));

            var before = new Dictionary<DateTime, string>(cache.Dates);

            // 갱신 조건을 만족시키기 위해 최종 갱신일을 지운다.
            cache.Updated = null;

            string message;
            bool ok = Holidays.TryRefresh(cache, key, years, out message);
            Console.WriteLine("결과: " + (ok ? "성공" : "실패"));
            Console.WriteLine("메시지: " + (message ?? "(없음)"));

            if (!ok) return 1;

            // 변경분 출력
            var added = new List<DateTime>();
            foreach (DateTime d in cache.Dates.Keys)
                if (!before.ContainsKey(d)) added.Add(d);
            added.Sort();

            var changed = new List<DateTime>();
            foreach (KeyValuePair<DateTime, string> kv in cache.Dates)
            {
                string old;
                if (before.TryGetValue(kv.Key, out old) && old != kv.Value) changed.Add(kv.Key);
            }
            changed.Sort();

            Console.WriteLine();
            Console.WriteLine("추가된 공휴일: " + added.Count + "건");
            foreach (DateTime d in added)
                Console.WriteLine("  + " + d.ToString("yyyy-MM-dd") + "  " + cache.Dates[d]);

            Console.WriteLine("명칭 변경: " + changed.Count + "건");
            foreach (DateTime d in changed)
                Console.WriteLine("  ~ " + d.ToString("yyyy-MM-dd") + "  " + before[d] + " -> " + cache.Dates[d]);

            // API에 없는데 파일에만 있는 건 (수동 추가분) 은 지우지 않는다.
            Console.WriteLine();
            Console.WriteLine("갱신 후: " + cache.Dates.Count + "건");

            Holidays.Save(path, cache);
            Console.WriteLine("저장 완료: " + path);
            return 0;
        }
    }
}
