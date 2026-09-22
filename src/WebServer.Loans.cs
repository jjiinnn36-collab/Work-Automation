using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;

namespace PaymentAlert
{
    /// <summary>
    /// ERP 차입관리 스케줄 가져오기 (ADR-0023).
    /// POST /api/import/loans?mode=preview|save[&only=시트,시트][&nm=시트|차입명|약칭 …]  본문 = 파일 그대로, X-File-Name = 파일 이름.
    /// 새 차입건을 저장할 때는 차입명이 있어야 한다. 화면의 차입처 자리에는 약칭, 없으면 차입명을 쓴다.
    /// 미리보기는 아무것도 쓰지 않는다. 저장은 새 회차만 넣고 이미 있는 회차는 건드리지 않는다.
    /// </summary>
    public sealed partial class WebServer
    {
        public const string 차입출처 = "ERP 차입스케줄";
        const int 차입알림영업일 = 5;

        static int? 연도칸(string s, string 이름)
        {
            string t = (s ?? "").Trim();
            if (t.Length == 0) return null;
            int y;
            if (!int.TryParse(t, NumberStyles.None, Inv, out y) || y < 2000 || y > 2100)
                throw new HttpError(400, 이름 + "는 2000~2100 사이 연도여야 합니다.");
            return y;
        }

