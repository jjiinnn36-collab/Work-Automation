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
    /// 이미 다른 창에서 단계가 바뀌어, 사용자가 보고 누른 단계와 DB 의 단계가 다를 때 난다.
    /// 옛 화면을 믿고 그대로 쓰면 한 번 누른 것이 두 번 진행되거나 되돌린 것이 사라진다.
    /// </summary>
    public sealed class StageConflictException : Exception
    {
        public readonly int 현재단계;
        public StageConflictException(string message, int 현재단계) : base(message) { this.현재단계 = 현재단계; }
    }

    /// <summary>
    /// 납부 알림의 자료 저장소. 웹사이트가 주인이고 팝업·보드는 같은 파일을 읽고 쓴다.
    /// Repository 와 같은 모양의 연산을 제공해, 기존 흐름 코드를 거의 그대로 둔다.
    /// </summary>
    public sealed class Store : IDisposable
    {
        public const string 파일이름 = "납부알림.db";
        public const int 스키마버전 = 3;

        readonly Conn c;
        public string 파일경로 { get; private set; }

        // 1판 표. 새 파일도 이 표를 먼저 만들고 아래 이관 단계를 차례로 밟는다.
        // 그래야 새로 만든 파일과 옛 파일을 이관한 결과가 똑같다.
        static readonly string[] 스키마1 = {
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
            try
            {
                // WAL: 웹 서버가 읽는 동안 팝업이 써도 서로 막지 않는다.
                c.Run("PRAGMA journal_mode=WAL");
                c.Run("PRAGMA synchronous=NORMAL");
                foreach (string sql in 스키마1) c.Run(sql);
                if (GetMeta("schema_version") == null) SetMeta("schema_version", "1");
                이관();
            }
            catch
            {
                c.Dispose();
                throw;
            }
        }

        public static Store Open(string dbPath) { return new Store(dbPath); }

        public void Dispose() { c.Dispose(); }

        public int 버전
        {
            get
            {
                int v;
                return int.TryParse(GetMeta("schema_version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
            }
        }

        // ── 스키마 이관 ── ADR-0006

        void 이관()
        {
            int v = 버전;
            if (v > 스키마버전)
                throw new InvalidOperationException(string.Format(
                    "자료 파일이 이 프로그램보다 새 버전입니다 (파일 {0}판, 프로그램 {1}판). 프로그램을 새로 빌드하세요.", v, 스키마버전));
            if (v >= 스키마버전) return;

            // 자료가 있는 파일이면 바꾸기 전에 사본을 남긴다. 이관이 틀려도 되돌릴 수 있게.
            long 항목수 = 0;
            c.Each("SELECT COUNT(*) FROM items", delegate(Reader r) { 항목수 = r.Long(0); });
            if (항목수 > 0 && 파일경로.IndexOf(".importing", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(파일경로)), "backups");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Backup(Path.Combine(dir, string.Format("납부알림-v{0}-이관전-{1}.db", v,
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))));
            }

            if (v < 2) c.Tx(이관2);
            if (v < 3) c.Tx(이관3);
        }

        /// <summary>
        /// 2판 → 3판. 진행흐름에 '사용자설정' 을 허용하고 사용자 단계 정의·금액 없음 칸을 더한다 (ADR-0015).
        /// SQLite 는 CHECK 를 고칠 수 없어 items 표를 새로 만들어 옮긴다. 한 트랜잭션이라 중간에 끊겨도 옛 표가 남는다.
        /// </summary>
        void 이관3()
        {
            c.Run("CREATE TABLE items_v3(" +
                  "id TEXT PRIMARY KEY, 기관 TEXT NOT NULL, 비용명 TEXT NOT NULL, " +
                  "진행흐름 TEXT NOT NULL CHECK(진행흐름 IN ('신고납부','납부만','제출만','사용자설정')), " +
                  "월 INTEGER NOT NULL CHECK(월 BETWEEN 1 AND 12), " +
                  "말일 INTEGER NOT NULL DEFAULT 0, 일 INTEGER NOT NULL DEFAULT 0, " +
                  "알림영업일 INTEGER NOT NULL DEFAULT 3, 금액규칙 TEXT NOT NULL DEFAULT '', " +
                  "고정금액 TEXT, 비고 TEXT NOT NULL DEFAULT '', 순서 INTEGER NOT NULL DEFAULT 0, " +
                  "홈페이지명 TEXT NOT NULL DEFAULT '', 홈페이지주소 TEXT NOT NULL DEFAULT '', 묶음 TEXT NOT NULL DEFAULT '', " +
                  "단계정의 TEXT NOT NULL DEFAULT '', 금액없음 INTEGER NOT NULL DEFAULT 0)");
            const string 옛칸 = "id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고,순서,홈페이지명,홈페이지주소,묶음";
            c.Run("INSERT INTO items_v3(" + 옛칸 + ") SELECT " + 옛칸 + " FROM items");
            c.Run("DROP TABLE items");
            c.Run("ALTER TABLE items_v3 RENAME TO items");
            SetMeta("schema_version", "3");
        }

        /// <summary>
        /// 1판 → 2판. 홈페이지·분할 묶음·문서 종류 칸, 변경 기록 표를 더하고
        /// 금액규칙을 고정/변동으로, 옛 단계 이름을 새 이름으로 바꾼다.
        /// </summary>
        void 이관2()
        {
            c.Run("ALTER TABLE items ADD COLUMN 홈페이지명 TEXT NOT NULL DEFAULT ''");
            c.Run("ALTER TABLE items ADD COLUMN 홈페이지주소 TEXT NOT NULL DEFAULT ''");
            c.Run("ALTER TABLE items ADD COLUMN 묶음 TEXT NOT NULL DEFAULT ''");
            c.Run("ALTER TABLE attachments ADD COLUMN 종류 TEXT NOT NULL DEFAULT '증빙'");
            c.Run("CREATE TABLE IF NOT EXISTS events(" +
                  "rid INTEGER PRIMARY KEY AUTOINCREMENT, 시각 TEXT NOT NULL, 연도 INTEGER NOT NULL, id TEXT NOT NULL, " +
                  "동작 TEXT NOT NULL, 이전단계 INTEGER, 이후단계 INTEGER, 출처 TEXT NOT NULL DEFAULT '', 내용 TEXT NOT NULL DEFAULT '')");
            c.Run("CREATE INDEX IF NOT EXISTS events_key ON events(연도, id)");
            c.Run("CREATE INDEX IF NOT EXISTS events_time ON events(시각)");

            // 고정금액이 적혀 있던 건은 고정으로 본다 — 적어 둔 금액을 버리지 않는다.
            c.Run("UPDATE items SET 금액규칙 = CASE WHEN 금액규칙='고정' OR 고정금액 IS NOT NULL THEN '고정' ELSE '변동' END");

            foreach (KeyValuePair<string, string> kv in Stages.옛이름)
                c.Run("UPDATE attachments SET 단계=? WHERE 단계=?", kv.Value, kv.Key);

            묶음찾기();
            SetMeta("schema_version", "2");
        }

        /// <summary>
        /// 이름이 '접두-두자리월' 이고 기관·비용명이 같은 항목이 둘 이상이면 한 묶음이다.
        /// 기존 kofia-05 … kofia-10 같은 분할납부 회차가 여기에 걸린다.
        /// </summary>
        void 묶음찾기()
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var prefixOf = new Dictionary<string, string>(StringComparer.Ordinal);
            c.Each("SELECT id, 기관, 비용명 FROM items", delegate(Reader r)
            {
                string id = r.Str(0);
                int dash = id.LastIndexOf('-');
                if (dash <= 0 || dash != id.Length - 3) return;
                string mm = id.Substring(dash + 1);
                if (!char.IsDigit(mm[0]) || !char.IsDigit(mm[1])) return;
                string prefix = id.Substring(0, dash);
                string key = prefix + "\t" + r.Str(1) + "\t" + r.Str(2);
                List<string> list;
                if (!groups.TryGetValue(key, out list)) { list = new List<string>(); groups[key] = list; }
                list.Add(id);
                prefixOf[key] = prefix;
            });
            foreach (KeyValuePair<string, List<string>> kv in groups)
            {
                if (kv.Value.Count < 2) continue;
                foreach (string id in kv.Value)
                    c.Run("UPDATE items SET 묶음=? WHERE id=?", prefixOf[kv.Key], id);
            }
        }

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
        const string 항목칸 = "id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고,홈페이지명,홈페이지주소,묶음,단계정의,금액없음";
        const string 항목자리 = "?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?";

        public List<PaymentItem> LoadMaster()
        {
            var list = new List<PaymentItem>();
            c.Each("SELECT " + 항목칸 + " FROM items ORDER BY 순서, id",
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
                    it.홈페이지명 = r.Str(11);
                    it.홈페이지주소 = r.Str(12);
                    it.묶음 = r.Str(13);
                    if (it.진행흐름 == Flow.사용자설정)
                    {
                        Stages.단계정의읽기(it, r.Str(14));
                        it.금액없음 = r.Long(15) != 0;
                    }
                    list.Add(it);
                });
            return list;
        }

        object[] 항목값(PaymentItem it, long 순서)
        {
            string rule = AmountRules.Normalize(it.금액규칙, it.고정금액);
            // 변동이면 마스터 금액을 두지 않는다 (AC-W26a). 납부 없는 흐름도 마찬가지 (AC-W27).
            decimal? fixedAmount = rule == AmountRules.고정 && it.납부있음 ? it.고정금액 : null;
            return new object[] {
                it.Id, it.기관 ?? "", it.비용명 ?? "", it.진행흐름.ToString(), it.월, it.말일,
                it.말일 ? 0 : it.일, it.알림영업일, rule, M(fixedAmount), it.비고 ?? "",
                it.홈페이지명 ?? "", it.홈페이지주소 ?? "", it.묶음 ?? "",
                Stages.단계정의(it), it.진행흐름 == Flow.사용자설정 && it.금액없음 ? 1 : 0, 순서 };
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
                    c.Run("INSERT INTO items(" + 항목칸 + ",순서) VALUES(" + 항목자리 + ")", 항목값(it, n));
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
            UpsertItems(new PaymentItem[] { it });
        }

        /// <summary>여러 항목을 한 트랜잭션으로. 분할납부 회차를 한꺼번에 만들 때 쓴다 — 반만 생기면 안 된다.</summary>
        public void UpsertItems(IEnumerable<PaymentItem> items)
        {
            c.Tx(delegate
            {
                foreach (PaymentItem it in items)
                {
                    long 다음순서 = 0;
                    c.Each("SELECT COALESCE(MAX(순서), -1) + 1 FROM items", delegate(Reader r) { 다음순서 = r.Long(0); });
                    c.Run("INSERT INTO items(" + 항목칸 + ",순서) VALUES(" + 항목자리 + ") " +
                          "ON CONFLICT(id) DO UPDATE SET 기관=excluded.기관, 비용명=excluded.비용명, " +
                          "진행흐름=excluded.진행흐름, 월=excluded.월, 말일=excluded.말일, 일=excluded.일, " +
                          "알림영업일=excluded.알림영업일, 금액규칙=excluded.금액규칙, 고정금액=excluded.고정금액, 비고=excluded.비고, " +
                          "홈페이지명=excluded.홈페이지명, 홈페이지주소=excluded.홈페이지주소, 묶음=excluded.묶음, " +
                          "단계정의=excluded.단계정의, 금액없음=excluded.금액없음",
                        항목값(it, 다음순서));
                }
                SetMeta("master_updated_at", DT(DateTime.Now));
            });
        }

        public const string 아이디접두 = "item-";

        /// <summary>
        /// 새 항목 id 를 만든다: item-0001, item-0002 …  (ADR-0015)
        /// 번호는 meta 에 적어 두어 지운 항목의 번호를 다시 쓰지 않는다 — 같은 id 면 옛 기록·금액·증빙이 붙기 때문이다.
        /// 옛 기록이나 분할 회차(item-0003-05)가 이미 쓰는 번호도 건너뛴다.
        /// </summary>
        public string 새항목아이디()
        {
            string result = null;
            c.Tx(delegate
            {
                long n;
                if (!long.TryParse(GetMeta("next_item_no"), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1) n = 1;
                while (true)
                {
                    string id = 아이디접두 + n.ToString("0000", CultureInfo.InvariantCulture);
                    bool used = false;
                    foreach (string table in new string[] { "items", "status", "amounts", "attachments", "events" })
                        c.Each("SELECT 1 FROM " + table + " WHERE id=? OR id LIKE ? LIMIT 1",
                            delegate(Reader r) { used = true; }, id, id + "-%");
                    n++;
                    if (!used) { result = id; break; }
                }
                SetMeta("next_item_no", n.ToString(CultureInfo.InvariantCulture));
            });
            return result;
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

        /// <summary>순서를 한 칸 옮긴다. -1 = 위로, +1 = 아래로. 끝에 닿으면 아무 일도 없다.</summary>
        public bool MoveItem(string id, int 방향)
        {
            bool moved = false;
            c.Tx(delegate
            {
                var ids = new List<string>();
                c.Each("SELECT id FROM items ORDER BY 순서, id", delegate(Reader r) { ids.Add(r.Str(0)); });
                int i = ids.IndexOf(id);
                int j = i + (방향 < 0 ? -1 : 1);
                if (i < 0 || j < 0 || j >= ids.Count) return;
                string t = ids[i]; ids[i] = ids[j]; ids[j] = t;
                for (int n = 0; n < ids.Count; n++) c.Run("UPDATE items SET 순서=? WHERE id=?", n, ids[n]);
                moved = true;
            });
            return moved;
        }

        /// <summary>그 해 금액 한 건을 지운다. 잘못 입력한 금액을 되돌릴 때 쓴다.</summary>
        public void DeleteAmount(int 연도, string id)
        {
            DeleteAmount(연도, id, null);
        }

        public void DeleteAmount(int 연도, string id, string 출처)
        {
            c.Tx(delegate
            {
                string before = null;
                c.Each("SELECT 금액 FROM amounts WHERE 연도=? AND id=?", delegate(Reader r) { before = r.Str(0); }, 연도, id);
                if (c.Run("DELETE FROM amounts WHERE 연도=? AND id=?", 연도, id) > 0 && 출처 != null)
                    기록(연도, id, "금액삭제", null, null, 출처, "지운 금액 " + before);
            });
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
            UpsertAmounts(records, null);
        }

        /// <summary>출처(팝업·웹)를 주면 변경 기록에도 남긴다.</summary>
        public void UpsertAmounts(IEnumerable<AmountRecord> records, string 출처)
        {
            c.Tx(delegate
            {
                foreach (AmountRecord a in records)
                {
                    c.Run("INSERT INTO amounts(연도,id,금액,출처,확인일,비고) VALUES(?,?,?,?,?,?) " +
                          "ON CONFLICT(연도,id) DO UPDATE SET 금액=excluded.금액, 출처=excluded.출처, " +
                          "확인일=excluded.확인일, 비고=excluded.비고",
                        a.연도, a.Id, M(a.금액), a.출처 ?? "", D(a.확인일), a.비고 ?? "");
                    if (출처 != null)
                        기록(a.연도, a.Id, "금액", null, null, 출처,
                            a.금액.ToString("N0", CultureInfo.InvariantCulture) + "원" + (string.IsNullOrEmpty(a.출처) ? "" : " · " + a.출처));
                }
            });
        }

        // ── 진행 상태 ──
        public Dictionary<string, StatusRecord> LoadStatus()
        {
            var map = new Dictionary<string, StatusRecord>(StringComparer.Ordinal);
            c.Each("SELECT 연도,id,단계,변경일시,최종확인일,메모 FROM status", delegate(Reader r)
            {
                var st = 상태읽기(r);
                map[st.Key] = st;
            });
            return map;
        }

        static StatusRecord 상태읽기(Reader r)
        {
            var st = new StatusRecord();
            st.연도 = r.Int(0);
            st.Id = r.Str(1);
            st.단계 = r.Int(2);
            st.변경일시 = r.Date(3);
            st.최종확인일 = r.Date(4);
            st.메모 = r.Str(5);
            return st;
        }

        /// <summary>한 건의 지금 상태. 기록이 없으면 단계 0 으로 만들어 돌려준다.</summary>
        public StatusRecord LoadStatus(int 연도, string id)
        {
            StatusRecord found = null;
            c.Each("SELECT 연도,id,단계,변경일시,최종확인일,메모 FROM status WHERE 연도=? AND id=?",
                delegate(Reader r) { found = 상태읽기(r); }, 연도, id);
            if (found != null) return found;
            var st = new StatusRecord();
            st.연도 = 연도;
            st.Id = id;
            return st;
        }

        /// <summary>
        /// 이번에 사용자가 실제로 바꾼 기록(변경됨)만 쓴다. 나머지 행은 건드리지 않는다.
        /// 한 트랜잭션으로 묶어 팝업·보드·웹이 동시에 써도 서로의 변경을 지우지 않는다.
        /// 옛 TSV 를 옮길 때 쓴다. 화면의 단계 변경은 아래 Advance/Defer/Revert 를 쓴다.
        /// </summary>
        public void SaveStatus(IEnumerable<StatusRecord> records)
        {
            c.Tx(delegate
            {
                foreach (StatusRecord st in records)
                {
                    if (!st.변경됨) continue;
                    상태쓰기(st);
                }
            });
        }

        void 상태쓰기(StatusRecord st)
        {
            c.Run("INSERT INTO status(연도,id,단계,변경일시,최종확인일,메모) VALUES(?,?,?,?,?,?) " +
                  "ON CONFLICT(연도,id) DO UPDATE SET 단계=excluded.단계, 변경일시=excluded.변경일시, " +
                  "최종확인일=excluded.최종확인일, 메모=excluded.메모",
                st.연도, st.Id, st.단계, DT(st.변경일시), D(st.최종확인일), st.메모 ?? "");
        }

        // ── 단계 변경 ── ADR-0004
        // 읽기·확인·쓰기·기록을 한 쓰기 트랜잭션 안에서 한다. 두 창이 같은 건을 동시에 눌러도
        // 한쪽만 통과하고 다른 쪽은 StageConflictException 을 받는다.
        // 기대단계에 -1 을 주면 확인하지 않는다 (옛 호출용).

        /// <summary>다음 지점으로 한 칸. 오늘 확인한 것으로도 표시한다.</summary>
        public StatusRecord Advance(int 연도, string id, Flow flow, int 기대단계, DateTime 지금, DateTime 오늘, string 출처)
        {
            return Advance(연도, id, Stages.For(flow), 기대단계, 지금, 오늘, 출처);
        }

        /// <summary>항목의 단계 목록(사용자설정 포함)으로 한 칸 나아간다.</summary>
        public StatusRecord Advance(int 연도, string id, string[] 단계들, int 기대단계, DateTime 지금, DateTime 오늘, string 출처)
        {
            StatusRecord result = null;
            c.Tx(delegate
            {
                StatusRecord st = LoadStatus(연도, id);
                확인(st, 기대단계);
                int last = 단계들.Length - 1;
                if (st.단계 >= last)
                    throw new StageConflictException("이미 마지막 단계까지 끝난 건입니다.", st.단계);
                int before = st.단계;
                st.단계 = before + 1;
                st.변경일시 = 지금;
                st.최종확인일 = 오늘.Date;
                상태쓰기(st);
                기록(연도, id, "진행", before, st.단계, 출처, 단계들[st.단계]);
                result = st;
            });
            return result;
        }

        /// <summary>오늘은 대기. 단계는 그대로 두고 오늘 확인한 것으로만 표시한다.</summary>
        public StatusRecord Defer(int 연도, string id, int 기대단계, DateTime 지금, DateTime 오늘, string 출처)
        {
            StatusRecord result = null;
            c.Tx(delegate
            {
                StatusRecord st = LoadStatus(연도, id);
                확인(st, 기대단계);
                st.최종확인일 = 오늘.Date;
                상태쓰기(st);
                기록(연도, id, "대기", st.단계, st.단계, 출처, "");
                result = st;
            });
            return result;
        }

        /// <summary>
        /// '오늘은 대기' 를 취소한다. 단계는 그대로 두고 오늘 확인 표시만 지워 다시 묻게 한다 (ADR-0019).
        /// 오늘 대기한 건이 아니면 거절한다.
        /// </summary>
        public StatusRecord 대기취소(int 연도, string id, int 기대단계, DateTime 지금, DateTime 오늘, string 출처)
        {
            StatusRecord result = null;
            c.Tx(delegate
            {
                StatusRecord st = LoadStatus(연도, id);
                확인(st, 기대단계);
                if (!st.최종확인일.HasValue || st.최종확인일.Value.Date != 오늘.Date)
                    throw new StageConflictException("오늘 대기한 건이 아닙니다.", st.단계);
                st.최종확인일 = null;
                상태쓰기(st);
                기록(연도, id, "대기취소", st.단계, st.단계, 출처, "");
                result = st;
            });
            return result;
        }

        /// <summary>
        /// 한 칸 되돌린다. 오늘 확인 표시도 지워 팝업이 다시 묻게 한다 —
        /// 잘못 완료 처리한 건이 조용히 기한을 넘기는 것을 막기 위해서다.
        /// </summary>
        public StatusRecord Revert(int 연도, string id, Flow flow, int 기대단계, DateTime 지금, string 출처)
        {
            return Revert(연도, id, Stages.For(flow), 기대단계, 지금, 출처);
        }

        public StatusRecord Revert(int 연도, string id, string[] 단계들, int 기대단계, DateTime 지금, string 출처)
        {
            StatusRecord result = null;
            c.Tx(delegate
            {
                StatusRecord st = LoadStatus(연도, id);
                확인(st, 기대단계);
                int last = 단계들.Length - 1;
                if (st.단계 > last) st.단계 = last;
                if (st.단계 <= 0)
                    throw new StageConflictException("첫 단계라 되돌릴 수 없습니다.", st.단계);
                int before = st.단계;
                st.단계 = before - 1;
                st.변경일시 = 지금;
                st.최종확인일 = null;
                상태쓰기(st);
                기록(연도, id, "되돌리기", before, st.단계, 출처, 단계들[before] + " → " + 단계들[st.단계]);
                result = st;
            });
            return result;
        }

        static void 확인(StatusRecord st, int 기대단계)
        {
            if (기대단계 >= 0 && st.단계 != 기대단계)
                throw new StageConflictException(
                    "다른 창에서 이 건의 단계가 먼저 바뀌었습니다. 화면을 새로 고친 뒤 다시 확인하세요.", st.단계);
        }

        // ── 변경 기록 ──

        void 기록(int 연도, string id, string 동작, int? 이전, int? 이후, string 출처, string 내용)
        {
            c.Run("INSERT INTO events(시각,연도,id,동작,이전단계,이후단계,출처,내용) VALUES(?,?,?,?,?,?,?,?)",
                DT(DateTime.Now), 연도, id, 동작, 이전.HasValue ? (object)이전.Value : null,
                이후.HasValue ? (object)이후.Value : null, 출처 ?? "", 내용 ?? "");
        }

        /// <summary>첨부·삭제처럼 단계와 무관한 일을 기록한다.</summary>
        public void AddEvent(int 연도, string id, string 동작, string 출처, string 내용)
        {
            기록(연도, id, 동작, null, null, 출처, 내용);
        }

        /// <summary>한 건의 기록. 최근 것이 먼저.</summary>
        public List<StatusEvent> LoadEvents(int 연도, string id)
        {
            return 기록읽기("SELECT rid,시각,연도,id,동작,이전단계,이후단계,출처,내용 FROM events WHERE 연도=? AND id=? ORDER BY rid DESC",
                연도, id);
        }

        /// <summary>기간 안의 기록. 최근 것이 먼저. to 는 그날 끝까지 포함한다.</summary>
        public List<StatusEvent> LoadEvents(DateTime from, DateTime to, int limit)
        {
            return 기록읽기("SELECT rid,시각,연도,id,동작,이전단계,이후단계,출처,내용 FROM events " +
                "WHERE 시각 >= ? AND 시각 < ? ORDER BY rid DESC LIMIT ?",
                D(from.Date), D(to.Date.AddDays(1)), limit);
        }

        List<StatusEvent> 기록읽기(string sql, params object[] args)
        {
            var list = new List<StatusEvent>();
            c.Each(sql, delegate(Reader r)
            {
                var e = new StatusEvent();
                e.Rid = r.Long(0);
                DateTime? t = r.Date(1);
                e.시각 = t.HasValue ? t.Value : DateTime.MinValue;
                e.연도 = r.Int(2);
                e.Id = r.Str(3);
                e.동작 = r.Str(4);
                e.이전단계 = r.IsNull(5) ? (int?)null : r.Int(5);
                e.이후단계 = r.IsNull(6) ? (int?)null : r.Int(6);
                e.출처 = r.Str(7);
                e.내용 = r.Str(8);
                list.Add(e);
            }, args);
            return list;
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
            c.Each("SELECT 연도,id,단계,저장파일,원본파일명,첨부일시,종류 FROM attachments ORDER BY 연도, id, 첨부일시, rid",
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
                    a.종류 = r.Str(6);
                    list.Add(a);
                });
            return list;
        }

        public void AddAttachment(Attachment a)
        {
            c.Run("INSERT INTO attachments(연도,id,단계,저장파일,원본파일명,첨부일시,종류) VALUES(?,?,?,?,?,?,?)",
                a.연도, a.Id, a.단계 ?? "", a.저장파일, a.원본파일명 ?? "",
                a.첨부일시 == DateTime.MinValue ? null : DT(a.첨부일시),
                a.종류 == Attachment.받은문서 ? Attachment.받은문서 : Attachment.증빙);
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

        /// <summary>파일이 온전한지 SQLite 에게 묻는다. 'ok' 면 정상.</summary>
        public string 무결성검사()
        {
            string result = "";
            c.Each("PRAGMA quick_check", delegate(Reader r) { result += (result.Length > 0 ? "; " : "") + r.Str(0); });
            return result;
        }

        /// <summary>WAL 파일을 본 파일에 합치고 단일 파일 방식으로 바꾼다. 파일을 옮기기 전에 쓴다.</summary>
        public void 단일파일로()
        {
            c.Run("PRAGMA journal_mode=DELETE");
        }
    }
}
