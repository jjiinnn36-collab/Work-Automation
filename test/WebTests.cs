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
            Has("CSP 머리글", r.Headers["Content-Security-Policy"] ?? "", "script-src 'self'");
            Check("app.js 200", Get("/app.js").Status, 200);
            Check("app.css 200", Get("/app.css").Status, 200);
            Check("목록에 없는 파일 404", Get("/secret.txt").Status, 404);
            Check("상위 폴더 경로 404", Get("/..%2fsrc%2fProgram.cs").Status, 404);
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
            Has("다음 단계", r.Text, "\"nextStage\":\"전표결재\"");
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

            Check("말일로 수정", Post("/api/item", "mode=edit&id=edu-02&month=2&day=" + E("말일") + "&fixed=" + E("30,000") + 기본).Status, 200);
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
            Has("첨부 시점 단계", r.Text, "\"stage\":\"납부완료\"");

            string saved;
            using (Store db = Store.Open(DataPaths.Db(DataDir))) saved = db.LoadAttachments()[0].저장파일;

            r = Get("/api/file?y=2026&id=kofia-09&f=" + E(saved));
            Check("열기 200", r.Status, 200);
            Check("내용 그대로", r.Text, "hello receipt");
            Check("PDF 형식", r.Headers["Content-Type"], "application/pdf");
            Has("샌드박스", r.Headers["Content-Security-Policy"] ?? "", "sandbox");

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
    }
}