        JObj 차입가져오기(Env env, HttpListenerRequest req)
        {
            NameValueCollection q = Query(req);
            string mode = q["mode"] ?? "preview";
            if (mode != "preview" && mode != "save") throw new HttpError(400, "mode 는 preview 또는 save 여야 합니다.");

            string raw = req.Headers["X-File-Name"];
            if (string.IsNullOrEmpty(raw)) throw new HttpError(400, "파일 이름이 없습니다.");
            string name;
            try { name = 파일이름(Uri.UnescapeDataString(raw)); }
            catch (HttpError) { throw; }
            catch { throw new HttpError(400, "파일 이름이 올바르지 않습니다."); }

            byte[] data = 본문읽기(req, SheetReader.최대크기, "20MB 보다 큰 파일은 읽지 않습니다.");

            List<Sheet> sheets;
            try { sheets = SheetReader.Read(data, name); }
            catch (SheetReader.읽기오류 ex) { throw new HttpError(400, ex.Message); }

            HashSet<string> only = null;
            if (!string.IsNullOrEmpty(q["only"]))
            {
                only = new HashSet<string>(StringComparer.Ordinal);
                foreach (string s in q["only"].Split(',')) if (s.Trim().Length > 0) only.Add(s.Trim());
            }

            Dictionary<string, decimal> 고친금액 = 고친금액읽기(req.RawUrl);
            Dictionary<string, string[]> 이름들 = 차입이름읽기(req.RawUrl);

            // 연장스케줄 업로드: 이미 가져온 차입건에 새 회차를 이어 붙인다.
            Store.차입건 대상 = null;
            if (q["extend"] != null)
            {
                string g = q["extend"].Trim();
                foreach (Store.차입건 x in env.Db.차입목록()) if (x.묶음 == g) 대상 = x;
                if (대상 == null) throw new HttpError(404, "그런 차입건이 없습니다: " + g);
            }

            var loans = new List<object>();
            var skipped = new List<object>();
            var created = new List<PaymentItem>();
            int 새회차 = 0, 있던회차 = 0;

            var 할일 = new List<차입할일>();
            foreach (Sheet sheet in sheets)
            {
                if (!LoanSchedule.양식인가(sheet))
                {
                    bool 비었음 = true;
                    foreach (string[] row in sheet.행)
                        foreach (string c in row) if (!string.IsNullOrEmpty(c) && c.Trim().Length > 0) { 비었음 = false; break; }
                    skipped.Add(new JObj().Set("sheet", sheet.이름)
                        .Set("reason", 비었음 ? "빈 시트" : "차입 스케줄 양식이 아님 (기준일자·현금흐름구분·액면이자금액 열이 없음)"));
                    continue;
                }

                LoanPlan plan;
                try { plan = LoanSchedule.Parse(sheet); }
                catch (LoanSchedule.형식오류 ex)
                {
                    loans.Add(new JObj().Set("sheet", sheet.이름).Set("ok", false).Set("error", ex.Message));
                    continue;
                }

                string 묶음;
                int 앞회차 = 0;
                List<object> checks = null;
                if (대상 != null)
                {
                    if (!같은거래처(plan.거래처, 대상.거래처))
                    {
                        skipped.Add(new JObj().Set("sheet", sheet.이름)
                            .Set("reason", "다른 거래처 (" + plan.거래처 + ") — " + 대상.거래처 + " 차입건에 붙일 수 없음"));
                        continue;
                    }
                    묶음 = 대상.묶음;
                    // 같은 연장 파일을 다시 올려도 번호가 밀리지 않게, 이 스케줄의 첫 지급일보다 앞선 회차만 센다.
                    앞회차 = 묶음회차수(env, 묶음, plan.지급[0].지급일);
                    checks = 연장확인(plan, 대상);
                }
                else
                {
                    묶음 = null;   // 차입명이 정해진 뒤(아래) 차입명 기준으로 기존 건을 찾는다.
                }

                var job = new 차입할일();
                job.Sheet = sheet; job.Plan = plan; job.묶음 = 묶음; job.앞회차 = 앞회차; job.Checks = checks;
                job.선택됨 = only == null || only.Contains(sheet.이름);
                할일.Add(job);
            }

            // 이름을 먼저 정하고, 그 다음 차입명 기준으로 기존 차입건(같은 거래처·차입일·차입명)을 찾는다.
            // 거래처만 같은 다른 사업은 새 건으로 들어간다 (사용자 요청 2026-09-22).
            foreach (차입할일 job in 할일)
            {
                if (대상 != null)
                {
                    job.Plan.차입명 = 대상.차입명; job.Plan.약칭 = 대상.약칭;   // 연장: 대상 이름을 잇는다.
                    continue;
                }
                string[] nm;
                if (이름들.TryGetValue(job.Sheet.이름, out nm)) { job.Plan.차입명 = nm[0]; job.Plan.약칭 = nm[1]; }
                // 차입명이 비면 시트명을 차입명으로 쓴다 (사용자 요청 2026-09-22).
                if (job.Plan.차입명 == null || job.Plan.차입명.Trim().Length == 0) job.Plan.차입명 = (job.Sheet.이름 ?? "").Trim();
                job.묶음 = env.Db.차입묶음(job.Plan.거래처, job.Plan.차입일, job.Plan.차입명);
            }

            foreach (차입할일 job in 할일)
            {
                Sheet sheet = job.Sheet; LoanPlan plan = job.Plan; string 묶음 = job.묶음; int 앞회차 = job.앞회차;
                List<object> checks = job.Checks;
                bool 선택됨 = job.선택됨;
                JObj dto = 차입Dto(env, plan, 묶음, 앞회차);

                if (mode == "save" && 선택됨)
                {
                    if (묶음 == null) 묶음 = env.Db.새항목아이디();
                    var items = new List<PaymentItem>();
                    var amounts = new Dictionary<string, AmountRecord>(StringComparer.Ordinal);
                    foreach (LoanPayment p in plan.지급)
                    {
                        PaymentItem it = 차입회차(plan, p, 묶음, 앞회차);
                        items.Add(it);
                        decimal 고친값;
                        bool 고침 = 고친금액.TryGetValue(고친금액키(sheet.이름, p.지급일), out 고친값);
                        var a = new AmountRecord();
                        a.연도 = p.지급일.Year;
                        a.Id = it.Id;
                        a.금액 = 고침 ? 고친값 : p.금액;
                        a.출처 = 고침 && 고친값 != p.금액 ? 차입출처 + " (고침)" : 차입출처;
                        a.확인일 = env.Today;
                        a.비고 = 근거(p);
                        amounts[it.Id] = a;
                    }
                    List<string> added = env.Db.차입가져오기(묶음, plan, items, amounts, name, 출처, 대상 != null ? "연장" : "가져오기");
                    foreach (PaymentItem it in items) if (added.Contains(it.Id)) created.Add(it);
                    새회차 += added.Count;
                    있던회차 += items.Count - added.Count;
                    dto = 차입Dto(env, plan, 묶음, 앞회차).Set("group", 묶음).Set("added", added.Count);
                }
                if (checks != null) dto.Set("checks", checks);
                loans.Add(dto.Set("selected", 선택됨));
            }

            var result = new JObj()
                .Set("file", name).Set("mode", mode)
                .Set("loans", loans).Set("skipped", skipped);
            if (대상 != null)
                result.Set("extend", new JObj()
                    .Set("group", 대상.묶음).Set("org", 대상.표시이름)
                    .Set("maturity", 대상.만기.HasValue ? 대상.만기.Value.ToString("yyyy-MM-dd", Inv) : null)
                    .Set("count", 묶음회차수(env, 대상.묶음, DateTime.MaxValue)));
            if (mode == "save")
                result.Set("added", 새회차).Set("existing", 있던회차).Set("popup", 팝업확인(env, created));
            return result;
        }

