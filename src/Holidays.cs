using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace PaymentAlert
{
    /// <summary>
    /// 공휴일 자료 관리. 공공데이터포털 특일정보 API로 받아 로컬 파일에 캐시한다.
    /// 일상 실행은 캐시만 읽으므로 네트워크가 끊겨도 알림은 멈추지 않는다.
    /// </summary>
    public static class Holidays
    {
        // 주의: 경로는 B090041/openapi/service 이다.
        // B090041_OPEN_API/service 로 쓰면 NO_OPENAPI_SERVICE_ERROR(코드 12)가 돌아온다.
        const string ApiBase =
            "https://apis.data.go.kr/B090041/openapi/service/SpcdeInfoService/getRestDeInfo";

        /// <summary>캐시를 다시 받아올 주기.</summary>
        const int 갱신주기일 = 30;

        public class Cache
        {
            public Dictionary<DateTime, string> Dates = new Dictionary<DateTime, string>();
            public HashSet<int> Years = new HashSet<int>();
            public DateTime? Updated;

            public int 경과일
            {
                get { return Updated.HasValue ? (int)(DateTime.Today - Updated.Value.Date).TotalDays : int.MaxValue; }
            }
        }

        public static Cache Load(string path)
        {
            var c = new Cache();
            if (!File.Exists(path)) return c;

            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("#"))
                {
                    string[] meta = line.Substring(1).Split('\t');
                    if (meta.Length >= 2 && meta[0].Trim() == "updated")
                    {
                        DateTime u;
                        if (DateTime.TryParse(meta[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out u))
                            c.Updated = u;
                    }
                    continue;
                }

                string[] cells = line.Split('\t');
                if (cells.Length < 1) continue;
                if (cells[0].Trim() == "날짜") continue;   // 헤더

                DateTime d;
                if (DateTime.TryParseExact(cells[0].Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                {
                    c.Dates[d.Date] = cells.Length > 1 ? cells[1].Trim() : "";
                    c.Years.Add(d.Year);
                }
            }
            return c;
        }

        public static void Save(string path, Cache c)
        {
            var rows = new List<string[]>();
            var keys = new List<DateTime>(c.Dates.Keys);
            keys.Sort();
            foreach (DateTime d in keys)
                rows.Add(new string[] { d.ToString("yyyy-MM-dd"), c.Dates[d] });

            var years = new List<int>(c.Years);
            years.Sort();
            var yearText = new List<string>();
            foreach (int y in years) yearText.Add(y.ToString(CultureInfo.InvariantCulture));

            Tsv.Write(path,
                new string[] { "날짜", "명칭" },
                rows,
                new string[] {
                    "updated\t" + DateTime.Today.ToString("yyyy-MM-dd"),
                    "years\t" + string.Join(",", yearText.ToArray()),
                    "이 파일은 자동으로 갱신됩니다. 직접 편집해도 됩니다 (날짜는 yyyy-MM-dd 형식)."
                });
        }

        /// <summary>
        /// 필요한 연도의 자료가 없거나 오래되었으면 API로 갱신을 시도한다.
        /// 실패해도 예외를 던지지 않는다. 기존 캐시로 계속 동작해야 하기 때문이다.
        /// </summary>
        /// <returns>갱신에 성공했으면 true</returns>
        public static bool TryRefresh(Cache cache, string apiKey, IEnumerable<int> neededYears, out string message)
        {
            message = null;

            var missing = new List<int>();
            foreach (int y in neededYears)
                if (!cache.Years.Contains(y)) missing.Add(y);

            bool stale = cache.경과일 >= 갱신주기일;
            if (missing.Count == 0 && !stale) return false;   // 갱신할 필요 없음

            // 갱신이 필요한 시점에만 키 부재를 알린다. 그렇지 않으면 매 실행마다 로그가 쌓인다.
            if (string.IsNullOrEmpty(apiKey))
            {
                message = "공휴일 자동 갱신이 필요하지만 data/apikey.txt 가 없습니다. "
                        + "data/holidays.tsv 를 직접 채우거나 공공데이터포털 서비스키를 넣어 주세요.";
                return false;
            }

            var target = new List<int>(missing);
            if (stale)
                foreach (int y in neededYears)
                    if (!target.Contains(y)) target.Add(y);

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                int added = 0;
                foreach (int year in target)
                {
                    for (int month = 1; month <= 12; month++)
                        added += FetchMonth(cache, apiKey, year, month);
                    cache.Years.Add(year);
                }
                cache.Updated = DateTime.Today;
                message = string.Format("공휴일 {0}건을 갱신했습니다.", added);
                return true;
            }
            catch (WebException wex)
            {
                message = "공휴일 자동 갱신 실패 — " + DescribeWebError(wex);
                return false;
            }
            catch (Exception ex)
            {
                message = "공휴일 자동 갱신에 실패했습니다: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 공공데이터포털은 오류를 HTTP 상태가 아니라 응답 본문의 errMsg 로 알려준다.
        /// 사용자가 무엇을 해야 하는지 알 수 있게 풀어서 돌려준다.
        /// </summary>
        static string DescribeWebError(WebException wex)
        {
            string body = null;
            if (wex.Response != null)
            {
                try
                {
                    using (var sr = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                        body = sr.ReadToEnd();
                }
                catch { }
            }

            if (string.IsNullOrEmpty(body))
                return "네트워크에 연결하지 못했습니다. 사내에서 apis.data.go.kr 접속이 막혀 있을 수 있습니다. (" + wex.Message + ")";

            if (body.IndexOf("SERVICE_KEY_IS_NOT_REGISTERED_ERROR", StringComparison.Ordinal) >= 0)
                return "서비스키가 등록되어 있지 않습니다. 공공데이터포털에서 "
                     + "'한국천문연구원_특일 정보' 활용신청을 하셨는지 확인하세요. "
                     + "신청 직후에는 반영까지 시간이 걸릴 수 있습니다.";

            if (body.IndexOf("SERVICE_ACCESS_DENIED_ERROR", StringComparison.Ordinal) >= 0)
                return "해당 서비스에 대한 사용 권한이 없습니다. 활용신청 승인 상태를 확인하세요.";

            if (body.IndexOf("NO_OPENAPI_SERVICE_ERROR", StringComparison.Ordinal) >= 0)
                return "API 주소가 잘못되었거나 서비스가 폐기되었습니다.";

            if (body.IndexOf("LIMITED_NUMBER_OF_SERVICE_REQUESTS_EXCEEDS_ERROR", StringComparison.Ordinal) >= 0)
                return "일일 호출 한도를 초과했습니다. 내일 다시 시도됩니다.";

            int s = body.IndexOf("<errMsg>", StringComparison.Ordinal);
            int e = body.IndexOf("</errMsg>", StringComparison.Ordinal);
            if (s >= 0 && e > s)
                return body.Substring(s + 8, e - s - 8);

            return wex.Message;
        }

        static int FetchMonth(Cache cache, string apiKey, int year, int month)
        {
            string url = string.Format(
                "{0}?serviceKey={1}&solYear={2}&solMonth={3:00}&numOfRows=50&_type=xml",
                ApiBase, apiKey, year, month);

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.UserAgent = "PaymentAlert";
            req.Proxy = WebRequest.GetSystemWebProxy();

            string xml;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                xml = sr.ReadToEnd();

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNodeList items = doc.GetElementsByTagName("item");
            int added = 0;
            foreach (XmlNode item in items)
            {
                XmlNode locdate = item["locdate"];
                XmlNode isHoliday = item["isHoliday"];
                XmlNode dateName = item["dateName"];

                if (locdate == null) continue;
                if (isHoliday != null && isHoliday.InnerText.Trim() != "Y") continue;

                DateTime d;
                if (!DateTime.TryParseExact(locdate.InnerText.Trim(), "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;

                cache.Dates[d.Date] = dateName != null ? dateName.InnerText.Trim() : "";
                cache.Years.Add(d.Year);
                added++;
            }
            return added;
        }
    }
}
