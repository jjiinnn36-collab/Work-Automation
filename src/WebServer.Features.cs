using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace PaymentAlert
{
    /// <summary>
    /// 웹 서버 중 분할납부·문서 열기·순서·변경 기록.
    /// </summary>
    public sealed partial class WebServer
    {
        /// <summary>파일을 OS 기본 프로그램으로 연다 (AC-W106). 시험에서 바꿔 끼운다.</summary>
        public Action<string> 파일열기 = delegate(string path)
        {
            var psi = new ProcessStartInfo(path);
            psi.UseShellExecute = true;
            using (Process.Start(psi)) { }
        };

        // ══ 분할납부 ═══════════════════════════════════════════════ ADR-0010

        /// <summary>"5, 6,7" → [5,6,7]. 1~12, 겹침 없음. 쉼표가 없으면 한 개.</summary>
        static List<int> 월목록(string s)
        {
            var list = new List<int>();
            string t = (s ?? "").Trim();
            if (t.IndexOf(',') < 0)
            {
                list.Add(정수(t, "월", 1, 12));
                return list;
            }
            foreach (string part in t.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int m = 정수(p, "월", 1, 12);
                if (list.Contains(m)) throw new HttpError(400, "같은 월이 두 번 들어 있습니다: " + m + "월");
                list.Add(m);
            }
            if (list.Count == 0) throw new HttpError(400, "월을 넣어 주세요.");
            list.Sort();
            return list;
        }

        /// <summary>
        /// 회차마다 {id}-{MM} 항목을 만들고 묶음 이름을 id 로 둔다.
        /// 회차 금액(amt_MM)을 같이 보내면 그 해 연도별 금액으로 저장한다 — 마스터 고정금액이 아니다 (AC-W41).
        /// 비워 둔 회차는 미확인으로 남는다 (AC-W40). 금액을 나눠 채우지 않는다 (AC-W37).
        /// </summary>
        JObj 분할항목추가(Env env, NameValueCollection f, string baseId, List<int> months)
        {
            if (baseId.Length > 37) throw new HttpError(400, "분할납부 id 는 37자까지입니다 (뒤에 -월 이 붙습니다).");

            var items = new List<PaymentItem>();
            foreach (int m in months)
            {
                string id = baseId + "-" + m.ToString("00", Inv);
                if (env.Item(id) != null) throw new HttpError(409, "같은 id 의 항목이 이미 있습니다: " + id);
                PaymentItem it = 항목읽기(env, f, id, m);
                it.묶음 = baseId;
                items.Add(it);
            }

            string yText = (f["amountYear"] ?? "").Trim();
            int year = yText.Length == 0 ? env.Today.Year : 연도(yText);
            string source = 글자(f["source"], "출처", 60, false);
            var amounts = new List<AmountRecord>();
            foreach (PaymentItem it in items)
            {
                string raw = (f["amt_" + it.월.ToString("00", Inv)] ?? "").Trim();
                if (raw.Length == 0 || !it.납부있음) continue;
                amounts.Add(금액기록(env, year, it.Id, 금액(raw, it.월 + "월 금액"), source));
            }

            env.Db.UpsertItems(items);
            if (amounts.Count > 0) env.Db.UpsertAmounts(amounts, 출처);

            var ids = new List<string>();
            foreach (PaymentItem it in items) ids.Add(it.Id);
            return new JObj().Set("ok", true).Set("group", baseId).Set("ids", ids)
                .Set("amounts", amounts.Count).Set("popup", 팝업확인(env, items));
        }

        static AmountRecord 금액기록(Env env, int year, string id, decimal amount, string source)
        {
            var rec = new AmountRecord();
            rec.연도 = year;
            rec.Id = id;
            rec.금액 = amount;
            rec.출처 = source.Length == 0 ? "웹 입력" : source;
            rec.확인일 = env.Today;
            return rec;
        }

        /// <summary>한 묶음에서 그 해에 기한이 있는 회차. 여러 해에 걸친 차입 이자 묶음도 있다 (ADR-0023).</summary>
        List<PaymentItem> 묶음항목(Env env, string group, int year)
        {
            string g = (group ?? "").Trim();
            if (g.Length == 0) throw new HttpError(400, "묶음 이름이 없습니다.");
            var all = env.Master.FindAll(delegate(PaymentItem x) { return x.묶음 == g; });
            if (all.Count == 0) throw new HttpError(404, "그런 분할납부 묶음이 없습니다: " + g);
            var list = all.FindAll(delegate(PaymentItem x) { return x.해당연도(year); });
            if (list.Count == 0) throw new HttpError(404, "이 묶음은 " + year + "년에 회차가 없습니다: " + g);
            list.Sort(delegate(PaymentItem a, PaymentItem b)
            {
                int c = a.월.CompareTo(b.월);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        /// <summary>한 묶음의 모든 회차를 한 표로 (AC-W33). 합계는 늘 같이 (AC-W35).</summary>
        JObj Group(Env env, int y, string group)
        {
            List<PaymentItem> items = 묶음항목(env, group, y);
            var rows = new List<object>();
            decimal total = 0;
            int entered = 0;
            foreach (PaymentItem it in items)
            {
                Occurrence o = OccurrenceOf(env, it, y);
                decimal? a = Amount(o);
                if (a.HasValue) total += a.Value;
                if (o.실제금액 != null) entered++;
                rows.Add(Dto(env, o, false));
            }
            return new JObj()
                .Set("year", y).Set("group", items[0].묶음)
                .Set("org", items[0].기관).Set("name", items[0].카드이름)
                .Set("rows", rows).Set("total", total).Set("entered", entered).Set("count", items.Count);
        }

        /// <summary>회차 금액을 한 번에 저장한다. 출처는 한 번만 (AC-W34). 빈 칸은 건드리지 않는다 (AC-W89).</summary>
        JObj SaveGroupAmounts(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            List<PaymentItem> items = 묶음항목(env, f["group"], y);
            string source = 글자(f["source"], "출처", 60, false);
            var records = new List<AmountRecord>();
            foreach (PaymentItem it in items)
            {
                string raw = (f["amt_" + it.Id] ?? "").Trim();
                if (raw.Length == 0 || !it.납부있음) continue;
                records.Add(금액기록(env, y, it.Id, 금액(raw, it.월 + "월 금액"), source));
            }
            if (records.Count == 0) throw new HttpError(400, "넣은 금액이 없습니다.");
            env.Db.UpsertAmounts(records, 출처);
            foreach (AmountRecord r in records) env.Amounts[r.Key] = r;
            return Group(env, y, items[0].묶음).Set("saved", records.Count);
        }

        // ══ 문서 열기·순서·기록 ═══════════════════════════════════ ADR-0011

        /// <summary>브라우저가 못 보여 주는 형식(xlsx·hwp)을 이 PC 의 기본 프로그램으로 연다.</summary>
        JObj OpenFile(Env env, NameValueCollection f)
        {
            int y = 연도(f["y"]);
            string id = 아이디(f["id"]);
            Attachment hit = null;
            foreach (Attachment a in env.Files.For(y, id))
                if (a.저장파일 == f["f"]) { hit = a; break; }
            if (hit == null) throw new HttpError(404, "증빙을 찾을 수 없습니다.");

            string root = Path.GetFullPath(env.Files.RootDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(env.Files.FullPath(hit));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new HttpError(403, "증빙 폴더 밖의 파일입니다.");
            if (!System.IO.File.Exists(full)) throw new HttpError(404, "증빙 파일이 지워졌습니다.");

            파일열기(full);
            return new JObj().Set("ok", true);
        }

        JObj MoveItem(Env env, NameValueCollection f)
        {
            PaymentItem it = 항목(env, f["id"]);
            // to = 목록 위치(0부터) — 끌어서 옮기기. 없으면 dir = up/down 한 칸.
            if (!string.IsNullOrEmpty(f["to"]))
            {
                int to;
                if (!int.TryParse(f["to"], NumberStyles.None, Inv, out to)) throw new HttpError(400, "to 는 0 이상의 숫자여야 합니다.");
                return new JObj().Set("ok", true).Set("moved", env.Db.MoveItemTo(it.Id, to));
            }
            string dir = f["dir"];
            if (dir != "up" && dir != "down") throw new HttpError(400, "dir 은 up 또는 down 이어야 합니다.");
            bool moved = env.Db.MoveItem(it.Id, dir == "up" ? -1 : 1);
            return new JObj().Set("ok", true).Set("moved", moved);
        }

        /// <summary>한 건의 변경 기록(y,id) 또는 기간의 전체 기록(from,to). 최근 것이 먼저.</summary>
        JObj Events(Env env, NameValueCollection q)
        {
            List<StatusEvent> list;
            if (!string.IsNullOrEmpty(q["id"]))
                list = env.Db.LoadEvents(연도(q["y"]), 아이디(q["id"]));
            else
                list = env.Db.LoadEvents(날짜(q["from"], env.Today.AddDays(-30)), 날짜(q["to"], env.Today), 500);

            var rows = new List<object>();
            foreach (StatusEvent e in list)
            {
                PaymentItem it = env.Item(e.Id);
                string[] stages = it != null ? Stages.For(it) : null;
                Func<int?, string> 이름 = delegate(int? s)
                {
                    if (!s.HasValue || stages == null || s.Value < 0 || s.Value >= stages.Length) return null;
                    return stages[s.Value];
                };
                rows.Add(env.기관사이트붙이기(new JObj()
                    .Set("at", e.시각.ToString("yyyy-MM-dd HH:mm:ss", Inv))
                    .Set("year", e.연도).Set("id", e.Id)
                    .Set("name", it != null ? it.카드이름 : (e.Id == "-" ? "설정" : e.Id))
                    .Set("org", it != null ? it.기관 : "")
                    .Set("action", e.동작).Set("source", e.출처).Set("detail", e.내용)
                    .Set("from", 이름(e.이전단계)).Set("to", 이름(e.이후단계)), it));
            }
            return new JObj().Set("rows", rows);
        }
    }
}