        /// <summary>GET /api/loans — 가져온 차입건 목록.</summary>
        JObj 차입목록(Env env)
        {
            var list = new List<object>();
            foreach (Store.차입건 x in env.Db.차입목록())
            {
                list.Add(new JObj()
                    .Set("group", x.묶음).Set("org", x.표시이름).Set("name", x.차입명).Set("short", x.약칭)
                    .Set("start", x.차입일.ToString("yyyy-MM-dd", Inv))
                    .Set("face", x.액면).Set("rate", x.이율)
                    .Set("maturity", x.만기.HasValue ? x.만기.Value.ToString("yyyy-MM-dd", Inv) : null)
                    .Set("count", 묶음회차수(env, x.묶음, DateTime.MaxValue))
                    .Set("file", x.파일));
            }
            return Head(env).Set("loans", list);
        }

        /// <summary>묶음의 회차 중 지급일이 기준일보다 앞선 것의 수.</summary>
        static int 묶음회차수(Env env, string 묶음, DateTime 기준)
        {
            int n = 0;
            foreach (PaymentItem it in env.Db.LoadMaster())
            {
                if (it.묶음 != 묶음) continue;
                if (기준 == DateTime.MaxValue || (it.시작연도.HasValue && it.원기한일(it.시작연도.Value) < 기준)) n++;
            }
            return n;
        }

