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
    public sealed partial class WebServer : IDisposable
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

        /// <summary>실행 파일 폴더. backup-folder.txt 를 찾는 곳. 비우면 자료 폴더를 쓴다.</summary>
        public string BaseDir;

        /// <summary>
        /// 공휴일을 받아 오는 함수 (캐시, 인증키, 연도) → 결과 문구, 성공 여부는 캐시 변경으로 판단.
        /// 시험에서 네트워크 없이 바꿔 끼울 수 있게 밖으로 뺐다.
        /// </summary>
        public Func<Holidays.Cache, string, List<int>, string> 공휴일받기 = delegate(Holidays.Cache c, string key, List<int> years)
        {
            string msg;
            Holidays.TryRefresh(c, key, years, out msg);
            return msg;
        };

        readonly object 작업잠금 = new object();
        bool 공휴일작업중;
        string 공휴일작업결과;

        /// <summary>
        /// 항목을 고친 결과 그 항목이 오늘 알릴 건이 되면 부른다. 인자는 항목 id.
        /// 팝업을 실제로 띄우는 일은 실행 프로그램이 맡는다.
        /// </summary>
        public Action<string> 팝업요청;

        /// <summary>이 PC 에 폴더 선택 창을 띄운다 (제목, 처음 폴더) → 고른 경로, 취소면 null. 없으면 화면에서 고를 수 없다.</summary>
        public Func<string, string, string> 폴더고르기;

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

                // 요청마다 따로 처리한다 (ADR-0012). 파일을 기본 프로그램으로 여는 요청처럼 오래 걸리는 것이 있어도
                // 다른 화면 요청이 기다리지 않는다. DB 는 요청마다 새로 열고, 동시 쓰기는 SQLite 잠금이 줄 세운다.
                ThreadPool.QueueUserWorkItem(delegate { 처리(ctx); });
            }
        }

        void 처리(HttpListenerContext ctx)
        {
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

        static readonly Dictionary<string, string> 정적형식 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" }, { ".txt", "text/plain; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" }, { ".ico", "image/x-icon" }, { ".svg", "image/svg+xml" },
            { ".png", "image/png" }, { ".woff2", "font/woff2" }, { ".woff", "font/woff" },
        };

        static readonly Regex 인라인스크립트 = new Regex(@"<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase);
        readonly Dictionary<string, KeyValuePair<DateTime, string>> 해시캐시 = new Dictionary<string, KeyValuePair<DateTime, string>>();

        /// <summary>
        /// 화면 파일 (ADR-0007). web 폴더 안의 정해 둔 형식만 내준다. 점으로 시작하는 이름·상위 폴더 경로는 막는다.
        /// </summary>
        void Static(HttpListenerResponse res, string path)
        {
            string rel = path == "/" ? "index.html" : path.TrimStart('/');
            if (rel.EndsWith("/")) rel += "index.html";
            foreach (string seg in rel.Split('/'))
                if (seg.Length == 0 || seg.StartsWith(".") || seg.IndexOf('\\') >= 0 || seg.IndexOf(':') >= 0)
                    throw new HttpError(404, "없는 화면입니다.");

            string ext = Path.GetExtension(rel);
            if (ext.Length == 0) { rel += ".html"; ext = ".html"; }
            string type;
            if (!정적형식.TryGetValue(ext, out type)) throw new HttpError(404, "없는 화면입니다.");

            string root = Path.GetFullPath(webRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                throw new HttpError(404, "없는 화면입니다.");

            byte[] body = File.ReadAllBytes(full);
            if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                // 정적 내보내기가 넣은 인라인 스크립트만 해시로 허용한다. 'unsafe-inline' 은 쓰지 않는다.
                res.Headers["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self' " + 스크립트해시(full, body) + "; " +
                    "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; " +
                    "connect-src 'self'; frame-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            }
            else if (rel.StartsWith("_next/static/", StringComparison.Ordinal))
            {
                res.Headers["Cache-Control"] = "public, max-age=31536000, immutable";   // 이름에 내용 해시가 들어 있다
            }

            res.StatusCode = 200;
            res.ContentType = type;
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
        }

        string 스크립트해시(string full, byte[] body)
        {
            DateTime mtime = File.GetLastWriteTimeUtc(full);
            lock (해시캐시)
            {
                KeyValuePair<DateTime, string> hit;
                if (해시캐시.TryGetValue(full, out hit) && hit.Key == mtime) return hit.Value;
            }
            var parts = new List<string>();
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (Match m in 인라인스크립트.Matches(Encoding.UTF8.GetString(body)))
                {
                    string h = "'sha256-" + Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(m.Groups[1].Value))) + "'";
                    if (!parts.Contains(h)) parts.Add(h);
                }
            }
            string joined = string.Join(" ", parts.ToArray());
            lock (해시캐시) 해시캐시[full] = new KeyValuePair<DateTime, string>(mtime, joined);
            return joined;
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
                    case "/api/loans": Send(ctx.Response, 200, 차입목록(env)); return;
                    case "/api/history": Send(ctx.Response, 200, History(env, q["from"], q["to"])); return;
                    case "/api/attachments":
                        Send(ctx.Response, 200, AttachmentList(env, 연도(q["y"]), 아이디(q["id"])));
                        return;
                    case "/api/settings": Send(ctx.Response, 200, Settings(env)); return;
                    case "/api/group": Send(ctx.Response, 200, Group(env, 연도(q["y"]), q["group"])); return;
                    case "/api/events": Send(ctx.Response, 200, Events(env, q)); return;
                    case "/api/health":
                        Send(ctx.Response, 200, new JObj().Set("ok", true).Set("version", AppInfo.버전)
                            .Set("schema", env.Db.버전).Set("today", env.Today.ToString("yyyy-MM-dd", Inv)));
                        return;
                    case "/api/file": SendFile(ctx.Response, env, 연도(q["y"]), 아이디(q["id"]), q["f"]); return;
                }
            }
            throw new HttpError(404, "없는 기능입니다.");
        }

        void Post(HttpListenerContext ctx, string path)
        {
            HttpListenerRequest req = ctx.Request;

            // 첨부·가져오기는 본문이 파일 자체라 양식으로 읽지 않는다.
            if (path == "/api/attach")
            {
                using (Env env = Env.Open(dataDir, 오늘()))
                    Send(ctx.Response, 200, Attach(env, req));
                return;
            }
            if (path == "/api/import/loans")
            {
                using (Env env = Env.Open(dataDir, 오늘()))
                    Send(ctx.Response, 200, 차입가져오기(env, req));
                return;
            }

            NameValueCollection f = ReadForm(req);
            using (Env env = Env.Open(dataDir, 오늘()))
            {
                switch (path)
                {
                    case "/api/advance": Send(ctx.Response, 200, ChangeStage(env, f, "진행")); return;
                    case "/api/defer": Send(ctx.Response, 200, ChangeStage(env, f, "대기")); return;
                    case "/api/revert": Send(ctx.Response, 200, ChangeStage(env, f, "되돌리기")); return;
                    case "/api/undefer": Send(ctx.Response, 200, ChangeStage(env, f, "대기취소")); return;
                    case "/api/amount": Send(ctx.Response, 200, SaveAmount(env, f)); return;
                    case "/api/amount/delete":
                        env.Db.DeleteAmount(연도(f["y"]), 아이디(f["id"]), 출처);
                        Send(ctx.Response, 200, new JObj().Set("ok", true));
                        return;
                    case "/api/item": Send(ctx.Response, 200, SaveItem(env, f)); return;
                    case "/api/item/delete": Send(ctx.Response, 200, DeleteItem(env, f)); return;
                    case "/api/settings/start-date": Send(ctx.Response, 200, SetStartDate(env, f)); return;
                    case "/api/group/amounts": Send(ctx.Response, 200, SaveGroupAmounts(env, f)); return;
                    case "/api/open": Send(ctx.Response, 200, OpenFile(env, f)); return;
                    case "/api/item/move": Send(ctx.Response, 200, MoveItem(env, f)); return;
                    case "/api/settings/apikey": Send(ctx.Response, 200, SetApiKey(env, f)); return;
                    case "/api/holidays/refresh": Send(ctx.Response, 200, RefreshHolidays(env)); return;
                    case "/api/backup": Send(ctx.Response, 200, BackupNow(env)); return;
                    case "/api/settings/backup-dir": Send(ctx.Response, 200, SetBackupDir(env, f)); return;
                    case "/api/settings/backup-dir/pick": Send(ctx.Response, 200, PickBackupDir(env)); return;
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
            public Holidays.Cache 공휴일;
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
                    e.공휴일 = cache;
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

            Dictionary<string, string> _마지막동작;
            /// <summary>건마다 마지막 동작. 필요할 때 한 번만 읽는다.</summary>
            public Dictionary<string, string> 마지막동작
            {
                get { if (_마지막동작 == null) _마지막동작 = Db.마지막동작(); return _마지막동작; }
            }

            Dictionary<string, PaymentItem> _기관사이트;
            /// <summary>
            /// 기관마다 홈페이지 하나: 같은 기관 중 목록 순서로 먼저 주소가 있는 항목 (사용자 요청 2026-09-18).
            /// 한 건에만 주소를 넣어도 같은 기관의 모든 줄에 아이콘이 붙는다.
            /// </summary>
            public PaymentItem 기관사이트(string 기관)
            {
                if (_기관사이트 == null)
                {
                    _기관사이트 = new Dictionary<string, PaymentItem>(StringComparer.Ordinal);
                    foreach (PaymentItem x in Master)
                    {
                        string k = (x.기관 ?? "").Trim();
                        if (k.Length > 0 && !string.IsNullOrWhiteSpace(x.홈페이지주소) && !_기관사이트.ContainsKey(k)) _기관사이트[k] = x;
                    }
                }
                PaymentItem hit;
                return _기관사이트.TryGetValue((기관 ?? "").Trim(), out hit) ? hit : null;
            }

            /// <summary>화면의 기관명 옆 아이콘에 쓸 주소·이름. 제 주소가 있으면 그것, 없으면 같은 기관의 주소.</summary>
            public JObj 기관사이트붙이기(JObj o, PaymentItem it)
            {
                PaymentItem src = it != null && !string.IsNullOrWhiteSpace(it.홈페이지주소) ? it : (it != null ? 기관사이트(it.기관) : null);
                return o.Set("orgSiteUrl", src != null ? src.홈페이지주소 : "").Set("orgSiteName", src != null ? (src.홈페이지명 ?? "") : "");
            }

            public PaymentItem Item(string id)
            {
                foreach (PaymentItem it in Master) if (it.Id == id) return it;
                return null;
            }

            /// <summary>
            /// 그 해의 발생 건 전부. 추적 시작일 이전 건도 넣는다 — 화면이 '지난 건' 에 흐리게 보여 준다
            /// (사용자 결정 2026-09-16, ADR-0014). 집계에서는 빼야 하므로 시작전() 으로 가린다.
            /// </summary>
            public List<Occurrence> OccurrencesOf(int year)
            {
                DateTime 기준 = year == Today.Year ? Today : new DateTime(year, 7, 1);
                var all = Scheduler.BuildOccurrences(Master, Cal, 기준, Amounts, null);
                var r = new List<Occurrence>();
                foreach (Occurrence o in all) if (o.연도 == year) r.Add(o);
                return r;
            }

            /// <summary>추적 시작일보다 기한이 이른 건. 할 일이 아니므로 집계·행동에서 뺀다.</summary>
            public bool 시작전(Occurrence o)
            {
                return 시작일.HasValue && o.보정기한일.Date < 시작일.Value.Date;
            }

            public int 단계(Occurrence o)
            {
                StatusRecord st;
                return Status.TryGetValue(o.Key, out st) ? st.단계 : 0;
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
                .Set("notYet", 알림전)
                .Set("warnings", Warnings.Build(env.공휴일, Scheduler.TargetYears(env.Today), env.Today));
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
            var cnt = new 집계();
            decimal 합계 = 0;
            int 미확인 = 0;
            foreach (Occurrence o in month)
            {
                if (!cnt.더하기(env, o))
                {
                    decimal? a = Amount(o);
                    if (a.HasValue) 합계 += a.Value; else if (Unknown(o)) 미확인++;
                }
                list.Add(Dto(env, o, false));
            }

            return Head(env)
                .Set("year", y).Set("month", m)
                .Set("rows", list)
                .Set("inProgress", cnt.진행중).Set("upcoming", cnt.진행예정).Set("done", cnt.완료).Set("overdue", cnt.지남)
                .Set("beforeStart", cnt.시작전)
                .Set("total", 합계).Set("amountUnknown", 미확인);
        }

        /// <summary>
        /// 이번 달·연간이 같은 낱말로 센다 (사용자 결정 Q3, ADR-0014).
        /// 기한 지남 = 기한이 지난 미완료, 진행중 = 한 단계라도 밟은 미완료, 진행예정 = 아직 아무 단계도 안 밟은 미완료.
        /// 셋은 겹치지 않는다. 추적 시작일 이전 건은 어느 칸에도 넣지 않는다.
        /// </summary>
        sealed class 집계
        {
            public int 진행중, 진행예정, 완료, 지남, 시작전, 지난, 지난완료;

            /// <summary>센 건이 시작일 이전이면 true (금액 합계에서도 빼라는 뜻).</summary>
            public bool 더하기(Env env, Occurrence o)
            {
                if (env.시작전(o)) { 시작전++; return true; }
                bool done = Done(env, o);
                bool 기한지남 = o.보정기한일.Date < env.Today;
                if (기한지남) { 지난++; if (done) 지난완료++; }
                if (done) 완료++;
                else if (기한지남) 지남++;
                else if (env.단계(o) >= 1) 진행중++;
                else 진행예정++;
                return false;
            }
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

            var cnt = new 집계();
            int 미확인 = 0;
            var remaining = new List<object>();
            var finished = new List<object>();
            var orgs = new List<string>();
            foreach (Occurrence o in occs)
            {
                bool before = cnt.더하기(env, o);
                bool done = Done(env, o);
                if (!before && Unknown(o)) 미확인++;
                if (!orgs.Contains(o.Item.기관)) orgs.Add(o.Item.기관);

                // 지난 건 묶음 = 끝난 건 + 추적 시작일 이전 건, 최근 것이 위로 (AC-W56)
                if (done || before) finished.Insert(0, Dto(env, o, false));
                else if (o.보정기한일.Date < env.Today) remaining.Insert(cnt.지남 - 1, Dto(env, o, false));   // 놓친 기한은 맨 위에 고정 (AC-W55)
                else remaining.Add(Dto(env, o, false));
            }
            orgs.Sort(StringComparer.CurrentCulture);

            return Head(env)
                .Set("year", y)
                .Set("count", occs.Count - cnt.시작전)
                .Set("beforeStart", cnt.시작전)
                .Set("past", cnt.지난).Set("pastDone", cnt.지난완료)
                .Set("inProgress", cnt.진행중).Set("upcoming", cnt.진행예정)
                .Set("overdue", cnt.지남).Set("amountUnknown", 미확인)
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
                // 올해 기한이 없는 항목(다른 해의 차입 회차 등)은 올해 금액이 해당 없다 — '미확인' 으로 보이면 안 된다.
                bool 올해있음 = it.납부있음 && it.해당연도(env.Today.Year);
                bool 입력함 = 올해있음 && env.Amounts.TryGetValue(env.Today.Year + "\t" + it.Id, out a);
                if (입력함) 올해 = env.Amounts[env.Today.Year + "\t" + it.Id].금액;
                else if (올해있음 && it.금액규칙 == AmountRules.고정) 올해 = it.고정금액;

                list.Add(env.기관사이트붙이기(ItemDto(it)
                    .Set("thisYearAmount", 올해)
                    .Set("thisYearEntered", 입력함)
                    .Set("thisYearUnknown", !올해.HasValue && 올해있음), it));
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
                if (st.단계 < Stages.FinalIndex(it)) continue;
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
                    .Set("kind", a.종류)
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
            // PDF·이미지는 화면 안 보기창에서 연다 (AC-W105). Chrome 은 sandbox 가 걸린 PDF 를 뷰어로 열지 않으므로
            // 이 둘에는 걸지 않는다. 그 밖의 형식은 내려받기로만 나가고 sandbox 로 막는다 (ADR-0011).
            if (type == null) res.Headers["Content-Security-Policy"] = "sandbox";
            else res.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; object-src 'self'; plugin-types application/pdf";
            res.ContentType = type ?? "application/octet-stream";
            res.StatusCode = 200;

            using (FileStream fs = System.IO.File.OpenRead(full))
            {
                res.ContentLength64 = fs.Length;
                fs.CopyTo(res.OutputStream);
            }
        }

        // ══ 변경 ═══════════════════════════════════════════════════

        // 단계 변경은 DB 가 한 트랜잭션에서 확인하고 쓴다 (ADR-0004).
        // 화면이 본 단계(stage)를 함께 보내면, 그사이 팝업이 바꾼 경우 409 로 거절한다.

        const string 출처 = "웹";

        JObj ChangeStage(Env env, NameValueCollection f, string 동작)
        {
            int y = 연도(f["y"]);
            PaymentItem it = 항목(env, f["id"]);
            연도확인(it, y);   // 쓰기 전에 본다 — 거절할 요청이 기록을 남기면 안 된다
            int 기대 = -1;
            string s = f["stage"];
            if (!string.IsNullOrEmpty(s) &&
                (!int.TryParse(s, NumberStyles.None, Inv, out 기대) || 기대 > Stages.FinalIndex(it)))
                throw new HttpError(400, "단계 값이 올바르지 않습니다.");

            // 기한 당일·지난 미완료 건은 한 단계 진행해도 '오늘 확인' 을 찍지 않아 납부까지 계속 알린다 (사용자 요청 2026-09-22).
            bool 기한임박 = OccurrenceOf(env, it, y).보정기한일.Date <= env.Today.Date;

            try
            {
                StatusRecord st;
                if (동작 == "진행") st = env.Db.Advance(y, it.Id, Stages.For(it), 기대, DateTime.Now, env.Today, 출처, 기한임박);
                else if (동작 == "대기") st = env.Db.Defer(y, it.Id, 기대, DateTime.Now, env.Today, 출처);
                else if (동작 == "대기취소") st = env.Db.대기취소(y, it.Id, 기대, DateTime.Now, env.Today, 출처);
                else st = env.Db.Revert(y, it.Id, Stages.For(it), 기대, DateTime.Now, 출처);
                env.Status[st.Key] = st;
            }
            catch (StageConflictException ce)
            {
                throw new HttpError(409, ce.Message);
            }
            return Dto(env, OccurrenceOf(env, it, y), 동작 == "대기");
        }

        JObj SaveAmount(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            PaymentItem it = 항목(env, f["id"]);
            연도확인(it, y);
            decimal v = 금액(f["amount"], "금액");

            var rec = new AmountRecord();
            rec.연도 = y;
            rec.Id = it.Id;
            rec.금액 = v;
            rec.출처 = "웹 입력";
            rec.확인일 = env.Today;
            rec.비고 = 글자(f["memo"], "메모", 200, false);
            env.Db.UpsertAmounts(new AmountRecord[] { rec }, 출처);
            env.Amounts[rec.Key] = rec;
            return Dto(env, OccurrenceOf(env, it, y), false);
        }

        JObj SaveItem(Env env, NameValueCollection f)
        {
            string id = (f["id"] ?? "").Trim();
            bool 새항목 = f["mode"] == "new";

            // 새 항목은 id 를 비워 보내면 서버가 만든다 (ADR-0015). 한 번 쓴 번호는 지워도 다시 쓰지 않는다.
            if (새항목 && id.Length == 0) id = env.Db.새항목아이디();
            if (!아이디형식.IsMatch(id))
                throw new HttpError(400, "id 는 영문·숫자·_·- 로 1~40자여야 합니다.");

            // 월에 쉼표가 있으면 분할납부 회차를 한꺼번에 만든다 (AC-W32). 회차 수는 월 칸만 정한다 (AC-W42).
            List<int> months = 월목록(f["month"]);
            if (months.Count > 1)
            {
                if (!새항목) throw new HttpError(400, "여러 회차는 새로 추가할 때만 만들 수 있습니다. 회차마다 따로 고치세요.");
                return 분할항목추가(env, f, id, months);
            }

            if (새항목 && env.Item(id) != null) throw new HttpError(409, "같은 id 의 항목이 이미 있습니다: " + id);
            if (!새항목 && env.Item(id) == null) throw new HttpError(404, "고칠 항목이 없습니다: " + id);

            PaymentItem it = 항목읽기(env, f, id, months[0]);
            PaymentItem 기존 = env.Item(id);
            it.묶음 = 기존 != null ? 기존.묶음 : "";
            it.차입 = 기존 != null && 기존.차입;   // 차입 회차는 저장해도 차입 회차 (응답의 이름·단계가 맞게)
            // 유효연도는 보낸 경우에만 바꾼다. 화면이 모르는 칸이라고 가져온 차입 회차의 기간이 풀리면 안 된다.
            it.시작연도 = f["startYear"] != null ? 연도칸(f["startYear"], "시작 연도") : (기존 != null ? 기존.시작연도 : null);
            it.종료연도 = f["endYear"] != null ? 연도칸(f["endYear"], "종료 연도") : (기존 != null ? 기존.종료연도 : null);
            if (it.시작연도.HasValue && it.종료연도.HasValue && it.종료연도 < it.시작연도)
                throw new HttpError(400, "종료 연도가 시작 연도보다 빠릅니다.");

            env.Db.UpsertItem(it);

            // 기한을 당겨서 오늘 알릴 건이 됐으면 9시 예약 실행을 기다리지 않고 바로 알린다.
            bool 팝업 = 팝업확인(env, new PaymentItem[] { it });
            return new JObj().Set("ok", true).Set("item", ItemDto(it)).Set("popup", 팝업);
        }

        bool 팝업확인(Env env, IEnumerable<PaymentItem> items)
        {
            if (팝업요청 == null) return false;
            foreach (PaymentItem it in items)
            {
                if (!오늘알릴건(env, it)) continue;
                try { 팝업요청(it.Id); return true; }
                catch (Exception ex) { if (Log != null) Log("알림 팝업을 띄우지 못했습니다: " + ex.Message); return false; }
            }
            return false;
        }

        /// <summary>항목 칸을 읽고 검사한다. 한 건 저장과 분할 회차 만들기가 같은 규칙을 쓴다.</summary>
        PaymentItem 항목읽기(Env env, NameValueCollection f, string id, int month)
        {
            var it = new PaymentItem();
            it.Id = id;
            it.기관 = 글자(f["org"], "기관", 60, true);
            it.비용명 = 글자(f["name"], "비용명", 60, true);
            try { it.진행흐름 = Stages.Parse(f["flow"]); }
            catch (FormatException ex) { throw new HttpError(400, ex.Message); }

            if (it.진행흐름 == Flow.사용자설정)
            {
                // 단계 이름과 버튼 문구는 줄마다 한 칸. 버튼 문구 줄은 이름 줄과 자리가 맞아야 한다.
                string[] names = 줄목록(f["stages"]);
                string[] raw = 줄목록(f["actions"]);
                var acts = new string[names.Length];
                for (int i = 0; i < names.Length; i++) acts[i] = i < raw.Length ? raw[i].Trim() : "";
                if (acts.Length > 0) acts[0] = "";
                for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
                string 문제 = Stages.단계검사(names, acts);
                if (문제 != null) throw new HttpError(400, 문제);
                it.사용자단계 = names;
                it.사용자행동 = acts;
                string na = (f["noAmount"] ?? "").Trim();
                it.금액없음 = na == "1" || na == "true";
            }

            it.월 = month;
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

            // 금액규칙은 고정/변동 두 가지 (AC-W25). 납부 없는 흐름은 금액을 받지 않는다 (AC-W27).
            string rule = (f["rule"] ?? "").Trim();
            if (rule.Length == 0) rule = AmountRules.변동;
            if (!AmountRules.IsValid(rule)) throw new HttpError(400, "금액규칙은 고정 또는 변동이어야 합니다.");
            it.금액규칙 = rule;
            string fixedText = (f["fixed"] ?? "").Trim();
            if (it.납부있음 && rule == AmountRules.고정)
            {
                if (fixedText.Length == 0) throw new HttpError(400, "고정 규칙은 금액을 넣어야 합니다.");
                it.고정금액 = 금액(fixedText, "고정금액");
            }
            // 변동이면 금액칸에 무엇이 있어도 마스터 금액으로 쓰지 않는다 (AC-W26a).

            it.비고 = 글자(f["memo"], "비고", 200, false);
            it.홈페이지명 = 글자(f["siteName"], "홈페이지 이름", 40, false);
            it.홈페이지주소 = 주소(f["siteUrl"]);
            if (it.홈페이지주소.Length > 0 && it.홈페이지명.Length == 0) it.홈페이지명 = "홈페이지";
            return it;
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
            연도확인(it, y);

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
                string[] stages = Stages.For(it);
                int idx = Math.Max(0, Math.Min(st.단계, stages.Length - 1));
                string 종류 = q["kind"] == Attachment.받은문서 ? Attachment.받은문서 : Attachment.증빙;
                Attachment a = env.Files.Attach(y, it.Id, stages[idx], tmp, 종류, 출처);
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
                env.Files.Remove(a, 출처);
                return new JObj().Set("ok", true).Set("count", env.Files.CountFor(y, id));
            }
            throw new HttpError(404, "증빙을 찾을 수 없습니다.");
        }

        // ══ 설정·관리 ═══════════════════════════════════════════════ ADR-0008

        string 설정폴더() { return string.IsNullOrEmpty(BaseDir) ? dataDir : BaseDir; }
        string 백업폴더() { return Backups.폴더(설정폴더(), dataDir); }

        /// <summary>POST /api/settings/backup-dir/pick — 폴더 선택 창을 띄워 고른 곳으로 바꾼다. 취소하면 그대로.</summary>
        JObj PickBackupDir(Env env)
        {
            if (폴더고르기 == null) throw new HttpError(501, "이 실행 방식에서는 폴더 선택 창을 열 수 없습니다.");
            string 고른 = 폴더고르기("백업 폴더 선택", 백업폴더());
            if (string.IsNullOrEmpty(고른))
                return new JObj().Set("ok", true).Set("cancelled", true).Set("backupDir", 백업폴더());
            string 이유 = Backups.폴더검사(고른, webRoot);
            if (이유 != null) throw new HttpError(400, 이유);
            Backups.폴더저장(설정폴더(), 고른);
            return new JObj().Set("ok", true).Set("cancelled", false).Set("backupDir", 백업폴더()).Set("custom", Backups.따로정함(설정폴더()));
        }

        /// <summary>POST /api/settings/backup-dir  dir=폴더 (빈 값 = 기본 위치). 이미 만든 사본은 옮기지 않는다.</summary>
        JObj SetBackupDir(Env env, NameValueCollection f)
        {
            string dir = (f["dir"] ?? "").Trim();
            string 이유 = Backups.폴더검사(dir, webRoot);
            if (이유 != null) throw new HttpError(400, 이유);
            Backups.폴더저장(설정폴더(), dir);
            return new JObj().Set("ok", true).Set("backupDir", 백업폴더()).Set("custom", Backups.따로정함(설정폴더()));
        }

        JObj Settings(Env env)
        {
            var years = new List<int>(env.공휴일.Years);
            years.Sort();
            var backups = new List<object>();
            foreach (Backups.사본 b in Backups.목록(백업폴더()))
            {
                if (backups.Count >= 10) break;
                backups.Add(new JObj().Set("name", b.이름).Set("size", b.크기)
                    .Set("at", b.만든시각.ToString("yyyy-MM-dd HH:mm", Inv)));
            }
            bool running; string result;
            lock (작업잠금) { running = 공휴일작업중; result = 공휴일작업결과; }

            return Head(env)
                .Set("version", AppInfo.버전)
                .Set("schema", env.Db.버전)
                .Set("integrity", env.Db.무결성검사())
                .Set("dataDir", dataDir)
                .Set("dbPath", DataPaths.Db(dataDir))
                .Set("logPath", DataPaths.로그(dataDir))
                .Set("backupDir", 백업폴더())
                .Set("backupDirDefault", Backups.기본폴더(dataDir))
                .Set("backupDirCustom", Backups.따로정함(설정폴더()))
                .Set("backups", backups)
                .Set("apiKeySet", System.IO.File.Exists(DataPaths.ApiKey(dataDir)))
                .Set("holidayYears", years)
                .Set("holidayCount", env.공휴일.Dates.Count)
                .Set("holidayUpdated", env.공휴일.Updated.HasValue ? env.공휴일.Updated.Value.ToString("yyyy-MM-dd", Inv) : null)
                .Set("holidayJob", new JObj().Set("running", running).Set("message", result))
                .Set("warnings", Warnings.Build(env.공휴일, Scheduler.TargetYears(env.Today), env.Today));
        }

        JObj SetStartDate(Env env, NameValueCollection f)
        {
            string s = (f["date"] ?? "").Trim();
            DateTime? d = null;
            if (s.Length > 0)
            {
                DateTime v;
                if (!DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out v) || v.Year < 2000 || v.Year > 2100)
                    throw new HttpError(400, "날짜 형식이 올바르지 않습니다 (예: 2026-09-01).");
                d = v;
            }
            env.Db.SetStartDate(d);
            env.Db.AddEvent(env.Today.Year, "-", "설정", 출처, "추적 시작일 " + (d.HasValue ? s : "없음"));
            return new JObj().Set("ok", true).Set("startDate", d.HasValue ? s : null);
        }

        /// <summary>인증키는 파일로만 두고 화면에 되돌려 주지 않는다.</summary>
        JObj SetApiKey(Env env, NameValueCollection f)
        {
            string key = (f["key"] ?? "").Trim();
            string path = DataPaths.ApiKey(dataDir);
            if (key.Length == 0)
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                return new JObj().Set("ok", true).Set("apiKeySet", false);
            }
            if (key.Length > 300) throw new HttpError(400, "인증키가 너무 깁니다.");
            foreach (char ch in key)
                if (char.IsWhiteSpace(ch) || char.IsControl(ch)) throw new HttpError(400, "인증키에 공백이나 줄바꿈이 들어 있습니다.");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            System.IO.File.WriteAllText(path, key, new UTF8Encoding(false));
            return new JObj().Set("ok", true).Set("apiKeySet", true);
        }

        /// <summary>
        /// 공휴일 갱신은 수십 번 외부 호출이라 요청 스레드에서 하지 않는다. 뒤에서 돌리고 결과는 설정 조회로 본다.
        /// </summary>
        JObj RefreshHolidays(Env env)
        {
            string path = DataPaths.ApiKey(dataDir);
            if (!System.IO.File.Exists(path)) throw new HttpError(400, "공공데이터포털 인증키를 먼저 넣어 주세요.");
            string key = System.IO.File.ReadAllText(path, Encoding.UTF8).Trim();

            lock (작업잠금)
            {
                if (공휴일작업중) throw new HttpError(409, "공휴일을 이미 받아 오는 중입니다.");
                공휴일작업중 = true;
                공휴일작업결과 = null;
            }

            List<int> years = Scheduler.TargetYears(env.Today);
            string dbPath = DataPaths.Db(dataDir);
            var t = new Thread(delegate()
            {
                string msg;
                try
                {
                    using (Store db = Store.Open(dbPath))
                    {
                        Holidays.Cache cache = db.LoadHolidays();
                        int before = cache.Dates.Count;
                        cache.Updated = null;   // 사용자가 누른 갱신은 주기와 상관없이 받는다
                        msg = 공휴일받기(cache, key, years) ?? "";
                        if (cache.Updated.HasValue)
                        {
                            db.SaveHolidays(cache);
                            msg = string.Format("공휴일 {0}건 → {1}건. {2}", before, cache.Dates.Count, msg).Trim();
                        }
                    }
                }
                catch (Exception ex) { msg = "공휴일을 받지 못했습니다: " + ex.Message; }
                if (Log != null) Log("공휴일 갱신(웹): " + msg);
                lock (작업잠금) { 공휴일작업중 = false; 공휴일작업결과 = msg; }
            });
            t.IsBackground = true;
            t.Start();
            return new JObj().Set("ok", true).Set("started", true);
        }

        JObj BackupNow(Env env)
        {
            string path = Backups.지금백업(env.Db, 백업폴더(), DateTime.Now);
            return new JObj().Set("ok", true).Set("name", Path.GetFileName(path));
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
                .Set("name", it.카드이름)
                .Set("flow", it.진행흐름.ToString())
                .Set("month", it.월)
                .Set("day", it.말일 ? "말일" : it.일.ToString(Inv))
                .Set("lead", it.알림영업일)
                .Set("rule", it.금액규칙 ?? "")
                .Set("fixed", it.금액규칙 == AmountRules.고정 ? it.고정금액 : null)
                .Set("memo", it.비고 ?? "")
                .Set("siteName", it.홈페이지명 ?? "")
                .Set("siteUrl", it.홈페이지주소 ?? "")
                .Set("group", it.묶음 ?? "")
                .Set("paid", it.납부있음)
                .Set("stages", Stages.For(it))
                .Set("actions", 행동목록(it))
                .Set("hideStart", Stages.시작숨김(it))
                .Set("noAmount", it.진행흐름 == Flow.사용자설정 && it.금액없음)
                .Set("loan", it.차입)
                .Set("startYear", it.시작연도.HasValue ? (object)it.시작연도.Value : null)
                .Set("endYear", it.종료연도.HasValue ? (object)it.종료연도.Value : null);
        }

        /// <summary>각 지점에 도달할 때 누를 버튼 문구. 첫 칸(시작 지점)은 비어 있다.</summary>
        static string[] 행동목록(PaymentItem it)
        {
            string[] names = Stages.For(it);
            var list = new string[names.Length];
            list[0] = "";
            for (int i = 1; i < names.Length; i++) list[i] = Stages.다음행동(it, i - 1) ?? "";
            return list;
        }

        /// <summary>
        /// 발생 건 하나를 화면이 그대로 그릴 수 있는 모양으로 만든다.
        /// 문구와 판정은 여기서 끝낸다. 화면마다 규칙이 갈라지지 않게 하기 위해서다.
        /// </summary>
        static JObj Dto(Env env, Occurrence o, bool 오늘대기)
        {
            PaymentItem it = o.Item;
            string[] stages = Stages.For(it);
            StatusRecord st;
            env.Status.TryGetValue(o.Key, out st);
            int stage = st == null ? 0 : Math.Max(0, Math.Min(st.단계, stages.Length - 1));
            bool done = stage >= stages.Length - 1;
            int left = o.남은영업일(env.Cal, env.Today);

            string text;
            string severity;
            bool before = env.시작전(o);
            if (done) { text = "처리 완료"; severity = "done"; }
            else if (before) { text = "추적 시작 전"; severity = "before"; }
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
            // 오늘 '오늘은 대기' 를 눌렀고 그 뒤로 다른 동작이 없는 건 — 웹에서 대기 취소를 보인다 (ADR-0022).
            // 받은 알림은 목록을 만들기 전에 오늘 확인 표시를 지워 두므로(대기한 건도 화면에 남기려고) 오늘대기 로 대신 확인한다.
            // 진행도 오늘 확인 표시를 남기므로 마지막 동작이 '대기' 인지는 늘 본다.
            string 마지막;
            bool 오늘확인 = 오늘대기 || (st != null && st.최종확인일.HasValue && st.최종확인일.Value.Date == env.Today);
            bool deferredToday = !done && st != null && 오늘확인 &&
                env.마지막동작.TryGetValue(o.Key, out 마지막) && 마지막 == "대기";

            return env.기관사이트붙이기(new JObj()
                .Set("year", o.연도)
                .Set("id", it.Id)
                .Set("org", it.기관)
                .Set("name", it.카드이름)
                .Set("flow", it.진행흐름.ToString())
                .Set("stages", stages)
                .Set("hideStart", Stages.시작숨김(it))
                .Set("stage", stage)
                .Set("stageName", stages[stage])
                .Set("nextStage", done ? null : stages[stage + 1])
                .Set("nextAction", done ? Stages.끝남문구 : Stages.다음행동(it, stage))
                .Set("paid", it.납부있음)
                .Set("siteName", it.홈페이지명 ?? "")
                .Set("siteUrl", it.홈페이지주소 ?? "")
                .Set("group", it.묶음 ?? "")
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
                .Set("deferredToday", deferredToday)
                .Set("changedAt", st != null && st.변경일시.HasValue ? st.변경일시.Value.ToString("yyyy-MM-dd HH:mm", Inv) : null)
                .Set("attachments", env.Files.CountFor(o.연도, it.Id))
                .Set("beforeStart", before)
                .Set("loan", it.차입)
                .Set("memo", it.비고 ?? ""), it);
        }

        static void 연도확인(PaymentItem it, int year)
        {
            if (!it.해당연도(year))
                throw new HttpError(400, "이 항목은 " + year + "년에 기한이 없습니다 (유효연도 밖).");
        }

        static Occurrence OccurrenceOf(Env env, PaymentItem it, int year)
        {
            연도확인(it, year);
            var one = new List<PaymentItem> { it };
            foreach (Occurrence o in Scheduler.BuildOccurrences(one, env.Cal, new DateTime(year, 7, 1), env.Amounts))
                if (o.연도 == year) return o;
            throw new InvalidOperationException("발생 건을 만들지 못했습니다.");
        }

        static bool Done(Env env, Occurrence o)
        {
            StatusRecord st;
            return env.Status.TryGetValue(o.Key, out st) && st.단계 >= Stages.FinalIndex(o.Item);
        }

        static decimal? Amount(Occurrence o) { return AmountRules.금액(o); }

        static bool Unknown(Occurrence o) { return AmountRules.미확인(o); }

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

        static string[] 줄목록(string s)
        {
            string t = (s ?? "").Replace("\r", "");
            if (t.Trim().Length == 0) return new string[0];
            return t.Split('\n');
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

        /// <summary>
        /// 신고 홈페이지 주소. http/https 만 받는다 — javascript: 같은 주소가 링크로 박히면
        /// 누르는 순간 이 화면 안에서 스크립트가 돈다.
        /// </summary>
        static string 주소(string s)
        {
            string t = (s ?? "").Trim();
            if (t.Length == 0) return "";
            Uri u;
            if (t.Length > 300 || !Uri.TryCreate(t, UriKind.Absolute, out u) ||
                (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                throw new HttpError(400, "홈페이지 주소는 http:// 또는 https:// 로 시작해야 합니다.");
            return u.AbsoluteUri;
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
