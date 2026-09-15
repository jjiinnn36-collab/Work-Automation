using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PaymentAlert
{
    /// <summary>요청을 거절할 때 쓰는 예외. 상태 코드와 화면에 보일 문구를 담는다.</summary>
    public sealed class HttpError : Exception
    {
        public readonly int Status;
        public HttpError(int status, string message) : base(message) { Status = status; }
    }

    /// <summary>
    /// 내 PC 에서만 열리는 웹 화면 서버. 자료는 팝업·보드와 같은 자료 DB 를 읽고 쓴다.
    /// 요청은 한 번에 하나씩 처리하고, 요청마다 DB 를 새로 연다.
    /// 그래서 팝업이 같은 시각에 써도 서로 옛 자료를 붙들고 있지 않는다.
    /// </summary>
    public sealed class WebServer : IDisposable
    {
        public const int 기본포트 = 8317;
        const long 첨부최대 = 50L * 1024 * 1024;
        const int 양식최대 = 64 * 1024;

        static readonly CultureInfo Ko = new CultureInfo("ko-KR");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Regex 아이디형식 = new Regex("^[A-Za-z0-9_-]{1,40}$");

        readonly string dataDir;
        readonly string webRoot;
        readonly Func<DateTime> 오늘;
        HttpListener listener;
        Thread thread;

        public int Port { get; private set; }
        public string Url { get { return "http://localhost:" + Port + "/"; } }

        /// <summary>처리 중 난 오류를 남길 곳. 없으면 버린다.</summary>
        public Action<string> Log;

        /// <summary>
        /// 항목을 고친 결과 그 항목이 오늘 알릴 건이 되면 부른다. 인자는 항목 id.
        /// 팝업을 실제로 띄우는 일은 실행 프로그램이 맡는다.
        /// </summary>
        public Action<string> 팝업요청;

        public WebServer(string dataDir, string webRoot, Func<DateTime> 오늘)
        {
            this.dataDir = dataDir;
            this.webRoot = webRoot;
            this.오늘 = 오늘;
        }

        /// <summary>시작포트부터 10개를 차례로 시도해 비어 있는 곳에서 연다.</summary>
        public void Start(int 시작포트)
        {
            for (int p = 시작포트; p < 시작포트 + 10; p++)
            {
                var l = new HttpListener();
                // localhost 로만 받는다. 관리자 권한 없이 열리고, 다른 PC 에서는 닿지 않는다.
                l.Prefixes.Add("http://localhost:" + p + "/");
                try
                {
                    l.Start();
                    listener = l;
                    Port = p;
                    break;
                }
                catch (HttpListenerException)
                {
                    try { l.Close(); } catch { }
                }
            }
            if (listener == null)
                throw new InvalidOperationException(string.Format("웹 화면용 포트를 열지 못했습니다 ({0}~{1}).", 시작포트, 시작포트 + 9));

            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "PaymentAlert.Web";
            thread.Start();
        }

        public void Dispose()
        {
            if (listener == null) return;
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }

        void Loop()
        {
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { return; }   // 멈춤

                try { Handle(ctx); }
                catch (Exception ex)
                {
                    if (Log != null) Log("웹 요청 처리 실패: " + ctx.Request.Url + " " + ex);
                }
                finally
                {
                    try { ctx.Response.Close(); } catch { }
                }
            }
        }

        // ══ 요청 분배 ══════════════════════════════════════════════

        void Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            res.Headers["X-Content-Type-Options"] = "nosniff";
            res.Headers["Cache-Control"] = "no-store";
            res.Headers["Referrer-Policy"] = "no-referrer";

            try
            {
                // 다른 이름으로 들어온 요청(DNS 재바인딩)을 막는다.
                string host = req.Headers["Host"] ?? "";
                if (host != "localhost:" + Port && host != "127.0.0.1:" + Port)
                    throw new HttpError(403, "허용되지 않은 주소입니다.");

                string path = req.Url.AbsolutePath;
                if (req.HttpMethod == "GET")
                {
                    if (path.StartsWith("/api/")) Get(ctx, path);
                    else Static(res, path);
                    return;
                }

                if (req.HttpMethod != "POST") throw new HttpError(405, "지원하지 않는 요청입니다.");

                // 다른 사이트가 몰래 보내는 요청을 막는다. 화면 스크립트만 이 머리글을 붙인다.
                if (req.Headers["X-PaymentAlert"] != "1")
                    throw new HttpError(403, "허용되지 않은 요청입니다.");
                string origin = req.Headers["Origin"];
                if (origin != null && origin != "http://localhost:" + Port && origin != "http://127.0.0.1:" + Port)
                    throw new HttpError(403, "허용되지 않은 요청입니다.");

                Post(ctx, path);
            }
            catch (HttpError he)
            {
                Send(res, he.Status, new JObj().Set("error", he.Message));
            }
            catch (Exception ex)
            {
                if (Log != null) Log("웹 요청 오류: " + req.Url + " " + ex);
                Send(res, 500, new JObj().Set("error", "처리하지 못했습니다: " + ex.Message));
            }
        }

        void Static(HttpListenerResponse res, string path)
        {
            string name;
            string type;
            if (path == "/" || path == "/index.html") { name = "index.html"; type = "text/html; charset=utf-8"; }
            else if (path == "/app.js") { name = "app.js"; type = "text/javascript; charset=utf-8"; }
            else if (path == "/app.css") { name = "app.css"; type = "text/css; charset=utf-8"; }
            else throw new HttpError(404, "없는 화면입니다.");

            string full = Path.Combine(webRoot, name);
            if (!File.Exists(full)) throw new HttpError(404, "화면 파일이 없습니다: " + name);

            res.Headers["Content-Security-Policy"] =
                "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; " +
                "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            byte[] body = File.ReadAllBytes(full);
            res.StatusCode = 200;
            res.ContentType = type;
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
        }

        void Get(HttpListenerContext ctx, string path)
        {
            NameValueCollection q = Query(ctx.Request);
            using (Env env = Env.Open(dataDir, 오늘()))
            {
                switch (path)
                {
                    case "/api/alerts": Send(ctx.Response, 200, Alerts(env)); return;
                    case "/api/month": Send(ctx.Response, 200, Month(env, q["ym"])); return;
                    case "/api/year": Send(ctx.Response, 200, Year(env, q["y"])); return;
                    case "/api/items": Send(ctx.Response, 200, Items(env)); return;
                    case "/api/history": Send(ctx.Response, 200, History(env, q["from"], q["to"])); return;
                    case "/api/attachments":
                        Send(ctx.Response, 200, AttachmentList(env, 연도(q["y"]), 아이디(q["id"])));
                        return;
                    case "/api/file": SendFile(ctx.Response, env, 연도(q["y"]), 아이디(q["id"]), q["f"]); return;
                }
            }
            throw new HttpError(404, "없는 기능입니다.");
        }

        void Post(HttpListenerContext ctx, string path)
        {
            HttpListenerRequest req = ctx.Request;

            // 첨부는 본문이 파일 자체라 양식으로 읽지 않는다.
            if (path == "/api/attach")
            {
                using (Env env = Env.Open(dataDir, 오늘()))
                    Send(ctx.Response, 200, Attach(env, req));
                return;
            }

            NameValueCollection f = ReadForm(req);
            using (Env env = Env.Open(dataDir, 오늘()))
            {
                switch (path)
                {
                    case "/api/advance": Send(ctx.Response, 200, Advance(env, f)); return;
                    case "/api/defer": Send(ctx.Response, 200, Defer(env, f)); return;
                    case "/api/revert": Send(ctx.Response, 200, Revert(env, f)); return;
                    case "/api/amount": Send(ctx.Response, 200, SaveAmount(env, f)); return;
                    case "/api/amount/delete":
                        env.Db.DeleteAmount(연도(f["y"]), 아이디(f["id"]));
                        Send(ctx.Response, 200, new JObj().Set("ok", true));
                        return;
                    case "/api/item": Send(ctx.Response, 200, SaveItem(env, f)); return;
                    case "/api/item/delete": Send(ctx.Response, 200, DeleteItem(env, f)); return;
                    case "/api/attach/delete": Send(ctx.Response, 200, DeleteAttachment(env, f)); return;
                }
            }
            throw new HttpError(404, "없는 기능입니다.");
        }

        // ══ 요청 한 건 동안 쓰는 자료 묶음 ═════════════════════════

        sealed class Env : IDisposable
        {
            public Store Db;
            public DateTime Today;
            public List<PaymentItem> Master;
            public BusinessDayCalendar Cal;
            public Dictionary<string, AmountRecord> Amounts;
            public DateTime? 시작일;
            public Dictionary<string, StatusRecord> Status;
            public AttachmentStore Files;

            public static Env Open(string dataDir, DateTime today)
            {
                var e = new Env();
                e.Today = today.Date;
                e.Db = Store.Open(DataPaths.Db(dataDir));
                try
                {
                    e.Master = e.Db.LoadMaster();
                    // 공휴일 갱신은 팝업이 맡는다. 웹은 저장된 것만 읽는다.
                    Holidays.Cache cache = e.Db.LoadHolidays();
                    e.Cal = new BusinessDayCalendar(cache.Dates.Keys, cache.Years);
                    e.Amounts = e.Db.LoadAmounts();
                    e.시작일 = e.Db.LoadStartDate();
                    e.Status = e.Db.LoadStatus();
                    e.Files = new AttachmentStore(e.Db, DataPaths.증빙(dataDir));
                    e.Files.Load();
                }
                catch
                {
                    e.Db.Dispose();
                    throw;
                }
                return e;
            }

            public void Dispose() { Db.Dispose(); }

            public PaymentItem Item(string id)
            {
                foreach (PaymentItem it in Master) if (it.Id == id) return it;
                return null;
            }

            /// <summary>해당 연도가 들어가도록 발생 건을 만든다. 시작일 이전 건은 빠진다.</summary>
            public List<Occurrence> OccurrencesOf(int year)
            {
                DateTime 기준 = year == Today.Year ? Today : new DateTime(year, 7, 1);
                var all = Scheduler.BuildOccurrences(Master, Cal, 기준, Amounts, 시작일);
                var r = new List<Occurrence>();
                foreach (Occurrence o in all) if (o.연도 == year) r.Add(o);
                return r;
            }

            public StatusRecord StatusOf(int year, string id)
            {
                StatusRecord st;
                if (!Status.TryGetValue(year + "\t" + id, out st))
                {
                    st = new StatusRecord();
                    st.연도 = year;
                    st.Id = id;
                    Status[st.Key] = st;
                }
                return st;
            }
        }

        // ══ 조회 ═══════════════════════════════════════════════════

        JObj Alerts(Env env)
        {
            List<Occurrence> occs = Scheduler.BuildOccurrences(env.Master, env.Cal, env.Today, env.Amounts, env.시작일);

            // 오늘 대기를 누른 건도 화면에는 남겨 둔다. 사라지면 누른 게 먹혔는지 알 수 없다.
            var 오늘대기 = new HashSet<string>(StringComparer.Ordinal);
            foreach (StatusRecord st in env.Status.Values)
            {
                if (st.최종확인일.HasValue && st.최종확인일.Value.Date == env.Today)
                {
                    오늘대기.Add(st.Key);
                    st.최종확인일 = null;
                }
            }

            RowSet set = Scheduler.BuildRows(occs, env.Status, env.Cal, env.Today);

            var rows = new List<object>();
            int 남은 = 0;
            foreach (AlertRow r in set.Rows)
            {
                bool 대기함 = 오늘대기.Contains(r.Occ.Key);
                if (!대기함) 남은++;
                rows.Add(Dto(env, r.Occ, 대기함));
            }

            var overdue = new List<object>();
            foreach (AlertRow r in set.Overdue) overdue.Add(Dto(env, r.Occ, 오늘대기.Contains(r.Occ.Key)));

            int 알림전 = 0;
            foreach (Occurrence o in occs)
            {
                if (o.연도 != env.Today.Year || o.알림일.Date <= env.Today) continue;
                if (!Done(env, o)) 알림전++;
            }

            return Head(env)
                .Set("rows", rows)
                .Set("pending", 남은)
                .Set("overdue", overdue)
                .Set("notYet", 알림전);
        }

        JObj Month(Env env, string ym)
        {
            int y = env.Today.Year, m = env.Today.Month;
            if (!string.IsNullOrEmpty(ym))
            {
                DateTime d;
                if (!DateTime.TryParseExact(ym, "yyyy-MM", Inv, DateTimeStyles.None, out d) || d.Year < 2000 || d.Year > 2100)
                    throw new HttpError(400, "월 형식이 올바르지 않습니다 (예: 2026-09).");
                y = d.Year; m = d.Month;
            }

            var month = new List<Occurrence>();
            foreach (Occurrence o in env.OccurrencesOf(y))
                if (o.원기한일.Month == m) month.Add(o);
            month.Sort(delegate(Occurrence a, Occurrence b)
            {
                int c = a.원기한일.CompareTo(b.원기한일);
                return c != 0 ? c : string.Compare(a.Item.Id, b.Item.Id, StringComparison.Ordinal);
            });

            var list = new List<object>();
            int 진행 = 0, 완료 = 0, 지남 = 0, 미확인 = 0;
            decimal 합계 = 0;
            foreach (Occurrence o in month)
            {
                bool done = Done(env, o);
                if (done) 완료++; else 진행++;
                if (!done && o.보정기한일.Date < env.Today) 지남++;
                decimal? a = Amount(o);
                if (a.HasValue) 합계 += a.Value; else if (Unknown(o)) 미확인++;
                list.Add(Dto(env, o, false));
            }

            return Head(env)
                .Set("year", y).Set("month", m)
                .Set("rows", list)
                .Set("inProgress", 진행).Set("done", 완료).Set("overdue", 지남)
                .Set("total", 합계).Set("amountUnknown", 미확인);
        }

        JObj Year(Env env, string ys)
        {
            int y = string.IsNullOrEmpty(ys) ? env.Today.Year : 연도(ys);
            List<Occurrence> occs = env.OccurrencesOf(y);
            occs.Sort(delegate(Occurrence a, Occurrence b)
            {
                int c = a.보정기한일.CompareTo(b.보정기한일);
                return c != 0 ? c : string.Compare(a.Item.Id, b.Item.Id, StringComparison.Ordinal);
            });

            int 지난 = 0, 지난완료 = 0, 진행중 = 0, 예정 = 0, 지남 = 0, 미확인 = 0;
            var remaining = new List<object>();
            var finished = new List<object>();
            var orgs = new List<string>();
            foreach (Occurrence o in occs)
            {
                bool done = Done(env, o);
                bool 기한지남 = o.보정기한일.Date < env.Today;
                if (기한지남) { 지난++; if (done) 지난완료++; }
                if (!done)
                {
                    if (기한지남) 지남++;
                    else if (o.알림일.Date <= env.Today) 진행중++;
                    else 예정++;
                }
                if (Unknown(o)) 미확인++;
                if (!orgs.Contains(o.Item.기관)) orgs.Add(o.Item.기관);

                if (done) finished.Insert(0, Dto(env, o, false));   // 최근 것이 위로
                else remaining.Add(Dto(env, o, false));
            }
            orgs.Sort(StringComparer.CurrentCulture);

            return Head(env)
                .Set("year", y)
                .Set("count", occs.Count)
                .Set("past", 지난).Set("pastDone", 지난완료)
                .Set("inProgress", 진행중).Set("upcoming", 예정)
                .Set("overdue", 지남).Set("amountUnknown", 미확인)
                .Set("remaining", remaining).Set("finished", finished)
                .Set("orgs", orgs);
        }

        JObj Items(Env env)
        {
            var list = new List<object>();
            foreach (PaymentItem it in env.Master)
            {
                AmountRecord a;
                decimal? 올해 = null;
                bool 입력함 = env.Amounts.TryGetValue(env.Today.Year + "\t" + it.Id, out a);
                if (입력함) 올해 = a.금액;
                else if (it.고정금액.HasValue) 올해 = it.고정금액;

                list.Add(ItemDto(it)
                    .Set("thisYearAmount", 올해)
                    .Set("thisYearEntered", 입력함)
                    .Set("thisYearUnknown", !올해.HasValue && RuleNeedsAmount(it)));
            }
            return Head(env).Set("items", list);
        }

        JObj History(Env env, string from, string to)
        {
            DateTime a = 날짜(from, new DateTime(env.Today.Year, 1, 1));
            DateTime b = 날짜(to, new DateTime(env.Today.Year, 12, 31));
            if (b < a) throw new HttpError(400, "조회 기간의 끝이 시작보다 빠릅니다.");

            var rows = new List<KeyValuePair<DateTime, JObj>>();
            decimal 합계 = 0;
            foreach (StatusRecord st in env.Status.Values)
            {
                PaymentItem it = env.Item(st.Id);
                if (it == null || !st.변경일시.HasValue) continue;
                if (st.단계 < Stages.FinalIndex(it.진행흐름)) continue;
                DateTime 처리일 = st.변경일시.Value.Date;
                if (처리일 < a || 처리일 > b) continue;

                Occurrence o = OccurrenceOf(env, it, st.연도);
                decimal? amt = Amount(o);
                if (amt.HasValue) 합계 += amt.Value;
                rows.Add(new KeyValuePair<DateTime, JObj>(st.변경일시.Value,
                    Dto(env, o, false).Set("doneAt", st.변경일시.Value.ToString("yyyy-MM-dd", Inv))));
            }
            rows.Sort(delegate(KeyValuePair<DateTime, JObj> x, KeyValuePair<DateTime, JObj> y) { return y.Key.CompareTo(x.Key); });

            var list = new List<object>();
            foreach (var kv in rows) list.Add(kv.Value);

            return Head(env)
                .Set("from", a.ToString("yyyy-MM-dd", Inv))
                .Set("to", b.ToString("yyyy-MM-dd", Inv))
                .Set("rows", list)
                .Set("total", 합계);
        }

        JObj AttachmentList(Env env, int y, string id)
        {
            var list = new List<object>();
            foreach (Attachment a in env.Files.For(y, id))
            {
                list.Add(new JObj()
                    .Set("file", a.저장파일)
                    .Set("name", a.원본파일명)
                    .Set("stage", a.단계)
                    .Set("at", a.첨부일시 == DateTime.MinValue ? "" : a.첨부일시.ToString("yyyy-MM-dd HH:mm", Inv)));
            }
            return new JObj().Set("year", y).Set("id", id).Set("files", list);
        }

        void SendFile(HttpListenerResponse res, Env env, int y, string id, string f)
        {
            Attachment hit = null;
            foreach (Attachment a in env.Files.For(y, id))
                if (a.저장파일 == f) { hit = a; break; }
            if (hit == null) throw new HttpError(404, "증빙을 찾을 수 없습니다.");

            // 목록에 있어도 증빙 폴더 밖을 가리키면 내주지 않는다.
            string root = Path.GetFullPath(env.Files.RootDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(env.Files.FullPath(hit));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new HttpError(403, "증빙 폴더 밖의 파일입니다.");
            if (!System.IO.File.Exists(full)) throw new HttpError(404, "증빙 파일이 지워졌습니다.");

            string ext = Path.GetExtension(full).ToLowerInvariant();
            string type = null;
            if (ext == ".pdf") type = "application/pdf";
            else if (ext == ".png") type = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") type = "image/jpeg";
            else if (ext == ".gif") type = "image/gif";

            string 이름 = string.IsNullOrEmpty(hit.원본파일명) ? Path.GetFileName(full) : hit.원본파일명;
            string 안내 = "filename*=UTF-8''" + Uri.EscapeDataString(이름);
            // 브라우저가 바로 보여줄 수 있는 것만 창에서 열고, 나머지는 내려받게 한다.
            res.Headers["Content-Disposition"] = (type != null ? "inline; " : "attachment; ") + 안내;
            res.Headers["Content-Security-Policy"] = "sandbox";
            res.ContentType = type ?? "application/octet-stream";
            res.StatusCode = 200;

            using (FileStream fs = System.IO.File.OpenRead(full))
            {
                res.ContentLength64 = fs.Length;
                fs.CopyTo(res.OutputStream);
            }
        }

        // ══ 변경 ═══════════════════════════════════════════════════

        JObj Advance(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            PaymentItem it = 항목(env, f["id"]);
            StatusRecord st = env.StatusOf(y, it.Id);
            int last = Stages.FinalIndex(it.진행흐름);
            if (st.단계 < 0) st.단계 = 0;
            if (st.단계 >= last) throw new HttpError(409, "이미 마지막 단계입니다.");

            st.단계++;
            st.변경일시 = DateTime.Now;
            st.최종확인일 = env.Today;
            st.변경됨 = true;
            env.Db.SaveStatus(new StatusRecord[] { st });
            return Dto(env, OccurrenceOf(env, it, y), false);
        }

        JObj Defer(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            PaymentItem it = 항목(env, f["id"]);
            StatusRecord st = env.StatusOf(y, it.Id);
            st.최종확인일 = env.Today;
            st.변경됨 = true;
            env.Db.SaveStatus(new StatusRecord[] { st });
            return Dto(env, OccurrenceOf(env, it, y), true);
        }

        JObj Revert(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            PaymentItem it = 항목(env, f["id"]);
            StatusRecord st = env.StatusOf(y, it.Id);
            if (st.단계 > Stages.FinalIndex(it.진행흐름)) st.단계 = Stages.FinalIndex(it.진행흐름);
            if (st.단계 <= 0) throw new HttpError(409, "첫 단계라 되돌릴 수 없습니다.");

            st.단계--;
            st.변경일시 = DateTime.Now;
            st.최종확인일 = null;
            st.변경됨 = true;
            env.Db.SaveStatus(new StatusRecord[] { st });
            return Dto(env, OccurrenceOf(env, it, y), false);
        }

        JObj SaveAmount(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            PaymentItem it = 항목(env, f["id"]);
            decimal v = 금액(f["amount"], "금액");

            var rec = new AmountRecord();
            rec.연도 = y;
            rec.Id = it.Id;
            rec.금액 = v;
            rec.출처 = "웹 입력";
            rec.확인일 = env.Today;
            rec.비고 = 글자(f["memo"], "메모", 200, false);
            env.Db.UpsertAmounts(new AmountRecord[] { rec });
            env.Amounts[rec.Key] = rec;
            return Dto(env, OccurrenceOf(env, it, y), false);
        }

        JObj SaveItem(Env env, NameValueCollection f)
        {
            string id = (f["id"] ?? "").Trim();
            if (!아이디형식.IsMatch(id))
                throw new HttpError(400, "id 는 영문·숫자·_·- 로 1~40자여야 합니다.");

            bool 새항목 = f["mode"] == "new";
            if (새항목 && env.Item(id) != null) throw new HttpError(409, "같은 id 의 항목이 이미 있습니다: " + id);
            if (!새항목 && env.Item(id) == null) throw new HttpError(404, "고칠 항목이 없습니다: " + id);

            var it = new PaymentItem();
            it.Id = id;
            it.기관 = 글자(f["org"], "기관", 60, true);
            it.비용명 = 글자(f["name"], "비용명", 60, true);
            try { it.진행흐름 = Stages.Parse(f["flow"]); }
            catch (FormatException ex) { throw new HttpError(400, ex.Message); }

            it.월 = 정수(f["month"], "월", 1, 12);
            string day = (f["day"] ?? "").Trim();
            if (day == "말일" || day.ToUpperInvariant() == "EOM")
            {
                it.말일 = true;
                it.일 = 0;
            }
            else
            {
                // 윤년이 아닌 해에도 늘 있는 날만 받는다. 2월 29일은 '말일'로 넣어야 한다.
                it.일 = 정수(day, "기한일", 1, DateTime.DaysInMonth(2023, it.월));
            }

            string lead = (f["lead"] ?? "").Trim();
            it.알림영업일 = lead.Length == 0 ? 3 : 정수(lead, "알림 영업일", 1, 60);
            it.금액규칙 = 글자(f["rule"], "금액규칙", 40, false);
            string fixedText = (f["fixed"] ?? "").Trim();
            it.고정금액 = fixedText.Length == 0 ? (decimal?)null : 금액(fixedText, "고정금액");
            it.비고 = 글자(f["memo"], "비고", 200, false);

            env.Db.UpsertItem(it);

            // 기한을 당겨서 오늘 알릴 건이 됐으면 9시 예약 실행을 기다리지 않고 바로 알린다.
            bool 팝업 = false;
            if (팝업요청 != null && 오늘알릴건(env, it))
            {
                try { 팝업요청(it.Id); 팝업 = true; }
                catch (Exception ex) { if (Log != null) Log("알림 팝업을 띄우지 못했습니다: " + ex.Message); }
            }

            return new JObj().Set("ok", true).Set("item", ItemDto(it)).Set("popup", 팝업);
        }

        /// <summary>팝업과 같은 규칙으로, 이 항목이 오늘 팝업에 나올 건인지 본다.</summary>
        static bool 오늘알릴건(Env env, PaymentItem it)
        {
            var occs = Scheduler.BuildOccurrences(new List<PaymentItem> { it }, env.Cal, env.Today, env.Amounts, env.시작일);
            // BuildRows 가 상태 목록에 빈 기록을 채워 넣으므로 새로 읽은 것을 준다.
            RowSet set = Scheduler.BuildRows(occs, env.Db.LoadStatus(), env.Cal, env.Today);
            return set.Rows.Count > 0;
        }

        JObj DeleteItem(Env env, NameValueCollection f)
        {
            PaymentItem it = 항목(env, f["id"]);
            // 진행 기록·금액·증빙은 남긴다. 같은 id 로 다시 만들면 이어진다.
            env.Db.DeleteItem(it.Id);
            return new JObj().Set("ok", true);
        }

        JObj Attach(Env env, HttpListenerRequest req)
        {
            NameValueCollection q = Query(req);
            int y = 연도(q["y"]);
            PaymentItem it = 항목(env, q["id"]);

            string raw = req.Headers["X-File-Name"];
            if (string.IsNullOrEmpty(raw)) throw new HttpError(400, "파일 이름이 없습니다.");
            string name;
            try { name = Uri.UnescapeDataString(raw); }
            catch { throw new HttpError(400, "파일 이름이 올바르지 않습니다."); }
            name = 파일이름(name);

            if (req.ContentLength64 <= 0) throw new HttpError(400, "빈 파일은 첨부할 수 없습니다.");
            if (req.ContentLength64 > 첨부최대) throw new HttpError(413, "50MB 보다 큰 파일은 첨부할 수 없습니다.");

            string tmpDir = Path.Combine(Path.GetTempPath(), "PaymentAlert-upload", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                string tmp = Path.Combine(tmpDir, name);
                long total = 0;
                using (FileStream fs = System.IO.File.Create(tmp))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if (total > 첨부최대) throw new HttpError(413, "50MB 보다 큰 파일은 첨부할 수 없습니다.");
                        fs.Write(buf, 0, n);
                    }
                }

                StatusRecord st = env.StatusOf(y, it.Id);
                string[] stages = Stages.For(it.진행흐름);
                int idx = Math.Max(0, Math.Min(st.단계, stages.Length - 1));
                Attachment a = env.Files.Attach(y, it.Id, stages[idx], tmp);
                return new JObj().Set("ok", true).Set("file", a.저장파일).Set("name", a.원본파일명)
                    .Set("count", env.Files.CountFor(y, it.Id));
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }

        JObj DeleteAttachment(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            string id = 아이디(f["id"]);
            string file = f["f"];
            foreach (Attachment a in env.Files.For(y, id))
            {
                if (a.저장파일 != file) continue;
                env.Files.Remove(a);
                return new JObj().Set("ok", true).Set("count", env.Files.CountFor(y, id));
            }
            throw new HttpError(404, "증빙을 찾을 수 없습니다.");
        }

        // ══ 화면용 자료 모양 ═══════════════════════════════════════

        static JObj Head(Env env)
        {
            return new JObj()
                .Set("today", env.Today.ToString("yyyy-MM-dd", Inv))
                .Set("todayText", env.Today.ToString("yyyy년 M월 d일 (ddd)", Ko))
                .Set("startDate", env.시작일.HasValue ? env.시작일.Value.ToString("yyyy-MM-dd", Inv) : null);
        }

        static JObj ItemDto(PaymentItem it)
        {
            return new JObj()
                .Set("id", it.Id)
                .Set("org", it.기관)
                .Set("name", it.비용명)
                .Set("flow", it.진행흐름.ToString())
                .Set("month", it.월)
                .Set("day", it.말일 ? "말일" : it.일.ToString(Inv))
                .Set("lead", it.알림영업일)
                .Set("rule", it.금액규칙 ?? "")
                .Set("fixed", it.고정금액)
                .Set("memo", it.비고 ?? "");
        }

        /// <summary>
        /// 발생 건 하나를 화면이 그대로 그릴 수 있는 모양으로 만든다.
        /// 문구와 판정은 여기서 끝낸다. 화면마다 규칙이 갈라지지 않게 하기 위해서다.
        /// </summary>
        static JObj Dto(Env env, Occurrence o, bool 오늘대기)
        {
            PaymentItem it = o.Item;
            string[] stages = Stages.For(it.진행흐름);
            StatusRecord st;
            env.Status.TryGetValue(o.Key, out st);
            int stage = st == null ? 0 : Math.Max(0, Math.Min(st.단계, stages.Length - 1));
            bool done = stage >= stages.Length - 1;
            int left = o.남은영업일(env.Cal, env.Today);

            string text;
            string severity;
            if (done) { text = "처리 완료"; severity = "done"; }
            else
            {
                if (left > 0) text = "D-" + left + "영업일";
                else if (left == 0) text = "오늘이 기한";
                else text = "기한 " + (-left) + "영업일 지남";

                if (left < 0) severity = "overdue";
                else if (o.알림일.Date <= env.Today) severity = "soon";
                else severity = "normal";
            }

            decimal? amount = Amount(o);
            string amountText = amount.HasValue ? amount.Value.ToString("N0", Inv) + "원" : (Unknown(o) ? "미확인" : "");

            bool confirmedToday = 오늘대기 ||
                (st != null && st.최종확인일.HasValue && st.최종확인일.Value.Date == env.Today);

            return new JObj()
                .Set("year", o.연도)
                .Set("id", it.Id)
                .Set("org", it.기관)
                .Set("name", it.비용명)
                .Set("flow", it.진행흐름.ToString())
                .Set("stages", stages)
                .Set("stage", stage)
                .Set("stageName", stages[stage])
                .Set("nextStage", done ? null : stages[stage + 1])
                .Set("done", done)
                .Set("due", o.원기한일.ToString("yyyy-MM-dd", Inv))
                .Set("dueDow", o.원기한일.ToString("ddd", Ko))
                .Set("payDue", o.보정기한일.ToString("yyyy-MM-dd", Inv))
                .Set("payDow", o.보정기한일.ToString("ddd", Ko))
                .Set("shifted", o.원기한일.Date != o.보정기한일.Date)
                .Set("alertDate", o.알림일.ToString("yyyy-MM-dd", Inv))
                .Set("daysLeft", left)
                .Set("statusText", text)
                .Set("severity", severity)
                .Set("amount", amount)
                .Set("amountText", amountText)
                .Set("amountEntered", o.실제금액 != null)
                .Set("amountRule", it.금액규칙 ?? "")
                .Set("confirmedToday", confirmedToday)
                .Set("changedAt", st != null && st.변경일시.HasValue ? st.변경일시.Value.ToString("yyyy-MM-dd HH:mm", Inv) : null)
                .Set("attachments", env.Files.CountFor(o.연도, it.Id))
                .Set("memo", it.비고 ?? "");
        }

        static Occurrence OccurrenceOf(Env env, PaymentItem it, int year)
        {
            var one = new List<PaymentItem> { it };
            foreach (Occurrence o in Scheduler.BuildOccurrences(one, env.Cal, new DateTime(year, 7, 1), env.Amounts))
                if (o.연도 == year) return o;
            throw new InvalidOperationException("발생 건을 만들지 못했습니다.");
        }

        static bool Done(Env env, Occurrence o)
        {
            StatusRecord st;
            return env.Status.TryGetValue(o.Key, out st) && st.단계 >= Stages.FinalIndex(o.Item.진행흐름);
        }

        /// <summary>확인된 금액 → 고정금액 순. 둘 다 없으면 null.</summary>
        static decimal? Amount(Occurrence o)
        {
            if (o.실제금액 != null) return o.실제금액.금액;
            return o.Item.고정금액;
        }

        static bool RuleNeedsAmount(PaymentItem it)
        {
            string r = it.금액규칙 ?? "";
            return r.Length > 0 && r != "해당없음";
        }

        /// <summary>납부할 돈이 있는데 금액을 모르는 건. 빈칸으로 두면 0원으로 오해한다.</summary>
        static bool Unknown(Occurrence o)
        {
            return !Amount(o).HasValue && RuleNeedsAmount(o.Item);
        }

        // ══ 입력 읽기 ═══════════════════════════════════════════════

        /// <summary>
        /// 주소 뒤 조건을 UTF-8 로 읽는다. HttpListener 의 QueryString 은 시스템 코드페이지로 풀어
        /// 한글 파일 이름이 깨진다.
        /// </summary>
        static NameValueCollection Query(HttpListenerRequest req)
        {
            string raw = req.RawUrl ?? "";
            int qm = raw.IndexOf('?');
            return Pairs(qm < 0 ? "" : raw.Substring(qm + 1));
        }

        static NameValueCollection Pairs(string body)
        {
            var r = new NameValueCollection();
            foreach (string pair in body.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq);
                string v = eq < 0 ? "" : pair.Substring(eq + 1);
                try
                {
                    r[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' '));
                }
                catch { throw new HttpError(400, "입력 형식이 올바르지 않습니다."); }
            }
            return r;
        }

        static NameValueCollection ReadForm(HttpListenerRequest req)
        {
            if (req.ContentLength64 > 양식최대) throw new HttpError(413, "입력이 너무 깁니다.");

            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                var buf = new char[4096];
                var sb = new StringBuilder();
                int n;
                while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                {
                    sb.Append(buf, 0, n);
                    if (sb.Length > 양식최대) throw new HttpError(413, "입력이 너무 깁니다.");
                }
                body = sb.ToString();
            }
            return Pairs(body);
        }

        static int 연도(string s)
        {
            int y;
            if (!int.TryParse((s ?? "").Trim(), NumberStyles.None, Inv, out y) || y < 2000 || y > 2100)
                throw new HttpError(400, "연도가 올바르지 않습니다.");
            return y;
        }

        static string 아이디(string s)
        {
            string id = (s ?? "").Trim();
            if (!아이디형식.IsMatch(id)) throw new HttpError(400, "id 가 올바르지 않습니다.");
            return id;
        }

        static PaymentItem 항목(Env env, string s)
        {
            PaymentItem it = env.Item(아이디(s));
            if (it == null) throw new HttpError(404, "항목을 찾을 수 없습니다: " + s);
            return it;
        }

        static int 정수(string s, string 이름, int min, int max)
        {
            int v;
            if (!int.TryParse((s ?? "").Trim(), NumberStyles.None, Inv, out v) || v < min || v > max)
                throw new HttpError(400, string.Format("{0}은(는) {1}~{2} 사이의 숫자여야 합니다.", 이름, min, max));
            return v;
        }

        /// <summary>원 단위 금액. 쉼표·공백·'원'은 허용하고, 음수와 소수는 거절한다.</summary>
        static decimal 금액(string s, string 이름)
        {
            string t = (s ?? "").Replace(",", "").Replace(" ", "").Replace("원", "").Trim();
            decimal v;
            if (t.Length == 0 || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, Inv, out v))
                throw new HttpError(400, 이름 + "은(는) 숫자로 넣어 주세요.");
            if (v != decimal.Truncate(v)) throw new HttpError(400, 이름 + "은(는) 원 단위로 넣어 주세요.");
            if (v > 999999999999999m) throw new HttpError(400, 이름 + "이(가) 너무 큽니다.");
            return v;
        }

        static string 글자(string s, string 이름, int max, bool 필수)
        {
            string t = (s ?? "").Trim();
            if (필수 && t.Length == 0) throw new HttpError(400, 이름 + "을(를) 넣어 주세요.");
            if (t.Length > max) throw new HttpError(400, string.Format("{0}은(는) {1}자까지 넣을 수 있습니다.", 이름, max));
            foreach (char ch in t)
                if (char.IsControl(ch)) throw new HttpError(400, 이름 + "에 쓸 수 없는 문자가 있습니다.");
            return t;
        }

        static DateTime 날짜(string s, DateTime 기본)
        {
            if (string.IsNullOrEmpty(s)) return 기본;
            DateTime d;
            if (!DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d))
                throw new HttpError(400, "날짜 형식이 올바르지 않습니다 (예: 2026-01-01).");
            return d;
        }

        static string 파일이름(string s)
        {
            string n = s.Replace('/', '\\');
            int slash = n.LastIndexOf('\\');
            if (slash >= 0) n = n.Substring(slash + 1);
            var sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char ch in n) sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
            string r = sb.ToString().Trim().TrimEnd('.');
            if (r.Length == 0) throw new HttpError(400, "파일 이름이 올바르지 않습니다.");
            if (r.Length > 120) r = r.Substring(r.Length - 120);
            return r;
        }

        static void Send(HttpListenerResponse res, int status, JObj body)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Write(body));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }
}