        static bool 같은거래처(string a, string b)
        {
            return string.Equals((a ?? "").Replace(" ", ""), (b ?? "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>연장이 맞는지 보이는 항목. level: ok / warn.</summary>
        static List<object> 연장확인(LoanPlan plan, Store.차입건 대상)
        {
            var list = new List<object>();
            list.Add(확인줄("거래처", plan.거래처, "ok"));
            list.Add(확인줄("액면", plan.액면.ToString("N0", Inv) + (plan.액면 == 대상.액면 ? "" : " (지금 " + 대상.액면.ToString("N0", Inv) + ")"),
                plan.액면 == 대상.액면 ? "ok" : "warn"));
            if (대상.만기.HasValue)
            {
                bool 이어짐 = Math.Abs((plan.차입일 - 대상.만기.Value).TotalDays) <= 7;
                list.Add(확인줄("새 스케줄 시작", plan.차입일.ToString("yyyy-MM-dd", Inv) +
                    (이어짐 ? " (지금 만기에서 이어짐)" : " (지금 만기 " + 대상.만기.Value.ToString("yyyy-MM-dd", Inv) + "과 떨어짐)"),
                    이어짐 ? "ok" : "warn"));
            }
            if (plan.이율.HasValue)
            {
                bool 같음 = 대상.이율.HasValue && 대상.이율.Value == plan.이율.Value;
                list.Add(확인줄("이율", (같음 || !대상.이율.HasValue ? "" : 대상.이율.Value.ToString("0.###", Inv) + "% → ") +
                    plan.이율.Value.ToString("0.###", Inv) + "%", 같음 ? "ok" : "warn"));
            }
            list.Add(확인줄("새 만기", plan.만기.HasValue ? plan.만기.Value.ToString("yyyy-MM-dd", Inv) : "—", "ok"));
            return list;
        }

        static JObj 확인줄(string label, string value, string level)
        {
            return new JObj().Set("label", label).Set("value", value).Set("level", level);
        }

        /// <summary>
        /// 등록 전에 고친 지급액: ov=지급일|금액|시트 (여러 개).
        /// 공용 쿼리 해석(Pairs)은 같은 이름을 덮어쓰므로 주소에서 ov 만 따로 모두 읽는다.
        /// </summary>
        static Dictionary<string, decimal> 고친금액읽기(string rawUrl)
        {
            var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (string v in 반복값(rawUrl, "ov", "고친 금액 형식이 올바르지 않습니다."))
            {
                string[] parts = (v ?? "").Split(new[] { '|' }, 3);
                DateTime d;
                if (parts.Length != 3 || !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out d))
                    throw new HttpError(400, "고친 금액 형식이 올바르지 않습니다.");
                map[고친금액키(parts[2], d)] = 금액(parts[1], "지급액");
            }
            return map;
        }

        sealed class 차입할일
        {
            public Sheet Sheet;
            public LoanPlan Plan;
            public string 묶음;
            public int 앞회차;
            public List<object> Checks;
            public bool 선택됨;
        }

        /// <summary>주소에서 같은 이름의 값을 모두 읽는다 (Pairs 는 덮어쓴다).</summary>
        static List<string> 반복값(string rawUrl, string key, string 오류)
        {
            var values = new List<string>();
            string raw = rawUrl ?? "";
            int qm = raw.IndexOf('?');
            if (qm < 0) return values;
            foreach (string pair in raw.Substring(qm + 1).Split('&'))
            {
                if (!pair.StartsWith(key + "=", StringComparison.Ordinal)) continue;
                try { values.Add(Uri.UnescapeDataString(pair.Substring(key.Length + 1).Replace('+', ' '))); }
                catch { throw new HttpError(400, 오류); }
            }
            if (values.Count > 500) throw new HttpError(400, 오류);
            return values;
        }

        /// <summary>차입명·약칭: nm=시트|차입명|약칭 (시트마다). 차입명 60자, 약칭 20자.</summary>
        static Dictionary<string, string[]> 차입이름읽기(string rawUrl)
        {
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string v in 반복값(rawUrl, "nm", "차입명 형식이 올바르지 않습니다."))
            {
                string[] parts = (v ?? "").Split(new[] { '|' }, 3);
                if (parts.Length != 3) throw new HttpError(400, "차입명 형식이 올바르지 않습니다.");
                string 이름 = parts[1].Trim(), 약칭 = parts[2].Trim();
                if (이름.Length > 60) throw new HttpError(400, "차입명은 60자까지입니다.");
                if (약칭.Length > 20) throw new HttpError(400, "약칭은 20자까지입니다.");
                if ((이름 + 약칭).IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) throw new HttpError(400, "차입명에 줄바꿈은 쓸 수 없습니다.");
                map[parts[0]] = new[] { 이름, 약칭 };
            }
            return map;
        }

        static string 고친금액키(string sheet, DateTime 지급일)
        {
            return (sheet ?? "") + "\n" + 지급일.ToString("yyyy-MM-dd", Inv);
        }

        /// <summary>미리보기·저장 결과에 같이 쓰는 차입건 요약. 회차마다 새로/이미 있음/금액 다름을 붙인다.</summary>
        /// <param name="앞회차">연장이면 이미 있는 회차 수 — 회차 번호를 그 뒤로 잇는다.</param>
        static JObj 차입Dto(Env env, LoanPlan plan, string 묶음, int 앞회차)
        {
            // 저장 직후에는 env.Master 가 옛 목록이라 DB 에서 다시 읽는다.
            var 있는항목 = new Dictionary<string, PaymentItem>(StringComparer.Ordinal);
            var 금액들 = env.Amounts;
            if (묶음 != null)
            {
                foreach (PaymentItem it in env.Db.LoadMaster()) if (it.묶음 == 묶음) 있는항목[it.Id] = it;
                금액들 = env.Db.LoadAmounts();
            }

            var rows = new List<object>();
            int 새로 = 0, 있음 = 0, 다름 = 0;
            foreach (LoanPayment p in plan.지급)
            {
                string id = 묶음 == null ? null : 회차아이디(묶음, p);
                string state = "new";
                decimal? 기존금액 = null;
                if (id != null && 있는항목.ContainsKey(id))
                {
                    AmountRecord a;
                    if (금액들.TryGetValue(p.지급일.Year + "\t" + id, out a)) 기존금액 = a.금액;
                    state = 기존금액.HasValue && 기존금액.Value != p.금액 ? "diff" : "exists";
                }
                if (state == "new") 새로++; else if (state == "exists") 있음++; else 다름++;

                DateTime 실제 = env.Cal.NextBusinessDayOrSame(p.지급일);
                var parts = new List<object>();
                foreach (LoanRow r in p.행)
                    parts.Add(new JObj().Set("date", r.날짜.ToString("yyyy-MM-dd", Inv)).Set("amount", r.금액));
                rows.Add(new JObj()
                    .Set("no", p.회차 + 앞회차)
                    .Set("id", id)
                    .Set("from", p.기간시작.ToString("yyyy-MM-dd", Inv))
                    .Set("scheduled", p.지급일.ToString("yyyy-MM-dd", Inv))
                    .Set("payDue", 실제.ToString("yyyy-MM-dd", Inv))
                    .Set("shifted", 실제 != p.지급일)
                    .Set("amount", p.금액)
                    .Set("unexpected", p.예상과다름)
                    .Set("rows", parts)
                    .Set("state", state)
                    .Set("existingAmount", 기존금액));
            }

            return new JObj()
                .Set("sheet", plan.시트)
                .Set("ok", true)
                .Set("org", plan.거래처)
                .Set("name", plan.차입명)
                .Set("short", plan.약칭)
                .Set("start", plan.차입일.ToString("yyyy-MM-dd", Inv))
                .Set("face", plan.액면)
                .Set("rate", plan.이율)
                .Set("maturity", plan.만기.HasValue ? plan.만기.Value.ToString("yyyy-MM-dd", Inv) : null)
                .Set("interestTotal", plan.이자합계)
                .Set("quarterExpected", plan.분기예상)
                .Set("known", 묶음 != null)
                .Set("payments", rows)
                .Set("newCount", 새로).Set("existingCount", 있음).Set("diffCount", 다름)
                .Set("warnings", plan.경고);
        }

        /// <summary>묶음 번호 + 지급 연월. 같은 차입건을 다시 올리면 같은 id 가 나온다.</summary>
        static string 회차아이디(string 묶음, LoanPayment p)
        {
            return 묶음 + "-" + p.지급일.ToString("yyyyMM", Inv);
        }

        static PaymentItem 차입회차(LoanPlan plan, LoanPayment p, string 묶음, int 앞회차)
        {
            var it = new PaymentItem();
            it.Id = 회차아이디(묶음, p);
            it.기관 = 자르기(Store.차입표시이름(plan.약칭, plan.차입명, plan.거래처), 60);
            it.차입 = true;
            // 회차 번호는 이름에 넣지 않는다 — 화면 어디에도 'N회차' 를 보이지 않는다 (사용자 요청 2026-09-18).
            it.비용명 = "차입금 이자";
            // 납부만 흐름의 버튼(전표 발행 → 납부완료)을 쓰되 고지서수령 대신 '지급액 확인' 에서 시작한다 (Stages.차입이자, 사용자 요청 2026-09-18).
            it.진행흐름 = Flow.납부만;
            it.월 = p.지급일.Month;
            // 2월 29일은 그 해에만 있으므로 말일로 둔다. 유효연도가 한 해라 뜻은 같다.
            if (p.지급일.Month == 2 && p.지급일.Day == DateTime.DaysInMonth(p.지급일.Year, 2) && p.지급일.Day == 29)
            {
                it.말일 = true;
                it.일 = 0;
            }
            else it.일 = p.지급일.Day;
            it.알림영업일 = 차입알림영업일;
            it.금액규칙 = AmountRules.변동;
            it.시작연도 = p.지급일.Year;
            it.종료연도 = p.지급일.Year;
            it.묶음 = 묶음;
            // 비고는 이자기간만 (사용자 요청 2026-09-18). 차입일·액면·이율은 loans 표에 있다.
            it.비고 = "이자기간 " + p.기간시작.ToString("yyyy-MM-dd", Inv) + "~" + p.지급일.AddDays(-1).ToString("yyyy-MM-dd", Inv);
            return it;
        }

        /// <summary>금액 근거: 합산한 스케줄 행.</summary>
        static string 근거(LoanPayment p)
        {
            var parts = new List<string>();
            foreach (LoanRow r in p.행)
                parts.Add(r.날짜.ToString("M/d", Inv) + " " + r.금액.ToString("N0", Inv));
            return 자르기(string.Join(" + ", parts.ToArray()), 200);
        }

        static string 자르기(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        static byte[] 본문읽기(HttpListenerRequest req, long max, string 너무큼)
        {
            if (req.ContentLength64 == 0) throw new HttpError(400, "빈 파일입니다.");
            if (req.ContentLength64 > max) throw new HttpError(413, 너무큼);
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > max) throw new HttpError(413, 너무큼);
                    ms.Write(buf, 0, n);
                }
                if (ms.Length == 0) throw new HttpError(400, "빈 파일입니다.");
                return ms.ToArray();
            }
        }
    }
}
