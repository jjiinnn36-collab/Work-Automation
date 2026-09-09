using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaymentAlert
{
    /// <summary>납부 마스터와 진행 상태 파일 입출력.</summary>
    public static class Repository
    {
        public const int 기본알림영업일 = 3;

        public static List<PaymentItem> LoadMaster(string path, List<string> warnings)
        {
            var items = new List<PaymentItem>();
            var rows = Tsv.Read(path);
            if (rows.Count == 0)
                throw new FileNotFoundException("납부 마스터를 읽지 못했습니다: " + path);

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lineNo = 1;

            foreach (var row in rows)
            {
                lineNo++;
                string id = Tsv.Get(row, "id");
                if (id.Length == 0) continue;

                if (!seenIds.Add(id))
                {
                    warnings.Add(string.Format("{0}행: id '{1}' 가 중복되어 건너뜁니다.", lineNo, id));
                    continue;
                }

                var it = new PaymentItem();
                it.Id = id;
                it.기관 = Tsv.Get(row, "기관");
                it.비용명 = Tsv.Get(row, "비용명");
                it.금액규칙 = Tsv.Get(row, "금액규칙");
                it.고정금액 = Tsv.GetDecimal(row, "고정금액");
                it.비고 = Tsv.Get(row, "비고");

                try { it.진행흐름 = Stages.Parse(Tsv.Get(row, "진행흐름")); }
                catch (FormatException ex)
                {
                    warnings.Add(string.Format("{0}행({1}): {2} 건너뜁니다.", lineNo, id, ex.Message));
                    continue;
                }

                int month = Tsv.GetInt(row, "월", 0);
                if (month < 1 || month > 12)
                {
                    warnings.Add(string.Format("{0}행({1}): 월 값이 잘못되어 건너뜁니다.", lineNo, id));
                    continue;
                }
                it.월 = month;

                string dayText = Tsv.Get(row, "일");
                if (dayText.Equals("EOM", StringComparison.OrdinalIgnoreCase) || dayText == "말일")
                {
                    it.말일 = true;
                }
                else
                {
                    int day;
                    if (!int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out day)
                        || day < 1 || day > 31)
                    {
                        warnings.Add(string.Format("{0}행({1}): 일 값 '{2}' 이 잘못되어 건너뜁니다.", lineNo, id, dayText));
                        continue;
                    }
                    it.일 = day;
                }

                // AC-17: 비었거나 잘못된 값이면 기본 3. 절대 중단하지 않는다.
                int lead = Tsv.GetInt(row, "알림영업일", 기본알림영업일);
                if (lead < 1 || lead > 60)
                {
                    if (Tsv.Get(row, "알림영업일").Length > 0)
                        warnings.Add(string.Format("{0}행({1}): 알림영업일 값이 유효하지 않아 기본 {2}일을 적용합니다.",
                            lineNo, id, 기본알림영업일));
                    lead = 기본알림영업일;
                }
                it.알림영업일 = lead;

                items.Add(it);
            }
            return items;
        }

        /// <summary>
        /// 추적 시작일을 읽는다. 이 날짜보다 기한이 이른 건은 아예 다루지 않는다.
        /// 프로그램을 쓰기 전의 건들이 '미처리'로 잡히는 것을 막기 위한 것이다.
        /// 파일이 없거나 형식이 잘못되면 null(제한 없음)을 돌려준다.
        /// </summary>
        public static DateTime? LoadStartDate(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    DateTime d;
                    if (DateTime.TryParse(line, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out d))
                        return d.Date;
                    return null;   // 첫 유효 줄이 날짜가 아니면 설정하지 않은 것으로 본다
                }
            }
            catch { /* 설정을 못 읽는다고 알림이 멈추면 안 된다 */ }
            return null;
        }

        /// <summary>
        /// 연도별 실제 납부금액을 읽는다. 파일이 없으면 빈 사전을 돌려준다.
        /// 금액을 모른다고 알림이 멈추면 안 되므로 없어도 정상 동작해야 한다.
        /// </summary>
        public static Dictionary<string, AmountRecord> LoadAmounts(string path, List<string> warnings)
        {
            var map = new Dictionary<string, AmountRecord>(StringComparer.Ordinal);
            int lineNo = 1;

            foreach (var row in Tsv.Read(path))
            {
                lineNo++;
                string id = Tsv.Get(row, "id");
                int year = Tsv.GetInt(row, "연도", 0);
                if (id.Length == 0 || year == 0) continue;

                decimal? amount = Tsv.GetDecimal(row, "금액");
                if (!amount.HasValue)
                {
                    if (warnings != null)
                        warnings.Add(string.Format("금액 자료 {0}행({1} {2}): 금액을 숫자로 읽지 못해 건너뜁니다.",
                            lineNo, year, id));
                    continue;
                }

                var a = new AmountRecord();
                a.연도 = year;
                a.Id = id;
                a.금액 = amount.Value;
                a.출처 = Tsv.Get(row, "출처");
                a.확인일 = Tsv.GetDate(row, "확인일");
                a.비고 = Tsv.Get(row, "비고");
                map[a.Key] = a;
            }
            return map;
        }

        public static Dictionary<string, StatusRecord> LoadStatus(string path)
        {
            var map = new Dictionary<string, StatusRecord>(StringComparer.Ordinal);
            foreach (var row in Tsv.Read(path))
            {
                string id = Tsv.Get(row, "id");
                int year = Tsv.GetInt(row, "연도", 0);
                if (id.Length == 0 || year == 0) continue;

                var st = new StatusRecord();
                st.연도 = year;
                st.Id = id;
                st.단계 = Tsv.GetInt(row, "단계", 0);
                st.변경일시 = Tsv.GetDate(row, "변경일시");
                st.최종확인일 = Tsv.GetDate(row, "최종확인일");
                st.메모 = Tsv.Get(row, "메모");
                map[st.Key] = st;
            }
            return map;
        }

        /// <summary>
        /// 진행 상태를 저장한다.
        /// 파일을 다시 읽어 이번 실행에서 건드리지 않은 기록은 그대로 두고,
        /// 사용자가 실제로 바꾼 것만 덮어쓴다. 팝업과 보드가 동시에 떠 있어도
        /// 한쪽이 다른 쪽의 변경을 지우지 않는다.
        /// </summary>
        public static void SaveStatus(string path, IEnumerable<StatusRecord> records, List<PaymentItem> master)
        {
            var flowById = new Dictionary<string, Flow>(StringComparer.OrdinalIgnoreCase);
            foreach (PaymentItem it in master) flowById[it.Id] = it.진행흐름;

            // 디스크에 있는 내용을 기준으로 삼는다.
            Dictionary<string, StatusRecord> merged = LoadStatus(path);

            foreach (StatusRecord st in records)
            {
                if (!st.변경됨) continue;          // 손대지 않은 기록은 건드리지 않는다
                merged[st.Key] = st;
            }

            var list = new List<StatusRecord>(merged.Values);
            list.Sort(delegate(StatusRecord a, StatusRecord b)
            {
                int c = a.연도.CompareTo(b.연도);
                return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            var rows = new List<string[]>();
            foreach (StatusRecord st in list)
            {
                // 사람이 읽을 수 있도록 단계 인덱스와 이름을 함께 남긴다.
                string stageName = "";
                Flow f;
                if (flowById.TryGetValue(st.Id, out f))
                {
                    string[] stages = Stages.For(f);
                    if (st.단계 >= 0 && st.단계 < stages.Length) stageName = stages[st.단계];
                }

                rows.Add(new string[] {
                    st.연도.ToString(CultureInfo.InvariantCulture),
                    st.Id,
                    st.단계.ToString(CultureInfo.InvariantCulture),
                    stageName,
                    st.변경일시.HasValue ? st.변경일시.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    st.최종확인일.HasValue ? st.최종확인일.Value.ToString("yyyy-MM-dd") : "",
                    st.메모 ?? ""
                });
            }

            Tsv.Write(path,
                new string[] { "연도", "id", "단계", "단계명", "변경일시", "최종확인일", "메모" },
                rows,
                new string[] { "이 파일은 프로그램이 관리합니다. 직접 편집하지 마세요." });
        }
    }
}
