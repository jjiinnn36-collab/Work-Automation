using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// 웹 화면 서버 시험. 임시 자료 폴더에 DB 를 만들고 실제 HTTP 로 두드린다.
    /// 오늘은 2026-09-17(목)로 고정한다.
    /// </summary>
    static class WebTests
    {
        static int passed = 0, failed = 0;
        static string Base;
        static string DataDir;
        static readonly List<string> 팝업요청 = new List<string>();

        static void Check(string name, object actual, object expected)
        {
            string a = Convert.ToString(actual, CultureInfo.InvariantCulture);
            string e = Convert.ToString(expected, CultureInfo.InvariantCulture);
            if (a == e) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  기대=" + e + " 실제=" + a); }
        }

        static void Has(string name, string text, string part)
        {
            if (text != null && text.Contains(part)) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  '" + part + "' 없음. 응답=" + Short(text)); }
        }

        static void Lacks(string name, string text, string part)
        {
            if (text != null && !text.Contains(part)) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  '" + part + "' 가 있으면 안 됨"); }
        }

        static string Short(string s)
        {
            if (s == null) return "(null)";
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }

        // ── HTTP ──
        sealed class Res
        {
            public int Status;
            public string Text;
            public byte[] Bytes;
            public WebHeaderCollection Headers;
        }

        static Res Send(string method, string path, byte[] body, string contentType, Dictionary<string, string> headers)
        {
            var req = (HttpWebRequest)WebRequest.Create(Base + path.TrimStart('/'));
            req.Method = method;
            req.Proxy = null;
            req.AllowAutoRedirect = false;
            req.Timeout = 15000;
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    if (kv.Key == "Host") req.Host = kv.Value;
                    else req.Headers[kv.Key] = kv.Value;
                }
            }
            if (body != null)
            {
                req.ContentType = contentType;
                req.ContentLength = body.Length;
                using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            }
            else if (method == "POST")
            {
                req.ContentLength = 0;
            }

            HttpWebResponse res;
            try { res = (HttpWebResponse)req.GetResponse(); }
            catch (WebException we)
            {
                res = we.Response as HttpWebResponse;
                if (res == null) throw;
            }

            using (res)
            using (var ms = new MemoryStream())
            {
                res.GetResponseStream().CopyTo(ms);
                var r = new Res();
                r.Status = (int)res.StatusCode;
                r.Bytes = ms.ToArray();
                r.Text = Encoding.UTF8.GetString(r.Bytes);
                r.Headers = res.Headers;
                return r;
            }
        }

        static Res Get(string path) { return Send("GET", path, null, null, null); }

        static Dictionary<string, string> 표시() { return new Dictionary<string, string> { { "X-PaymentAlert", "1" } }; }

        static Res Post(string path, string form)
        {
            return Send("POST", path, Encoding.UTF8.GetBytes(form), "application/x-www-form-urlencoded", 표시());
        }

        static string E(string s) { return Uri.EscapeDataString(s); }

        // ── 준비 ──
        static PaymentItem 항목(string id, string 기관, string 비용명, Flow f, int 월, bool 말일, int 일, int 알림, string 규칙, decimal? 고정)
        {
            var it = new PaymentItem();
            it.Id = id; it.기관 = 기관; it.비용명 = 비용명; it.진행흐름 = f; it.월 = 월;
            it.말일 = 말일; it.일 = 일; it.알림영업일 = 알림; it.금액규칙 = 규칙; it.고정금액 = 고정; it.비고 = "";
            return it;
        }

        static void 자료만들기()
        {
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                db.ReplaceMaster(new List<PaymentItem> {
                    항목("kofia-09", "금융투자협회", "협회회비", Flow.납부만, 9, false, 20, 3, "변동", null),
                    항목("fss-10", "금융감독원", "감독분담금", Flow.납부만, 10, true, 0, 3, "고정", 100000m),
                    항목("vat-q3", "국세청", "부가세 <3분기>", Flow.신고납부, 10, false, 25, 5, "변동", null)
                });
                db.SetStartDate(new DateTime(2026, 9, 1));

                var a = new AmountRecord();
                a.연도 = 2026; a.Id = "kofia-09"; a.금액 = 5838089m; a.출처 = "고지서"; a.확인일 = new DateTime(2026, 9, 10);
                db.UpsertAmounts(new AmountRecord[] { a });

                var h = new Holidays.Cache();
                h.Years.Add(2025); h.Years.Add(2026); h.Years.Add(2027);
                h.Dates[new DateTime(2026, 9, 24)] = "추석";
                h.Dates[new DateTime(2026, 9, 25)] = "추석";
                h.Dates[new DateTime(2026, 9, 26)] = "추석";
                h.Updated = new DateTime(2026, 9, 1);
                db.SaveHolidays(h);
            }
        }

        static StatusRecord 상태(string id)
        {
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                StatusRecord st;
                return db.LoadStatus().TryGetValue("2026\t" + id, out st) ? st : null;
            }
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            DataDir = Path.Combine(Path.GetTempPath(), "pa_web_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(DataDir);
            string webRoot = Path.GetFullPath(args.Length > 0 ? args[0] : "web");

            WebServer server = null;
            try
            {
                자료만들기();
                server = new WebServer(DataDir, webRoot, delegate { return new DateTime(2026, 9, 17); });
                server.팝업요청 = delegate(string id) { 팝업요청.Add(id); };
                server.Start(38600);
                Base = server.Url;

                화면파일();
                받은알림();
                요청막기();
                연간과이번달();
                단계바꾸기();
                금액();
                항목관리();
                이력();
                증빙();
                잘못된요청();
                동시변경과기록();
                항목규칙();
                설정과관리(server);
                분할납부();
                문서와순서와기록(server);
                납부서판독(server);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  FAIL  예외: " + ex);
            }
            finally
            {
                if (server != null) server.Dispose();
                try { Directory.Delete(DataDir, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine("  통과 {0}건 / 실패 {1}건", passed, failed);
            Console.WriteLine("==================================================");
            return failed == 0 ? 0 : 1;
        }

        static void 화면파일()
        {
            Console.WriteLine("\n[WEB-1] 화면 파일");
            Res r = Get("/");
            Check("첫 화면 200", r.Status, 200);
            Has("첫 화면 제목", r.Text, "납부 기한 알림");
            string csp = r.Headers["Content-Security-Policy"] ?? "";
            Has("CSP: 스크립트는 자기 출처와 해시만", csp, "script-src 'self' 'sha256-");
            Lacks("CSP: unsafe-inline 스크립트 없음", csp.Split(';')[1], "unsafe-inline");
            Has("CSP: 외부 연결 금지", csp, "connect-src 'self'");

            // 인라인 스크립트 해시가 실제 본문과 맞는지 — 어긋나면 화면이 하얗게 멈춘다.
            var sha = System.Security.Cryptography.SHA256.Create();
            bool 해시일치 = true; int 인라인 = 0;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(r.Text, @"<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)</script>"))
            {
                인라인++;
                string h = "'sha256-" + Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(m.Groups[1].Value))) + "'";
                if (!csp.Contains(h)) 해시일치 = false;
            }
            CheckTrue("인라인 스크립트 " + 인라인 + "개 모두 CSP 해시에 있음", 해시일치);

            string webRoot = Path.GetFullPath(Directory.Exists("web") ? "web" : Path.Combine("..", "web"));
            string[] chunks = Directory.Exists(Path.Combine(webRoot, "_next", "static"))
                ? Directory.GetFiles(Path.Combine(webRoot, "_next", "static"), "*.js", SearchOption.AllDirectories) : new string[0];
            CheckTrue("빌드된 화면 스크립트가 있음", chunks.Length > 0);
            if (chunks.Length > 0)
            {
                string rel = chunks[0].Substring(webRoot.Length).Replace('\\', '/');
                Res js = Get(rel);
                Check("화면 스크립트 200", js.Status, 200);
                Has("오래 보관 캐시 (이름에 해시)", js.Headers["Cache-Control"] ?? "", "immutable");
                Has("스크립트 형식", js.Headers["Content-Type"] ?? "", "javascript");
            }
            CheckTrue("페이지가 부르는 스크립트가 모두 web 폴더에 있음", 참조파일모두있음(r.Text, webRoot));
            Check("확장자 없는 경로는 .html", Get("/index").Status, 200);
            Check("목록에 없는 파일 404", Get("/secret.txt").Status, 404);
            Check("허용하지 않는 형식 404", Get("/web.config").Status, 404);
            Check("점으로 시작하는 경로 404", Get("/.git/config").Status, 404);
            Check("상위 폴더 경로 404", Get("/..%2fsrc%2fProgram.cs").Status, 404);
            Check("역슬래시 경로 404", Get("/_next%5c..%5c..%5csrc%5cDb.cs").Status, 404);
        }

        /// <summary>index.html 의 src/href 로 부르는 /_next 파일이 web 폴더에 전부 있는가 — 빌드 복사 누락 방지.</summary>
        static bool 참조파일모두있음(string html, string webRoot)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"(/_next/[^\"]+)\""))
            {
                string p = Path.Combine(webRoot, m.Groups[1].Value.TrimStart('/').Replace('/', '\\'));
                if (!File.Exists(p)) { Console.WriteLine("    빠진 파일: " + m.Groups[1].Value); return false; }
            }
            return true;
        }

        static void 받은알림()
        {
            Console.WriteLine("\n[WEB-2] 받은 알림");
            Res r = Get("/api/alerts");
            Check("200", r.Status, 200);
            Has("오늘 날짜", r.Text, "\"today\":\"2026-09-17\"");
            Has("협회회비 표시", r.Text, "\"id\":\"kofia-09\"");
            Has("D-2영업일 (9/20 일요일 → 9/21)", r.Text, "\"statusText\":\"D-2영업일\"");
            Has("보정 기한", r.Text, "\"payDue\":\"2026-09-21\"");
            Has("금액 문구", r.Text, "\"amountText\":\"5,838,089원\"");
            Has("다음 지점", r.Text, "\"nextStage\":\"전표발행\"");
            Has("다음 행동 (AC-W50)", r.Text, "\"nextAction\":\"전표 발행\"");
            Has("남은 건수 1", r.Text, "\"pending\":1");
            Has("알림 전 2건", r.Text, "\"notYet\":2");
            Lacks("알림일 전인 부가세는 없음", r.Text, "\"id\":\"vat-q3\"");
            Has("꺾쇠는 이스케이프", Get("/api/items").Text, "부가세 \\u003c3분기\\u003e");
        }

        static void 요청막기()
        {
            Console.WriteLine("\n[WEB-3] 다른 곳에서 온 요청 막기");
            Res r = Send("POST", "/api/advance", Encoding.UTF8.GetBytes("y=2026&id=kofia-09"),
                "application/x-www-form-urlencoded", null);
            Check("표시 머리글 없으면 403", r.Status, 403);

            var h = 표시();
            h["Origin"] = "http://evil.example";
            r = Send("POST", "/api/advance", Encoding.UTF8.GetBytes("y=2026&id=kofia-09"), "application/x-www-form-urlencoded", h);
            Check("다른 Origin 403", r.Status, 403);

            var h2 = new Dictionary<string, string> { { "Host", "evil.example:" + new Uri(Base).Port } };
            r = Send("GET", "/api/alerts", null, null, h2);
            CheckTrue("다른 Host 거절", r.Status == 403 || r.Status == 400);

            StatusRecord st = 상태("kofia-09");
            CheckTrue("막힌 요청은 기록 안 됨", st == null || st.단계 == 0);
            Check("PUT 405", Send("PUT", "/api/advance", new byte[0], "text/plain", 표시()).Status, 405);
        }

        static void CheckTrue(string name, bool c)
        {
            if (c) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        static void 연간과이번달()
        {
            Console.WriteLine("\n[WEB-4] 연간·이번 달");
            Res r = Get("/api/year?y=2026");
            Check("연간 200", r.Status, 200);
            Has("전체 3건", r.Text, "\"count\":3");
            Has("진행중 1", r.Text, "\"inProgress\":1");
            Has("진행예정 2", r.Text, "\"upcoming\":2");
            Has("금액 미확인 1 (부가세)", r.Text, "\"amountUnknown\":1");
            Has("기관 목록", r.Text, "\"orgs\":[");
            Has("고정금액 반영", r.Text, "\"amountText\":\"100,000원\"");

            r = Get("/api/month?ym=2026-10");
            Check("10월 200", r.Status, 200);
            Has("10월 합계는 고정금액만", r.Text, "\"total\":100000");
            Has("10월 미확인 1", r.Text, "\"amountUnknown\":1");
            Has("10월 말일 → 11/2", r.Text, "\"payDue\":\"2026-11-02\"");
            Has("10월 진행중 2", r.Text, "\"inProgress\":2");

            Check("시작일 이전 해는 비어 있음", Get("/api/year?y=2025").Text.Contains("\"count\":0"), true);
        }

        static void 단계바꾸기()
        {
            Console.WriteLine("\n[WEB-5] 진행·대기·되돌리기");
            Res r = Post("/api/advance", "y=2026&id=kofia-09");
            Check("진행 200", r.Status, 200);
            Has("단계 1", r.Text, "\"stage\":1");
            StatusRecord st = 상태("kofia-09");
            Check("DB 단계 1", st.단계, 1);
            Check("DB 최종확인일 오늘", st.최종확인일.Value.ToString("yyyy-MM-dd"), "2026-09-17");

            r = Get("/api/alerts");
            Has("진행한 건도 화면에 남음", r.Text, "\"id\":\"kofia-09\"");
            Has("오늘 확인 표시", r.Text, "\"confirmedToday\":true");
            Has("남은 건수 0", r.Text, "\"pending\":0");

            r = Post("/api/revert", "y=2026&id=kofia-09");
            Check("되돌리기 200", r.Status, 200);
            st = 상태("kofia-09");
            Check("DB 단계 0", st.단계, 0);
            CheckTrue("되돌리면 최종확인일 비움", !st.최종확인일.HasValue);
            Check("첫 단계에서 되돌리기 409", Post("/api/revert", "y=2026&id=kofia-09").Status, 409);

            r = Post("/api/defer", "y=2026&id=kofia-09");
            Check("대기 200", r.Status, 200);
            st = 상태("kofia-09");
            Check("대기는 단계 그대로", st.단계, 0);
            Check("대기는 최종확인일 오늘", st.최종확인일.Value.ToString("yyyy-MM-dd"), "2026-09-17");

            Post("/api/advance", "y=2026&id=kofia-09");
            r = Post("/api/advance", "y=2026&id=kofia-09");
            Has("마지막 단계 도달", r.Text, "\"done\":true");
            Check("마지막 단계에서 진행 409", Post("/api/advance", "y=2026&id=kofia-09").Status, 409);
            Lacks("끝난 건은 알림에서 빠짐", Get("/api/alerts").Text, "\"id\":\"kofia-09\"");
            Check("없는 항목 404", Post("/api/advance", "y=2026&id=nope").Status, 404);
        }

        static void 금액()
        {
            Console.WriteLine("\n[WEB-6] 금액");
            Res r = Post("/api/amount", "y=2026&id=vat-q3&amount=" + E("1,234,500원"));
            Check("저장 200", r.Status, 200);
            Has("금액 문구", r.Text, "\"amountText\":\"1,234,500원\"");
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
                Check("DB 금액", db.LoadAmounts()["2026\tvat-q3"].금액, 1234500m);

            Check("음수 400", Post("/api/amount", "y=2026&id=vat-q3&amount=-5").Status, 400);
            Check("소수 400", Post("/api/amount", "y=2026&id=vat-q3&amount=12.5").Status, 400);
            Check("글자 400", Post("/api/amount", "y=2026&id=vat-q3&amount=abc").Status, 400);
            Check("빈칸 400", Post("/api/amount", "y=2026&id=vat-q3&amount=").Status, 400);

            Check("삭제 200", Post("/api/amount/delete", "y=2026&id=vat-q3").Status, 200);
            Has("다시 미확인", Get("/api/month?ym=2026-10").Text, "\"amountText\":\"미확인\"");
        }

        static void 항목관리()
        {
            Console.WriteLine("\n[WEB-7] 항목 관리");
            string 기본 = "&org=" + E("국세청") + "&name=" + E("교육세") + "&flow=" + E("신고납부") + "&lead=4&rule=" + E("변동");
            Check("2월 29일은 거절", Post("/api/item", "mode=new&id=edu-02&month=2&day=29" + 기본).Status, 400);
            Check("2월 28일은 추가", Post("/api/item", "mode=new&id=edu-02&month=2&day=28" + 기본).Status, 200);
            Check("같은 id 새로 추가 409", Post("/api/item", "mode=new&id=edu-02&month=2&day=28" + 기본).Status, 409);
            Check("id 공백 400", Post("/api/item", "mode=new&id=" + E("bad id") + "&month=2&day=28" + 기본).Status, 400);
            Check("흐름 오류 400", Post("/api/item", "mode=new&id=x1&month=2&day=28&org=a&name=b&flow=xx").Status, 400);
            Check("월 13 400", Post("/api/item", "mode=new&id=x1&month=13&day=1" + 기본).Status, 400);
            Check("기관 빈칸 400", Post("/api/item", "mode=new&id=x1&month=1&day=1&org=&name=b&flow=" + E("납부만")).Status, 400);
            Check("없는 항목 수정 404", Post("/api/item", "mode=edit&id=nope&month=1&day=1" + 기본).Status, 404);

            Check("말일로 수정", Post("/api/item", "mode=edit&id=edu-02&month=2&day=" + E("말일") + "&fixed=" + E("30,000") + 기본.Replace(E("변동"), E("고정"))).Status, 200);
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                List<PaymentItem> m = db.LoadMaster();
                Check("항목 4건", m.Count, 4);
                PaymentItem e = m.Find(delegate(PaymentItem x) { return x.Id == "edu-02"; });
                Check("말일 저장", e.말일, true);
                Check("고정금액 저장", e.고정금액, 30000m);
                Check("알림 영업일", e.알림영업일, 4);
                Check("새 항목은 맨 뒤", m[3].Id, "edu-02");
            }
            Has("목록에 나옴", Get("/api/items").Text, "\"id\":\"edu-02\"");

            Has("알릴 때가 아닌 수정은 팝업 없음", Post("/api/item", "mode=edit&id=edu-02&month=2&day=28" + 기본).Text, "\"popup\":false");
            Check("팝업 요청 안 함", 팝업요청.Count, 0);

            // 부가세 기한을 9/22 로 당기면 5영업일 전인 9/15 가 알림일 → 오늘(9/17) 알릴 건
            string vat = "&org=" + E("국세청") + "&name=" + E("부가세") + "&flow=" + E("신고납부") + "&lead=5&rule=" + E("변동");
            Res pr = Post("/api/item", "mode=edit&id=vat-q3&month=9&day=22" + vat);
            Has("당겨서 오늘 알릴 건이면 팝업", pr.Text, "\"popup\":true");
            Check("팝업 요청 1번", 팝업요청.Count, 1);
            Check("요청한 항목", 팝업요청.Count > 0 ? 팝업요청[0] : "", "vat-q3");

            Post("/api/defer", "y=2026&id=vat-q3");
            Has("오늘 이미 대기한 건은 팝업 없음", Post("/api/item", "mode=edit&id=vat-q3&month=9&day=23" + vat).Text, "\"popup\":false");
            Check("팝업 요청 그대로 1번", 팝업요청.Count, 1);
            Post("/api/item", "mode=edit&id=vat-q3&month=10&day=25" + vat);

            Check("삭제 200", Post("/api/item/delete", "id=edu-02").Status, 200);
            Check("없는 항목 삭제 404", Post("/api/item/delete", "id=edu-02").Status, 404);
            Lacks("삭제 후 목록에 없음", Get("/api/items").Text, "\"id\":\"edu-02\"");
        }

        static void 이력()
        {
            Console.WriteLine("\n[WEB-8] 이력");
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            Res r = Get("/api/history?from=2000-01-01&to=2100-12-31");
            Check("200", r.Status, 200);
            Has("끝낸 협회회비", r.Text, "\"id\":\"kofia-09\"");
            Has("처리일은 누른 날", r.Text, "\"doneAt\":\"" + today + "\"");
            Has("합계", r.Text, "\"total\":5838089");
            Lacks("진행중인 건은 없음", r.Text, "\"id\":\"vat-q3\"");
            Check("끝이 시작보다 빠르면 400", Get("/api/history?from=2026-12-01&to=2026-01-01").Status, 400);
            Check("날짜 형식 400", Get("/api/history?from=2026/01/01").Status, 400);
        }

        static void 증빙()
        {
            Console.WriteLine("\n[WEB-9] 증빙");
            byte[] body = Encoding.UTF8.GetBytes("hello receipt");
            var h = 표시();
            h["X-File-Name"] = E("영수증 9월.pdf");
            Res r = Send("POST", "/api/attach?y=2026&id=kofia-09", body, "application/octet-stream", h);
            Check("첨부 200", r.Status, 200);
            Has("건수 1", r.Text, "\"count\":1");

            string folder = Path.Combine(DataPaths.증빙(DataDir), "2026", "kofia-09");
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder) : new string[0];
            Check("증빙 폴더에 복사됨", files.Length, 1);

            r = Get("/api/attachments?y=2026&id=kofia-09");
            Has("원본 이름", r.Text, "\"name\":\"영수증 9월.pdf\"");
            Has("첨부 시점 단계", r.Text, "\"stage\":\"납부\"");

            string saved;
            using (Store db = Store.Open(DataPaths.Db(DataDir))) saved = db.LoadAttachments()[0].저장파일;

            r = Get("/api/file?y=2026&id=kofia-09&f=" + E(saved));
            Check("열기 200", r.Status, 200);
            Check("내용 그대로", r.Text, "hello receipt");
            Check("PDF 형식", r.Headers["Content-Type"], "application/pdf");
            Lacks("PDF 는 보기창에서 열리게 sandbox 없음 (ADR-0011)", r.Headers["Content-Security-Policy"] ?? "", "sandbox");
            Has("PDF 도 스크립트는 막음", r.Headers["Content-Security-Policy"] ?? "", "default-src 'none'");

            Check("목록에 없는 경로 404", Get("/api/file?y=2026&id=kofia-09&f=" + E("..\\..\\납부알림.db")).Status, 404);
            Check("다른 건 이름으로 404", Get("/api/file?y=2026&id=vat-q3&f=" + E(saved)).Status, 404);

            h = 표시();
            h["X-File-Name"] = E("a.txt");
            Check("빈 파일 400", Send("POST", "/api/attach?y=2026&id=kofia-09", new byte[0], "application/octet-stream", h).Status, 400);
            h["X-PaymentAlert"] = "0";
            Check("표시 없는 첨부 403", Send("POST", "/api/attach?y=2026&id=kofia-09", body, "application/octet-stream", h).Status, 403);

            r = Post("/api/attach/delete", "y=2026&id=kofia-09&f=" + E(saved));
            Check("삭제 200", r.Status, 200);
            Has("건수 0", r.Text, "\"count\":0");
            Check("파일도 지워짐", Directory.GetFiles(folder).Length, 0);
            Check("다시 지우면 404", Post("/api/attach/delete", "y=2026&id=kofia-09&f=" + E(saved)).Status, 404);
        }

        static void 잘못된요청()
        {
            Console.WriteLine("\n[WEB-10] 잘못된 요청");
            Check("월 형식 400", Get("/api/month?ym=2026-9x").Status, 400);
            Check("연도 형식 400", Get("/api/year?y=abc").Status, 400);
            Check("연도 범위 400", Get("/api/year?y=1999").Status, 400);
            Check("없는 기능 404", Get("/api/nothing").Status, 404);
            Res r = Get("/api/nothing");
            Has("오류도 JSON", r.Text, "\"error\":");
            Check("nosniff", r.Headers["X-Content-Type-Options"], "nosniff");
        }

        static void 동시변경과기록()
        {
            Console.WriteLine("\n[WEB-11] 화면이 본 단계와 DB 가 다르면 거절, 변경은 '웹' 으로 기록 (ADR-0004)");
            Res r = Post("/api/advance", "y=2026&id=fss-10&stage=0");
            Check("본 단계가 맞으면 진행", r.Status, 200);
            r = Post("/api/advance", "y=2026&id=fss-10&stage=0");
            Check("옛 화면(단계 0)으로 다시 누르면 409", r.Status, 409);
            Has("이유를 알려 줌", r.Text, "다른 창에서");
            Check("단계는 한 번만 올라감", 상태("fss-10").단계, 1);
            Check("단계 값이 이상하면 400", Post("/api/advance", "y=2026&id=fss-10&stage=9").Status, 400);

            // 팝업이 그사이 되돌린 상황
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
                db.Revert(2026, "fss-10", Flow.납부만, 1, DateTime.Now, "팝업");
            Check("팝업이 되돌린 뒤 웹의 옛 화면(1)으로 되돌리기 409", Post("/api/revert", "y=2026&id=fss-10&stage=1").Status, 409);

            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                List<StatusEvent> ev = db.LoadEvents(2026, "fss-10");
                Check("성공한 두 번만 기록", ev.Count, 2);
                Check("최근은 팝업 되돌리기", ev[0].출처 + "/" + ev[0].동작, "팝업/되돌리기");
                Check("그 전은 웹 진행", ev[1].출처 + "/" + ev[1].동작, "웹/진행");
                List<StatusEvent> kev = db.LoadEvents(2026, "kofia-09");
                CheckTrue("금액·첨부·첨부삭제도 웹 출처로 남음",
                    kev.Exists(delegate(StatusEvent e) { return e.동작 == "첨부" && e.출처 == "웹"; }) &&
                    kev.Exists(delegate(StatusEvent e) { return e.동작 == "첨부삭제"; }));
                List<StatusEvent> vev = db.LoadEvents(2026, "vat-q3");
                CheckTrue("금액 입력·삭제 기록", vev.Exists(delegate(StatusEvent e) { return e.동작 == "금액"; }) &&
                    vev.Exists(delegate(StatusEvent e) { return e.동작 == "금액삭제"; }));
            }
        }

        static void 항목규칙()
        {
            Console.WriteLine("\n[WEB-12] 금액규칙 고정/변동, 홈페이지 주소 (AC-W25~W27, W92)");
            string 공통 = "&org=" + E("기관") + "&name=" + E("시험") + "&month=3&day=10";
            Check("옛 규칙 값은 거절", Post("/api/item", "mode=new&id=r1&flow=" + E("납부만") + "&rule=" + E("고지수령") + 공통).Status, 400);
            Check("고정인데 금액이 없으면 거절", Post("/api/item", "mode=new&id=r1&flow=" + E("납부만") + "&rule=" + E("고정") + 공통).Status, 400);
            Check("변동은 금액 없이 저장", Post("/api/item", "mode=new&id=r1&flow=" + E("납부만") + "&rule=" + E("변동") + 공통).Status, 200);
            Res r = Post("/api/item", "mode=edit&id=r1&flow=" + E("납부만") + "&rule=" + E("변동") + "&fixed=5000" + 공통);
            Has("변동에 금액을 넣어도 마스터 금액이 되지 않음 (AC-W26a)", r.Text, "\"fixed\":null");
            r = Post("/api/item", "mode=edit&id=r1&flow=" + E("제출만") + "&rule=" + E("고정") + "&fixed=5000" + 공통);
            Check("제출만은 고정이어도 저장", r.Status, 200);
            Has("제출만은 금액 없음 (AC-W27)", r.Text, "\"paid\":false");

            Check("javascript: 주소 거절", Post("/api/item", "mode=edit&id=r1&flow=" + E("납부만") + 공통 + "&siteUrl=" + E("javascript:alert(1)")).Status, 400);
            Check("ftp 주소 거절", Post("/api/item", "mode=edit&id=r1&flow=" + E("납부만") + 공통 + "&siteUrl=" + E("ftp://x")).Status, 400);
            r = Post("/api/item", "mode=edit&id=r1&flow=" + E("납부만") + 공통 + "&siteName=" + E("홈택스") + "&siteUrl=" + E("https://hometax.go.kr"));
            Check("https 주소 저장", r.Status, 200);
            Has("주소 정규화", r.Text, "\"siteUrl\":\"https://hometax.go.kr/\"");
            Has("이름 저장", Get("/api/items").Text, "\"siteName\":\"홈택스\"");
            r = Post("/api/item", "mode=edit&id=r1&flow=" + E("납부만") + 공통 + "&siteUrl=" + E("https://example.com/a"));
            Has("이름 없이 주소만 넣으면 '홈페이지'", r.Text, "\"siteName\":\"홈페이지\"");
            Post("/api/item/delete", "id=r1");
        }

        static void 설정과관리(WebServer server)
        {
            Console.WriteLine("\n[WEB-13] 설정: 시작일·인증키·공휴일 갱신·백업·상태 (ADR-0008)");
            Res r = Get("/api/health");
            Check("health 200", r.Status, 200);
            Has("판 번호", r.Text, "\"version\":\"" + AppInfo.버전 + "\"");
            Has("스키마 2", r.Text, "\"schema\":2");

            r = Get("/api/settings");
            Check("settings 200", r.Status, 200);
            Has("무결성 ok", r.Text, "\"integrity\":\"ok\"");
            Has("인증키 없음", r.Text, "\"apiKeySet\":false");
            Has("공휴일 연도", r.Text, "\"holidayYears\":[2025,2026,2027]");
            Has("추적 시작일", r.Text, "\"startDate\":\"2026-09-01\"");

            Check("시작일 형식 오류 400", Post("/api/settings/start-date", "date=2026/09/01").Status, 400);
            Check("시작일 바꾸기", Post("/api/settings/start-date", "date=2026-08-01").Status, 200);
            Has("바뀐 시작일", Get("/api/settings").Text, "\"startDate\":\"2026-08-01\"");
            Check("시작일 비우기", Post("/api/settings/start-date", "date=").Status, 200);
            Has("시작일 없음", Get("/api/settings").Text, "\"startDate\":null");
            Post("/api/settings/start-date", "date=2026-09-01");

            Check("인증키 없이 갱신은 400", Post("/api/holidays/refresh", "").Status, 400);
            Check("공백 든 인증키 거절", Post("/api/settings/apikey", "key=" + E("ab cd")).Status, 400);
            Check("인증키 저장", Post("/api/settings/apikey", "key=SECRET-KEY-123").Status, 200);
            r = Get("/api/settings");
            Has("인증키 있음 표시", r.Text, "\"apiKeySet\":true");
            Lacks("인증키 값은 화면에 돌려주지 않음", r.Text, "SECRET-KEY-123");
            Check("파일에 저장", File.ReadAllText(DataPaths.ApiKey(DataDir)), "SECRET-KEY-123");

            // 네트워크 대신 가짜로 받아 온다
            string 받은키 = null;
            server.공휴일받기 = delegate(Holidays.Cache c, string key, List<int> years)
            {
                받은키 = key;
                System.Threading.Thread.Sleep(300);
                c.Dates[new DateTime(2026, 10, 9)] = "한글날";
                foreach (int y in years) c.Years.Add(y);
                c.Updated = new DateTime(2026, 9, 17);
                return "가짜 갱신";
            };
            Check("갱신 시작 200", Post("/api/holidays/refresh", "").Status, 200);
            Check("도는 중에 또 누르면 409", Post("/api/holidays/refresh", "").Status, 409);
            for (int i = 0; i < 50 && Get("/api/settings").Text.Contains("\"running\":true"); i++) System.Threading.Thread.Sleep(100);
            r = Get("/api/settings");
            Has("끝나면 결과 문구", r.Text, "가짜 갱신");
            Has("공휴일 수 늘어남", r.Text, "\"holidayCount\":4");
            Check("저장한 인증키로 호출", 받은키, "SECRET-KEY-123");

            Check("인증키 지우기", Post("/api/settings/apikey", "key=").Status, 200);
            CheckTrue("파일도 지워짐", !File.Exists(DataPaths.ApiKey(DataDir)));

            r = Post("/api/backup", "");
            Check("지금 백업 200", r.Status, 200);
            Has("수동 사본 이름", r.Text, "납부알림-수동-");
            Has("목록에 보임", Get("/api/settings").Text, "납부알림-수동-");
            Has("받은 알림에 경고 칸", Get("/api/alerts").Text, "\"warnings\":[");
        }

        static void 분할납부()
        {
            Console.WriteLine("\n[WEB-14] 분할납부: 월 쉼표로 회차 만들기, 회차 금액 한 창 입력 (AC-W32~W42, ADR-0010)");
            string 공통 = "&org=" + E("협회") + "&name=" + E("연회비") + "&flow=" + E("납부만") + "&rule=" + E("변동") + "&day=20";
            Check("같은 월 두 번 400", Post("/api/item", "mode=new&id=dues&month=" + E("5,5") + 공통).Status, 400);
            Check("13월 400", Post("/api/item", "mode=new&id=dues&month=" + E("5,13") + 공통).Status, 400);
            Check("고칠 때 쉼표 400", Post("/api/item", "mode=edit&id=fss-10&month=" + E("5,6") + 공통).Status, 400);

            Res r = Post("/api/item", "mode=new&id=dues&month=" + E("7, 5,6") + 공통 +
                "&amountYear=2027&source=" + E("회비 안내문") + "&amt_05=" + E("1,000,000") + "&amt_07=999998");
            Check("세 회차 생성 200", r.Status, 200);
            Has("id 는 접두-월, 월 순서", r.Text, "\"ids\":[\"dues-05\",\"dues-06\",\"dues-07\"]");
            Has("금액 두 건 저장", r.Text, "\"amounts\":2");
            Has("목록에 묶음", Get("/api/items").Text, "\"group\":\"dues\"");
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                Dictionary<string, AmountRecord> am = db.LoadAmounts();
                Check("연도별 금액으로 저장 (AC-W41)", am["2027\tdues-05"].금액, 1000000m);
                Check("출처 한 번 입력", am["2027\tdues-07"].출처, "회비 안내문");
                CheckTrue("비운 회차는 미확인으로 남음 — 나눠 채우지 않음 (AC-W37, W40)", !am.ContainsKey("2027\tdues-06"));
                CheckTrue("마스터 고정금액이 되지 않음", !db.LoadMaster().Find(delegate(PaymentItem x) { return x.Id == "dues-05"; }).고정금액.HasValue);
            }
            Check("같은 회차 id 가 있으면 409", Post("/api/item", "mode=new&id=dues&month=" + E("6,8") + 공통).Status, 409);

            r = Get("/api/group?y=2027&group=dues");
            Check("묶음 조회 200", r.Status, 200);
            Has("회차 3건", r.Text, "\"count\":3");
            Has("합계 (AC-W35)", r.Text, "\"total\":1999998");
            Has("입력된 회차 2", r.Text, "\"entered\":2");
            Check("없는 묶음 404", Get("/api/group?y=2027&group=nope").Status, 404);

            Check("금액 없이 저장 400 (AC-W89)", Post("/api/group/amounts", "y=2027&group=dues&amt_dues-06=").Status, 400);
            r = Post("/api/group/amounts", "y=2027&group=dues&source=" + E("재안내") + "&amt_dues-06=" + E("1,000,000") + "&amt_dues-07=1000000");
            Check("회차 금액 한 번에 저장 200", r.Status, 200);
            Has("저장 2건", r.Text, "\"saved\":2");
            Has("합계 갱신", r.Text, "\"total\":3000000");
            Check("음수 회차 금액 400", Post("/api/group/amounts", "y=2027&group=dues&amt_dues-05=-1").Status, 400);

            foreach (string id in new string[] { "dues-05", "dues-06", "dues-07" }) Post("/api/item/delete", "id=" + id);
        }

        static void 문서와순서와기록(WebServer server)
        {
            Console.WriteLine("\n[WEB-15] 받은 문서 첨부·기본 프로그램으로 열기·순서 옮기기·변경 기록 (ADR-0011)");
            string 연파일 = null;
            server.파일열기 = delegate(string p) { 연파일 = p; };

            var h = 표시();
            h["X-File-Name"] = E("산출내역.xlsx");
            Res r = Send("POST", "/api/attach?y=2026&id=vat-q3&kind=" + E("받은문서"), Encoding.UTF8.GetBytes("xlsx"), "application/octet-stream", h);
            Check("받은 문서 첨부 200", r.Status, 200);
            r = Get("/api/attachments?y=2026&id=vat-q3");
            Has("종류가 받은문서", r.Text, "\"kind\":\"받은문서\"");
            string saved;
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
                saved = db.LoadAttachments().Find(delegate(Attachment a) { return a.Id == "vat-q3"; }).저장파일;

            Res xf = Get("/api/file?y=2026&id=vat-q3&f=" + E(saved));
            Has("xlsx 는 내려받기", xf.Headers["Content-Disposition"] ?? "", "attachment");
            Has("xlsx 는 sandbox", xf.Headers["Content-Security-Policy"] ?? "", "sandbox");
            Check("기본 프로그램으로 열기 200", Post("/api/open", "y=2026&id=vat-q3&f=" + E(saved)).Status, 200);
            CheckTrue("증빙 폴더 안의 그 파일을 연다", 연파일 != null && 연파일.EndsWith(saved) && 연파일.StartsWith(DataPaths.증빙(DataDir)));
            Check("목록에 없는 파일 404", Post("/api/open", "y=2026&id=vat-q3&f=" + E("..\\..\\납부알림.db")).Status, 404);
            Check("표시 없는 열기 403", Send("POST", "/api/open", Encoding.UTF8.GetBytes("y=2026&id=vat-q3&f=" + E(saved)), "application/x-www-form-urlencoded", null).Status, 403);

            Check("잘못된 방향 400", Post("/api/item/move", "id=vat-q3&dir=left").Status, 400);
            r = Post("/api/item/move", "id=vat-q3&dir=up");
            Has("위로 옮김", r.Text, "\"moved\":true");
            string items = Get("/api/items").Text;
            CheckTrue("vat-q3 가 fss-10 보다 앞", items.IndexOf("\"id\":\"vat-q3\"") < items.IndexOf("\"id\":\"fss-10\""));

            r = Get("/api/events?y=2026&id=fss-10");
            Check("한 건 기록 200", r.Status, 200);
            Has("단계 이름으로 풀어 줌", r.Text, "\"to\":\"전표발행\"");
            Has("출처", r.Text, "\"source\":\"웹\"");
            r = Get("/api/events?from=2000-01-01&to=2100-12-31");
            Has("기간 기록에 문서첨부", r.Text, "\"action\":\"문서첨부\"");
            Has("설정 변경도 보임", r.Text, "\"name\":\"설정\"");
        }

        static void 납부서판독(WebServer server)
        {
            Console.WriteLine("\n[WEB-16] 부가세 납부서 판독 → 반영 제안, 검산 불일치·후보 없음, 느린 판독에도 서버는 응답 (AC-W17·W18·W36)");
            byte[] pdf = Encoding.UTF8.GetBytes("%PDF-1.4 fake");
            var h = 표시();

            server.판독기 = delegate(string path) { return "ok\ttrue\ndue\t2026-10-26\nvat\t1200000\nedu\t0\nfarm\t0\nsurcharge\t0\nsum\t1200000\ntotal\t1200000\n"; };
            Res r = Send("POST", "/api/import/vat", pdf, "application/pdf", h);
            Check("판독 200", r.Status, 200);
            Has("반영 제안", r.Text, "\"ok\":true");
            Has("10월 부가세 항목", r.Text, "\"id\":\"vat-q3\"");
            Has("금액", r.Text, "\"amount\":1200000");
            Has("해당 연도", r.Text, "\"year\":2026");

            server.판독기 = delegate(string path) { return "ok\tfalse\ncode\t6\nmessage\t세목 합계와 문서상 '계' 가 일치하지 않아 반영하지 않습니다.\nsum\t100\ntotal\t101\n"; };
            r = Send("POST", "/api/import/vat", pdf, "application/pdf", h);
            Has("검산 불일치는 제안하지 않음", r.Text, "\"ok\":false");
            Has("이유", r.Text, "일치하지 않아");
            Has("대조 숫자", r.Text, "\"total\":101");

            server.판독기 = delegate(string path) { return "ok\ttrue\ndue\t2026-03-25\ntotal\t500\nsum\t500\n"; };
            r = Send("POST", "/api/import/vat", pdf, "application/pdf", h);
            Has("해당 월 부가세가 없으면 제안하지 않음", r.Text, "해당하는 부가세 항목이 없습니다");

            server.판독기 = delegate(string path) { return "아무 말"; };
            Check("결과 없는 도구 502", Send("POST", "/api/import/vat", pdf, "application/pdf", h).Status, 502);
            Check("빈 파일 400", Send("POST", "/api/import/vat", new byte[0], "application/pdf", h).Status, 400);
            Check("표시 없는 판독 403", Send("POST", "/api/import/vat", pdf, "application/pdf", null).Status, 403);

            // 실제 프로세스 경로: 느린 도구는 한도에서 끊고, 그동안 다른 요청은 응답한다.
            string fakeBase = Path.Combine(Path.GetTempPath(), "pa_fake_tools_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(Path.Combine(fakeBase, "tools"));
            File.WriteAllText(Path.Combine(fakeBase, "tools", "import-notice.ps1"), "param($Pdf,[switch]$Result)\r\nStart-Sleep -Seconds 20\r\n", new UTF8Encoding(true));
            string oldBase = server.BaseDir;
            server.BaseDir = fakeBase;
            server.판독기 = null;
            server.판독제한초 = 3;
            try
            {
                Res slow = null;
                var t = new System.Threading.Thread(delegate() { slow = Send("POST", "/api/import/vat", pdf, "application/pdf", 표시()); });
                var sw = System.Diagnostics.Stopwatch.StartNew();
                t.Start();
                System.Threading.Thread.Sleep(700);
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                Res health = Get("/api/health");
                CheckTrue("판독 중에도 다른 요청은 바로 응답 (1초 이내)", health.Status == 200 && sw2.ElapsedMilliseconds < 1000);
                t.Join(30000);
                Check("한도 넘기면 504", slow != null ? slow.Status : -1, 504);
                CheckTrue("한도 근처에서 끊음 (15초 안)", sw.ElapsedMilliseconds < 15000);
            }
            finally
            {
                server.BaseDir = oldBase;
                try { Directory.Delete(fakeBase, true); } catch { }
            }
        }
    }
}
