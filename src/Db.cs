using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PaymentAlert
{
    /// <summary>
    /// Windows 에 내장된 SQLite(winsqlite3.dll)를 부른다.
    /// 별도 DLL 을 배포하지 않으려고 운영체제 부품을 쓴다. Windows 10 이후 기본 포함.
    /// </summary>
    static class Sqlite
    {
        const string L = "winsqlite3.dll";
        public const int OK = 0, ROW = 100, DONE = 101, NULL = 5;
        public static readonly IntPtr TRANSIENT = new IntPtr(-1);   // 버퍼를 SQLite 가 복사하게 한다

        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_step(IntPtr stmt);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_text(IntPtr stmt, int i, byte[] v, int n, IntPtr destructor);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_int64(IntPtr stmt, int i, long v);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_null(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_column_bytes(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern long sqlite3_column_int64(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_column_type(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(L, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_changes(IntPtr db);
    }

    /// <summary>쿼리 결과 한 행을 읽는다.</summary>
    sealed class Reader
    {
        readonly IntPtr st;
        public Reader(IntPtr st) { this.st = st; }

        public bool IsNull(int i) { return Sqlite.sqlite3_column_type(st, i) == Sqlite.NULL; }
        public long Long(int i) { return Sqlite.sqlite3_column_int64(st, i); }
        public int Int(int i) { return (int)Long(i); }

        public string Str(int i)
        {
            // text 를 먼저 부른 뒤 bytes 를 읽어야 길이가 맞다 (SQLite 문서 규칙).
            IntPtr p = Sqlite.sqlite3_column_text(st, i);
            if (p == IntPtr.Zero) return "";
            int n = Sqlite.sqlite3_column_bytes(st, i);
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public DateTime? Date(int i)
        {
            if (IsNull(i)) return null;
            DateTime d;
            if (DateTime.TryParse(Str(i), CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        public decimal? Dec(int i)
        {
            if (IsNull(i)) return null;
            decimal d;
            if (decimal.TryParse(Str(i), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }
    }

    /// <summary>SQLite 연결 하나. 한 스레드에서만 쓴다.</summary>
    sealed class Conn : IDisposable
    {
        IntPtr db;
        public delegate void RowHandler(Reader r);

        public Conn(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            int rc = Sqlite.sqlite3_open_v2(U(path), out db, 0x2 | 0x4, IntPtr.Zero);   // READWRITE | CREATE
            if (rc != Sqlite.OK)
            {
                string msg = db != IntPtr.Zero ? Err() : "코드 " + rc;
                if (db != IntPtr.Zero) Sqlite.sqlite3_close_v2(db);
                db = IntPtr.Zero;
                throw new IOException("자료 파일을 열지 못했습니다: " + path + " (" + msg + ")");
            }
            // 웹 서버와 팝업이 동시에 쓰면 곧바로 실패하지 않고 잠깐 기다렸다가 이어서 쓴다.
            Sqlite.sqlite3_busy_timeout(db, 5000);
        }

        static byte[] U(string s) { return Encoding.UTF8.GetBytes((s ?? "") + "\0"); }

        string Err()
        {
            IntPtr p = Sqlite.sqlite3_errmsg(db);
            if (p == IntPtr.Zero) return "";
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        IntPtr Prepare(string sql)
        {
            IntPtr st;
            if (Sqlite.sqlite3_prepare_v2(db, U(sql), -1, out st, IntPtr.Zero) != Sqlite.OK)
                throw new InvalidOperationException("SQL 준비 실패: " + Err() + " — " + sql);
            return st;
        }

        static void Bind(IntPtr st, object[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                object v = args[i];
                int idx = i + 1;
                if (v == null) Sqlite.sqlite3_bind_null(st, idx);
                else if (v is string) { byte[] b = Encoding.UTF8.GetBytes((string)v); Sqlite.sqlite3_bind_text(st, idx, b, b.Length, Sqlite.TRANSIENT); }
                else if (v is int) Sqlite.sqlite3_bind_int64(st, idx, (int)v);
                else if (v is long) Sqlite.sqlite3_bind_int64(st, idx, (long)v);
                else if (v is bool) Sqlite.sqlite3_bind_int64(st, idx, (bool)v ? 1 : 0);
                else throw new ArgumentException("지원하지 않는 값 형식: " + v.GetType().Name);
            }
        }

        /// <summary>쓰기 문장을 실행하고 바뀐 행 수를 돌려준다.</summary>
        public int Run(string sql, params object[] args)
        {
            IntPtr st = Prepare(sql);
            try
            {
                Bind(st, args);
                int rc = Sqlite.sqlite3_step(st);
                if (rc != Sqlite.DONE && rc != Sqlite.ROW)
                    throw new InvalidOperationException("SQL 실행 실패: " + Err() + " — " + sql);
                return Sqlite.sqlite3_changes(db);
            }
            finally { Sqlite.sqlite3_finalize(st); }
        }

        public void Each(string sql, RowHandler h, params object[] args)
        {
            IntPtr st = Prepare(sql);
            try
            {
                Bind(st, args);
                var r = new Reader(st);
                int rc;
                while ((rc = Sqlite.sqlite3_step(st)) == Sqlite.ROW) h(r);
                if (rc != Sqlite.DONE)
                    throw new InvalidOperationException("SQL 조회 실패: " + Err() + " — " + sql);
            }
            finally { Sqlite.sqlite3_finalize(st); }
        }

        /// <summary>
        /// 한 트랜잭션으로 묶는다. IMMEDIATE 로 시작해 쓰기 잠금을 먼저 잡으므로,
        /// 두 프로세스가 같은 행을 읽고 고쳐 쓰다 한쪽 변경이 사라지는 일이 없다.
        /// </summary>
        public void Tx(Action body)
        {
            Run("BEGIN IMMEDIATE");
            try { body(); Run("COMMIT"); }
            catch
            {
                try { Run("ROLLBACK"); } catch { }
                throw;
            }
        }

        public void Dispose()
        {
            if (db != IntPtr.Zero) { Sqlite.sqlite3_close_v2(db); db = IntPtr.Zero; }
        }
    }

    /// <summary>
    /// 납부 알림의 자료 저장소. 웹사이트가 주인이고 팝업·보드는 같은 파일을 읽고 쓴다.
    /// Repository 와 같은 모양의 연산을 제공해, 기존 흐름 코드를 거의 그대로 둔다.
    /// </summary>
    public sealed class Store : IDisposable
    {
        public const string 파일이름 = "납부알림.db";
        const int 스키마버전 = 1;

        readonly Conn c;
        public string 파일경로 { get; private set; }

        static readonly string[] 스키마 = {
            "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT)",
            "CREATE TABLE IF NOT EXISTS items(" +
                "id TEXT PRIMARY KEY, 기관 TEXT NOT NULL, 비용명 TEXT NOT NULL, " +
                "진행흐름 TEXT NOT NULL CHECK(진행흐름 IN ('신고납부','납부만','제출만')), " +
                "월 INTEGER NOT NULL CHECK(월 BETWEEN 1 AND 12), " +
                "말일 INTEGER NOT NULL DEFAULT 0, 일 INTEGER NOT NULL DEFAULT 0, " +
                "알림영업일 INTEGER NOT NULL DEFAULT 3, 금액규칙 TEXT NOT NULL DEFAULT '', " +
                "고정금액 TEXT, 비고 TEXT NOT NULL DEFAULT '', 순서 INTEGER NOT NULL DEFAULT 0)",
            // 금액은 TEXT 로 둔다. 실수로 담으면 원 단위가 어긋날 수 있다.
            "CREATE TABLE IF NOT EXISTS amounts(" +
                "연도 INTEGER NOT NULL, id TEXT NOT NULL, 금액 TEXT NOT NULL, 출처 TEXT NOT NULL DEFAULT '', " +
                "확인일 TEXT, 비고 TEXT NOT NULL DEFAULT '', PRIMARY KEY(연도, id))",
            "CREATE TABLE IF NOT EXISTS status(" +
                "연도 INTEGER NOT NULL, id TEXT NOT NULL, 단계 INTEGER NOT NULL DEFAULT 0, " +
                "변경일시 TEXT, 최종확인일 TEXT, 메모 TEXT NOT NULL DEFAULT '', PRIMARY KEY(연도, id))",
            "CREATE TABLE IF NOT EXISTS attachments(" +
                "rid INTEGER PRIMARY KEY AUTOINCREMENT, 연도 INTEGER NOT NULL, id TEXT NOT NULL, " +
                "단계 TEXT NOT NULL DEFAULT '', 저장파일 TEXT NOT NULL, 원본파일명 TEXT NOT NULL DEFAULT '', 첨부일시 TEXT)",
            "CREATE TABLE IF NOT EXISTS holidays(날짜 TEXT PRIMARY KEY, 명칭 TEXT NOT NULL DEFAULT '')"
        };

        Store(string path)
        {
            파일경로 = path;
            c = new Conn(path);
            // WAL: 웹 서버가 읽는 동안 팝업이 써도 서로 막지 않는다.
            c.Run("PRAGMA journal_mode=WAL");
            c.Run("PRAGMA synchronous=NORMAL");
            foreach (string sql in 스키마) c.Run(sql);
            if (GetMeta("schema_version") == null)
                SetMeta("schema_version", 스키마버전.ToString(CultureInfo.InvariantCulture));
        }

        public static Store Open(string dbPath) { return new Store(dbPath); }

        public void Dispose() { c.Dispose(); }

        // ── 형식 변환 ── 날짜·금액은 문화권과 무관한 고정 형식으로 저장한다.
        static string D(DateTime? v) { return v.HasValue ? v.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null; }
        static string DT(DateTime? v) { return v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null; }
        static string M(decimal? v) { return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : null; }

        // ── meta ──
        public string GetMeta(string key)
        {
            string v = null;
            c.Each("SELECT value FROM meta WHERE key=?", delegate(Reader r) { v = r.IsNull(0) ? null : r.Str(0); }, key);
            return v;
        }

        public void SetMeta(string key, string value)
        {
            if (value == null) c.Run("DELETE FROM meta WHERE key=?", key);
            else c.Run("INSERT INTO meta(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value);
        }

        // ── 납부 항목 ──
        public List<PaymentItem> LoadMaster()
        {
            var list = new List<PaymentItem>();
            c.Each("SELECT id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고 FROM items ORDER BY 순서, id",
                delegate(Reader r)
                {
                    var it = new PaymentItem();
                    it.Id = r.Str(0);
                    it.기관 = r.Str(1);
                    it.비용명 = r.Str(2);
                    it.진행흐름 = Stages.Parse(r.Str(3));
                    it.월 = r.Int(4);
                    it.말일 = r.Long(5) != 0;
                    it.일 = r.Int(6);
                    it.알림영업일 = r.Int(7);
                    it.금액규칙 = r.Str(8);
                    it.고정금액 = r.Dec(9);
                    it.비고 = r.Str(10);
                    list.Add(it);
                });
            return list;
        }

        /// <summary>항목 전체를 바꾼다. 진행 상태·금액은 id 로 이어지므로 건드리지 않는다.</summary>
        public void ReplaceMaster(List<PaymentItem> items)
        {
            c.Tx(delegate
            {
                c.Run("DELETE FROM items");
                int n = 0;
                foreach (PaymentItem it in items)
                {
                    c.Run("INSERT INTO items(id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고,순서) " +
                          "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                        it.Id, it.기관 ?? "", it.비용명 ?? "", it.진행흐름.ToString(), it.월, it.말일,
                        it.말일 ? 0 : it.일, it.알림영업일, it.금액규칙 ?? "", M(it.고정금액), it.비고 ?? "", n);
                    n++;
                }
                SetMeta("master_updated_at", DT(DateTime.Now));
            });
        }

        // ── 웹 편집 ── 항목·금액을 한 건씩 다룬다. 엑셀 가져오기는 위의 ReplaceMaster 를 쓴다.

        /// <summary>
        /// 항목 하나를 넣거나 고친다. 새 항목은 목록 맨 뒤에 붙고, 기존 항목은 제자리를 지킨다.
        /// 값의 유효성은 부르는 쪽에서 먼저 본다. DB 는 진행흐름·월만 스스로 막는다.
        /// </summary>
        public void UpsertItem(PaymentItem it)
        {
            c.Tx(delegate
            {
                long 다음순서 = 0;
                c.Each("SELECT COALESCE(MAX(순서), -1) + 1 FROM items", delegate(Reader r) { 다음순서 = r.Long(0); });
                c.Run("INSERT INTO items(id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고,순서) " +
                      "VALUES(?,?,?,?,?,?,?,?,?,?,?,?) " +
                      "ON CONFLICT(id) DO UPDATE SET 기관=excluded.기관, 비용명=excluded.비용명, " +
                      "진행흐름=excluded.진행흐름, 월=excluded.월, 말일=excluded.말일, 일=excluded.일, " +
                      "알림영업일=excluded.알림영업일, 금액규칙=excluded.금액규칙, 고정금액=excluded.고정금액, 비고=excluded.비고",
                    it.Id, it.기관 ?? "", it.비용명 ?? "", it.진행흐름.ToString(), it.월, it.말일,
                    it.말일 ? 0 : it.일, it.알림영업일, it.금액규칙 ?? "", M(it.고정금액), it.비고 ?? "", 다음순서);
                SetMeta("master_updated_at", DT(DateTime.Now));
            });
        }

        /// <summary>
        /// 항목을 지운다. 진행 기록·금액·증빙은 남긴다 —
        /// 실수로 지웠다가 같은 id 로 다시 넣으면 기록이 그대로 이어져야 한다.
        /// </summary>
        public void DeleteItem(string id)
        {
            c.Tx(delegate
            {
                c.Run("DELETE FROM items WHERE id=?", id);
                SetMeta("master_updated_at", DT(DateTime.Now));
            });
        }

        /// <summary>그 해 금액 한 건을 지운다. 잘못 입력한 금액을 되돌릴 때 쓴다.</summary>
        public void DeleteAmount(int 연도, string id)
        {
            c.Run("DELETE FROM amounts WHERE 연도=? AND id=?", 연도, id);
        }

        // ── 추적 시작일 ──
        public DateTime? LoadStartDate()
        {
            string v = GetMeta("start_date");
            DateTime d;
            if (!string.IsNullOrEmpty(v) &&
                DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d.Date;
            return null;
        }

        public void SetStartDate(DateTime? d) { SetMeta("start_date", D(d)); }

        // ── 연도별 금액 ──
        public Dictionary<string, AmountRecord> LoadAmounts()
        {
            var map = new Dictionary<string, AmountRecord>(StringComparer.Ordinal);
            c.Each("SELECT 연도,id,금액,출처,확인일,비고 FROM amounts", delegate(Reader r)
            {
                decimal? amt = r.Dec(2);
                if (!amt.HasValue) return;
                var a = new AmountRecord();
                a.연도 = r.Int(0);
                a.Id = r.Str(1);
                a.금액 = amt.Value;
                a.출처 = r.Str(3);
                a.확인일 = r.Date(4);
                a.비고 = r.Str(5);
                map[a.Key] = a;
            });
            return map;
        }

        /// <summary>
        /// 넣거나 고친다. 목록에 없는 행은 지우지 않는다 —
        /// 엑셀에서 가져올 때 웹에서 따로 입력한 금액이 사라지면 안 되기 때문이다.
        /// </summary>
        public void UpsertAmounts(IEnumerable<AmountRecord> records)
        {
            c.Tx(delegate
            {
                foreach (AmountRecord a in records)
                    c.Run("INSERT INTO amounts(연도,id,금액,출처,확인일,비고) VALUES(?,?,?,?,?,?) " +
                          "ON CONFLICT(연도,id) DO UPDATE SET 금액=excluded.금액, 출처=excluded.출처, " +
                          "확인일=excluded.확인일, 비고=excluded.비고",
                        a.연도, a.Id, M(a.금액), a.출처 ?? "", D(a.확인일), a.비고 ?? "");
            });
        }

        // ── 진행 상태 ──
        public Dictionary<string, StatusRecord> LoadStatus()
        {
            var map = new Dictionary<string, StatusRecord>(StringComparer.Ordinal);
            c.Each("SELECT 연도,id,단계,변경일시,최종확인일,메모 FROM status", delegate(Reader r)
            {
                var st = new StatusRecord();
                st.연도 = r.Int(0);
                st.Id = r.Str(1);
                st.단계 = r.Int(2);
                st.변경일시 = r.Date(3);
                st.최종확인일 = r.Date(4);
                st.메모 = r.Str(5);
                map[st.Key] = st;
            });
            return map;
        }

        /// <summary>
        /// 이번에 사용자가 실제로 바꾼 기록(변경됨)만 쓴다. 나머지 행은 건드리지 않는다.
        /// 한 트랜잭션으로 묶어 팝업·보드·웹이 동시에 써도 서로의 변경을 지우지 않는다.
        /// </summary>
        public void SaveStatus(IEnumerable<StatusRecord> records)
        {
            c.Tx(delegate
            {
                foreach (StatusRecord st in records)
                {
                    if (!st.변경됨) continue;
                    c.Run("INSERT INTO status(연도,id,단계,변경일시,최종확인일,메모) VALUES(?,?,?,?,?,?) " +
                          "ON CONFLICT(연도,id) DO UPDATE SET 단계=excluded.단계, 변경일시=excluded.변경일시, " +
                          "최종확인일=excluded.최종확인일, 메모=excluded.메모",
                        st.연도, st.Id, st.단계, DT(st.변경일시), D(st.최종확인일), st.메모 ?? "");
                }
            });
        }

        // ── 공휴일 ──
        public Holidays.Cache LoadHolidays()
        {
            var cache = new Holidays.Cache();
            c.Each("SELECT 날짜, 명칭 FROM holidays", delegate(Reader r)
            {
                DateTime d;
                if (DateTime.TryParseExact(r.Str(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                {
                    cache.Dates[d.Date] = r.Str(1);
                    cache.Years.Add(d.Year);
                }
            });

            string years = GetMeta("holidays.years");
            if (!string.IsNullOrEmpty(years))
                foreach (string y in years.Split(','))
                {
                    int n;
                    if (int.TryParse(y.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) cache.Years.Add(n);
                }

            string upd = GetMeta("holidays.updated");
            DateTime u;
            if (!string.IsNullOrEmpty(upd) &&
                DateTime.TryParse(upd, CultureInfo.InvariantCulture, DateTimeStyles.None, out u))
                cache.Updated = u;
            return cache;
        }

        /// <summary>갱신 시각은 캐시의 값을 그대로 둔다. 옮겨 담을 때 갱신 주기가 초기화되면 안 된다.</summary>
        public void SaveHolidays(Holidays.Cache cache)
        {
            c.Tx(delegate
            {
                c.Run("DELETE FROM holidays");
                foreach (KeyValuePair<DateTime, string> kv in cache.Dates)
                    c.Run("INSERT INTO holidays(날짜,명칭) VALUES(?,?)", D(kv.Key), kv.Value ?? "");

                var ys = new List<int>(cache.Years);
                ys.Sort();
                var parts = new List<string>();
                foreach (int y in ys) parts.Add(y.ToString(CultureInfo.InvariantCulture));
                SetMeta("holidays.years", string.Join(",", parts.ToArray()));
                SetMeta("holidays.updated", D(cache.Updated));
            });
        }

        // ── 증빙 목록 ──
        public List<Attachment> LoadAttachments()
        {
            var list = new List<Attachment>();
            c.Each("SELECT 연도,id,단계,저장파일,원본파일명,첨부일시 FROM attachments ORDER BY 연도, id, 첨부일시, rid",
                delegate(Reader r)
                {
                    var a = new Attachment();
                    a.연도 = r.Int(0);
                    a.Id = r.Str(1);
                    a.단계 = r.Str(2);
                    a.저장파일 = r.Str(3);
                    a.원본파일명 = r.Str(4);
                    DateTime? d = r.Date(5);
                    a.첨부일시 = d.HasValue ? d.Value : DateTime.MinValue;
                    list.Add(a);
                });
            return list;
        }

        public void AddAttachment(Attachment a)
        {
            c.Run("INSERT INTO attachments(연도,id,단계,저장파일,원본파일명,첨부일시) VALUES(?,?,?,?,?,?)",
                a.연도, a.Id, a.단계 ?? "", a.저장파일, a.원본파일명 ?? "",
                a.첨부일시 == DateTime.MinValue ? null : DT(a.첨부일시));
        }

        /// <summary>저장파일은 파일마다 고유하므로 (연도, id, 저장파일) 로 한 건을 가린다.</summary>
        public void RemoveAttachment(Attachment a)
        {
            c.Run("DELETE FROM attachments WHERE 연도=? AND id=? AND 저장파일=?", a.연도, a.Id, a.저장파일);
        }

        // ── 관리 ──

        /// <summary>
        /// 쓰는 도중에도 온전한 사본을 만든다.
        /// WAL 방식이라 .db 파일만 복사하면 아직 합쳐지지 않은 변경이 빠질 수 있다.
        /// </summary>
        public void Backup(string destPath)
        {
            if (File.Exists(destPath)) File.Delete(destPath);
            c.Run("VACUUM INTO ?", destPath);
        }

        /// <summary>WAL 파일을 본 파일에 합치고 단일 파일 방식으로 바꾼다. 파일을 옮기기 전에 쓴다.</summary>
        public void 단일파일로()
        {
            c.Run("PRAGMA journal_mode=DELETE");
        }
    }
}
