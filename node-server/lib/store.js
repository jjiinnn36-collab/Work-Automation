"use strict";
// 기존 납부알림.db 를 그대로 읽는다 (스키마 5판). 바깥 라이브러리 없이 Node 에 들어 있는 node:sqlite 만 쓴다.
// 자료 모양을 바꾸지 않으므로 윈도우 프로그램과 같은 파일을 번갈아 써도 된다.
const { DatabaseSync } = require("node:sqlite");
const D = require("./dates");
const { Stages, AmountRules, 납부있음 } = require("./model");

/** 두 창이 같은 건을 동시에 바꿀 때 뒤늦은 쪽이 받는다 (src/Db.cs 의 StageConflictException). */
class StageConflictError extends Error {
  constructor(message, 현재단계) { super(message); this.현재단계 = 현재단계; }
}

// DB 에 담는 날짜·일시·금액 글자. src/Db.cs 의 D/DT/M 과 같다.
const p2 = (n) => String(n).padStart(2, "0");
function DTnow() {
  const n = new Date();
  return n.getFullYear() + "-" + p2(n.getMonth() + 1) + "-" + p2(n.getDate()) + " " +
    p2(n.getHours()) + ":" + p2(n.getMinutes()) + ":" + p2(n.getSeconds());
}
function Ddate(d) { return d ? D.ymd(d) : null; }
function Mmoney(v) { return v == null ? null : String(v); }

const 항목칸 =
  "id,기관,비용명,진행흐름,월,말일,일,알림영업일,금액규칙,고정금액,비고,홈페이지명,홈페이지주소,묶음,단계정의,금액없음,시작연도,종료연도";

// 빈 DB 를 만들 때 쓰는 최종 5판 스키마 (윈도우 Db.cs 의 스키마1 + 이관2~5 를 합친 결과).
// 새 파일과 옛 파일을 이관한 결과가 같도록 칸·제약을 맞춘다.
const 스키마5 = [
  "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT)",
  "CREATE TABLE IF NOT EXISTS items(" +
    "id TEXT PRIMARY KEY, 기관 TEXT NOT NULL, 비용명 TEXT NOT NULL, " +
    "진행흐름 TEXT NOT NULL CHECK(진행흐름 IN ('신고납부','납부만','제출만','사용자설정')), " +
    "월 INTEGER NOT NULL CHECK(월 BETWEEN 1 AND 12), " +
    "말일 INTEGER NOT NULL DEFAULT 0, 일 INTEGER NOT NULL DEFAULT 0, " +
    "알림영업일 INTEGER NOT NULL DEFAULT 3, 금액규칙 TEXT NOT NULL DEFAULT '', " +
    "고정금액 TEXT, 비고 TEXT NOT NULL DEFAULT '', 순서 INTEGER NOT NULL DEFAULT 0, " +
    "홈페이지명 TEXT NOT NULL DEFAULT '', 홈페이지주소 TEXT NOT NULL DEFAULT '', 묶음 TEXT NOT NULL DEFAULT '', " +
    "단계정의 TEXT NOT NULL DEFAULT '', 금액없음 INTEGER NOT NULL DEFAULT 0, 시작연도 INTEGER, 종료연도 INTEGER)",
  "CREATE TABLE IF NOT EXISTS amounts(" +
    "연도 INTEGER NOT NULL, id TEXT NOT NULL, 금액 TEXT NOT NULL, 출처 TEXT NOT NULL DEFAULT '', " +
    "확인일 TEXT, 비고 TEXT NOT NULL DEFAULT '', PRIMARY KEY(연도, id))",
  "CREATE TABLE IF NOT EXISTS status(" +
    "연도 INTEGER NOT NULL, id TEXT NOT NULL, 단계 INTEGER NOT NULL DEFAULT 0, " +
    "변경일시 TEXT, 최종확인일 TEXT, 메모 TEXT NOT NULL DEFAULT '', PRIMARY KEY(연도, id))",
  "CREATE TABLE IF NOT EXISTS attachments(" +
    "rid INTEGER PRIMARY KEY AUTOINCREMENT, 연도 INTEGER NOT NULL, id TEXT NOT NULL, " +
    "단계 TEXT NOT NULL DEFAULT '', 저장파일 TEXT NOT NULL, 원본파일명 TEXT NOT NULL DEFAULT '', 첨부일시 TEXT, " +
    "종류 TEXT NOT NULL DEFAULT '증빙')",
  "CREATE TABLE IF NOT EXISTS holidays(날짜 TEXT PRIMARY KEY, 명칭 TEXT NOT NULL DEFAULT '')",
  "CREATE TABLE IF NOT EXISTS events(" +
    "rid INTEGER PRIMARY KEY AUTOINCREMENT, 시각 TEXT NOT NULL, 연도 INTEGER NOT NULL, id TEXT NOT NULL, " +
    "동작 TEXT NOT NULL, 이전단계 INTEGER, 이후단계 INTEGER, 출처 TEXT NOT NULL DEFAULT '', 내용 TEXT NOT NULL DEFAULT '')",
  "CREATE INDEX IF NOT EXISTS events_key ON events(연도, id)",
  "CREATE INDEX IF NOT EXISTS events_time ON events(시각)",
  // 6판: UNIQUE(거래처, 차입일) 없음 — 한 거래처가 같은 날 여러 사업에 빌려줄 수 있다. 중복은 차입명으로 판정.
  "CREATE TABLE IF NOT EXISTS loans(" +
    "묶음 TEXT PRIMARY KEY, 거래처 TEXT NOT NULL, 차입일 TEXT NOT NULL, 액면 TEXT NOT NULL, " +
    "이율 TEXT, 만기 TEXT, 파일 TEXT NOT NULL DEFAULT '', 시트 TEXT NOT NULL DEFAULT '', 가져온일시 TEXT, " +
    "차입명 TEXT NOT NULL DEFAULT '', 약칭 TEXT NOT NULL DEFAULT '')",
];

/** DB 의 일시 글자를 'YYYY-MM-DD HH:mm' 로. 시간대를 건드리지 않으려고 글자 그대로 자른다. */
function 일시글자(v) {
  if (!v) return null;
  const s = String(v).trim().replace("T", " ");
  return s.length >= 16 ? s.slice(0, 16) : s;
}

function num(v) {
  if (v == null) return null;
  const n = Number(String(v).trim());
  return Number.isFinite(n) ? n : null;
}

class Store {
  constructor(path) {
    this.path = path;
    this.db = new DatabaseSync(path);
    // WAL: 윈도우 팝업이 쓰는 동안 읽어도 서로 막지 않는다.
    try { this.db.exec("PRAGMA journal_mode=WAL"); } catch { }
    try { this.db.exec("PRAGMA busy_timeout=4000"); } catch { }
  }

  close() { try { this.db.close(); } catch { } }

  /**
   * DB 가 없으면 만든다 (윈도우 판 EnsureDb 와 같은 6판 스키마).
   * 이미 있으면 5판 → 6판 이관(loans 의 UNIQUE 제약 제거)만 맞춰 준다. 나머지는 윈도우 앱이 관리한다.
   */
  static 초기화(path) {
    const s = new Store(path);
    try {
      s.db.exec(스키마5.join(";\n"));
      const v = s.getMeta("schema_version");
      if (v == null) s.setMeta("schema_version", "6");
      // 옛 파일이 loans 에 UNIQUE(거래처,차입일) 를 갖고 있으면 떼어낸다 (같은 거래처·날짜 다른 사업 허용).
      s.loans유니크제거();
    } finally {
      s.close();
    }
  }

  /** loans 표에 UNIQUE(거래처,차입일) 제약이 있으면 표를 새로 만들어 없앤다 (5판 → 6판). */
  loans유니크제거() {
    const row = this.all("SELECT sql FROM sqlite_master WHERE type='table' AND name='loans'")[0];
    if (!row || !/UNIQUE/i.test(String(row.sql))) return;   // 이미 없으면 그대로
    this.tx(() => {
      this.run("CREATE TABLE loans_v6(묶음 TEXT PRIMARY KEY, 거래처 TEXT NOT NULL, 차입일 TEXT NOT NULL, 액면 TEXT NOT NULL, " +
        "이율 TEXT, 만기 TEXT, 파일 TEXT NOT NULL DEFAULT '', 시트 TEXT NOT NULL DEFAULT '', 가져온일시 TEXT, " +
        "차입명 TEXT NOT NULL DEFAULT '', 약칭 TEXT NOT NULL DEFAULT '')");
      this.run("INSERT INTO loans_v6(묶음,거래처,차입일,액면,이율,만기,파일,시트,가져온일시,차입명,약칭) " +
        "SELECT 묶음,거래처,차입일,액면,이율,만기,파일,시트,가져온일시,차입명,약칭 FROM loans");
      this.run("DROP TABLE loans");
      this.run("ALTER TABLE loans_v6 RENAME TO loans");
      this.setMeta("schema_version", "6");
    });
  }

  all(sql, ...args) { return this.db.prepare(sql).all(...args); }

  run(sql, ...args) { return this.db.prepare(sql).run(...args); }

  /** 한 쓰기 트랜잭션. 안에서 던지면 되돌린다. */
  tx(fn) {
    this.db.exec("BEGIN IMMEDIATE");
    try {
      const r = fn();
      this.db.exec("COMMIT");
      return r;
    } catch (e) {
      try { this.db.exec("ROLLBACK"); } catch { }
      throw e;
    }
  }

  getMeta(key) {
    const r = this.all("SELECT value FROM meta WHERE key=?", key);
    return r.length && r[0].value != null ? String(r[0].value) : null;
  }

  setMeta(key, value) {
    if (value == null) this.run("DELETE FROM meta WHERE key=?", key);
    else this.run("INSERT INTO meta(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value);
  }

  get 버전() { return num(this.getMeta("schema_version")) || 0; }

  loadMaster() {
    const list = this.all("SELECT " + 항목칸 + " FROM items ORDER BY 순서, id").map((r) => {
      const it = {
        Id: r.id,
        기관: r.기관,
        비용명: r.비용명,
        진행흐름: r.진행흐름,
        월: Number(r.월),
        말일: Number(r.말일) !== 0,
        일: Number(r.일),
        알림영업일: Number(r.알림영업일),
        금액규칙: r.금액규칙 || "",
        고정금액: num(r.고정금액),
        비고: r.비고 || "",
        홈페이지명: r.홈페이지명 || "",
        홈페이지주소: r.홈페이지주소 || "",
        묶음: r.묶음 || "",
        금액없음: false,
        시작연도: r.시작연도 == null ? null : Number(r.시작연도),
        종료연도: r.종료연도 == null ? null : Number(r.종료연도),
        사용자단계: null,
        사용자행동: null,
        차입: false,
      };
      if (it.진행흐름 === "사용자설정") {
        Stages.단계정의읽기(it, r.단계정의);
        it.금액없음 = Number(r.금액없음) !== 0;
      }
      return it;
    });
    const 차입묶음 = new Set(this.all("SELECT 묶음 FROM loans").map((r) => r.묶음));
    for (const it of list) it.차입 = it.묶음.length > 0 && 차입묶음.has(it.묶음);
    return list;
  }

  loadAmounts() {
    const map = Object.create(null);
    for (const r of this.all("SELECT 연도,id,금액,출처,확인일,비고 FROM amounts")) {
      const amt = num(r.금액);
      if (amt == null) continue;
      const a = {
        연도: Number(r.연도), Id: r.id, 금액: amt,
        출처: r.출처 || "", 확인일: D.parse(r.확인일), 비고: r.비고 || "",
      };
      map[a.연도 + "\t" + a.Id] = a;
    }
    return map;
  }

  loadStatus() {
    const map = Object.create(null);
    for (const r of this.all("SELECT 연도,id,단계,변경일시,최종확인일,메모 FROM status")) {
      const st = {
        연도: Number(r.연도), Id: r.id, 단계: Number(r.단계),
        변경일시: D.parse(r.변경일시),            // 날짜 부분 (UTC 자정)
        변경일시원문: 일시글자(r.변경일시),        // 'YYYY-MM-DD HH:mm'
        최종확인일: D.parse(r.최종확인일), 메모: r.메모 || "",
      };
      map[st.연도 + "\t" + st.Id] = st;
    }
    return map;
  }

  loadStartDate() { return D.parse(this.getMeta("start_date")); }

  loadHolidays() {
    const cache = { Dates: new Map(), Years: new Set(), Updated: null };
    for (const r of this.all("SELECT 날짜, 명칭 FROM holidays")) {
      const d = D.parse(r.날짜);
      if (!d) continue;
      cache.Dates.set(D.ymd(d), r.명칭 || "");
      cache.Years.add(D.year(d));
    }
    const years = this.getMeta("holidays.years");
    if (years) for (const y of years.split(",")) { const n = num(y); if (n != null) cache.Years.add(n); }
    const upd = this.getMeta("holidays.updated");
    if (upd) cache.Updated = D.parse(upd);
    return cache;
  }

  /** 건마다 마지막 동작. */
  마지막동작() {
    const map = Object.create(null);
    const rows = this.all(
      "SELECT e.연도, e.id, e.동작 FROM events e " +
      "JOIN (SELECT MAX(rid) m FROM events GROUP BY 연도, id) x ON e.rid = x.m");
    for (const r of rows) map[Number(r.연도) + "\t" + r.id] = r.동작;
    return map;
  }

  /** 화면의 차입처 자리: 약칭, 없으면 차입명, 그것도 없으면 ERP 거래처. */
  static 차입표시이름(약칭, 차입명, 거래처) {
    if (약칭 && 약칭.trim().length > 0) return 약칭.trim();
    if (차입명 && 차입명.trim().length > 0) return 차입명.trim();
    return 거래처 || "";
  }

  차입목록() {
    return this.all(
      "SELECT 묶음,거래처,차입일,액면,이율,만기,파일,시트,차입명,약칭 FROM loans ORDER BY 차입일, 거래처"
    ).map((r) => ({
      묶음: r.묶음, 거래처: r.거래처, 차입일: D.parse(r.차입일),
      액면: num(r.액면), 이율: num(r.이율), 만기: D.parse(r.만기),
      파일: r.파일 || "", 시트: r.시트 || "",
      차입명: r.차입명 || "", 약칭: r.약칭 || "",
      표시이름: Store.차입표시이름(r.약칭, r.차입명, r.거래처),
    }));
  }

  첨부수() {
    const map = Object.create(null);
    for (const r of this.all("SELECT 연도, id, COUNT(*) n FROM attachments GROUP BY 연도, id"))
      map[Number(r.연도) + "\t" + r.id] = Number(r.n);
    return map;
  }

  무결성검사() {
    // 윈도우 판과 같게 quick_check 결과를 그대로 돌려준다. 정상이면 'ok'.
    try {
      let result = "";
      for (const r of this.all("PRAGMA quick_check")) {
        const v = String(Object.values(r)[0]);
        result += (result.length > 0 ? "; " : "") + v;
      }
      return result;
    } catch (e) {
      return String(e.message || e);
    }
  }

  // ── 단계 변경 ── src/Db.cs 의 Advance/Defer/대기취소/Revert 와 같다.
  loadStatusOne(연도, id) {
    const r = this.all("SELECT 연도,id,단계,변경일시,최종확인일,메모 FROM status WHERE 연도=? AND id=?", 연도, id);
    if (r.length) {
      const x = r[0];
      return {
        연도: Number(x.연도), Id: x.id, 단계: Number(x.단계),
        변경일시: D.parse(x.변경일시), 변경일시원문: 일시글자(x.변경일시),
        최종확인일: D.parse(x.최종확인일), 메모: x.메모 || "",
      };
    }
    return { 연도, Id: id, 단계: 0, 변경일시: null, 변경일시원문: null, 최종확인일: null, 메모: "" };
  }

  상태쓰기(st) {
    this.run(
      "INSERT INTO status(연도,id,단계,변경일시,최종확인일,메모) VALUES(?,?,?,?,?,?) " +
      "ON CONFLICT(연도,id) DO UPDATE SET 단계=excluded.단계, 변경일시=excluded.변경일시, " +
      "최종확인일=excluded.최종확인일, 메모=excluded.메모",
      st.연도, st.Id, st.단계, st.변경일시원문 || null, Ddate(st.최종확인일), st.메모 || "");
  }

  기록(연도, id, 동작, 이전, 이후, 출처, 내용) {
    this.run("INSERT INTO events(시각,연도,id,동작,이전단계,이후단계,출처,내용) VALUES(?,?,?,?,?,?,?,?)",
      DTnow(), 연도, id, 동작, 이전 == null ? null : 이전, 이후 == null ? null : 이후, 출처 || "", 내용 || "");
  }

  addEvent(연도, id, 동작, 출처, 내용) { this.tx(() => this.기록(연도, id, 동작, null, null, 출처, 내용)); }

  static 확인(st, 기대단계) {
    if (기대단계 >= 0 && st.단계 !== 기대단계)
      throw new StageConflictError("다른 창에서 이 건의 단계가 먼저 바뀌었습니다. 화면을 새로 고친 뒤 다시 확인하세요.", st.단계);
  }

  /** stage 를 붙여 저장한다 (변경일시원문 갱신). */
  static 지금찍기(st) {
    const n = new Date();
    st.변경일시 = D.today();
    st.변경일시원문 = DTnow();
  }

  // 기한임박(기한 당일·지남)인데 아직 마지막 단계가 아니면 '오늘 확인' 을 남기지 않아 납부까지 계속 알린다.
  advance(연도, id, 단계들, 기대단계, 오늘, 출처, 기한임박) {
    return this.tx(() => {
      const st = this.loadStatusOne(연도, id);
      Store.확인(st, 기대단계);
      const last = 단계들.length - 1;
      if (st.단계 >= last) throw new StageConflictError("이미 마지막 단계까지 완료된 건입니다.", st.단계);
      const before = st.단계;
      st.단계 = before + 1;
      Store.지금찍기(st);
      // 기한 당일·지난 미완료 건은 오늘 확인을 찍지 않는다(계속 알림). 그 외에는 오늘 확인으로 둔다.
      st.최종확인일 = (기한임박 && st.단계 < last) ? null : 오늘;
      this.상태쓰기(st);
      this.기록(연도, id, "진행", before, st.단계, 출처, 단계들[st.단계]);
      return st;
    });
  }

  defer(연도, id, 기대단계, 오늘, 출처) {
    return this.tx(() => {
      const st = this.loadStatusOne(연도, id);
      Store.확인(st, 기대단계);
      st.최종확인일 = 오늘;
      this.상태쓰기(st);
      this.기록(연도, id, "대기", st.단계, st.단계, 출처, "");
      return st;
    });
  }

  대기취소(연도, id, 기대단계, 오늘, 출처) {
    return this.tx(() => {
      const st = this.loadStatusOne(연도, id);
      Store.확인(st, 기대단계);
      const r = this.all("SELECT 동작 FROM events WHERE 연도=? AND id=? ORDER BY rid DESC LIMIT 1", 연도, id);
      const 마지막 = r.length ? r[0].동작 : null;
      if (!st.최종확인일 || st.최종확인일.getTime() !== 오늘.getTime() || 마지막 !== "대기")
        throw new StageConflictError("오늘 대기한 건이 아닙니다.", st.단계);
      st.최종확인일 = null;
      this.상태쓰기(st);
      this.기록(연도, id, "대기취소", st.단계, st.단계, 출처, "");
      return st;
    });
  }

  revert(연도, id, 단계들, 기대단계, 출처) {
    return this.tx(() => {
      const st = this.loadStatusOne(연도, id);
      Store.확인(st, 기대단계);
      const last = 단계들.length - 1;
      if (st.단계 > last) st.단계 = last;
      if (st.단계 <= 0) throw new StageConflictError("첫 단계라 되돌릴 수 없습니다.", st.단계);
      const before = st.단계;
      st.단계 = before - 1;
      Store.지금찍기(st);
      st.최종확인일 = null;
      this.상태쓰기(st);
      this.기록(연도, id, "되돌리기", before, st.단계, 출처, 단계들[before] + " → " + 단계들[st.단계]);
      return st;
    });
  }

  // ── 금액 ──
  upsertAmounts(records, 출처) {
    this.tx(() => {
      for (const a of records) {
        this.run(
          "INSERT INTO amounts(연도,id,금액,출처,확인일,비고) VALUES(?,?,?,?,?,?) " +
          "ON CONFLICT(연도,id) DO UPDATE SET 금액=excluded.금액, 출처=excluded.출처, 확인일=excluded.확인일, 비고=excluded.비고",
          a.연도, a.Id, Mmoney(a.금액), a.출처 || "", Ddate(a.확인일), a.비고 || "");
        if (출처 != null)
          this.기록(a.연도, a.Id, "금액", null, null, 출처, 천단위(a.금액) + "원" + (a.출처 ? " · " + a.출처 : ""));
      }
    });
  }

  deleteAmount(연도, id, 출처) {
    this.tx(() => {
      const r = this.all("SELECT 금액 FROM amounts WHERE 연도=? AND id=?", 연도, id);
      const before = r.length ? r[0].금액 : null;
      const res = this.run("DELETE FROM amounts WHERE 연도=? AND id=?", 연도, id);
      if (Number(res.changes) > 0 && 출처 != null) this.기록(연도, id, "금액삭제", null, null, 출처, "지운 금액 " + before);
    });
  }

  // ── 항목 ──
  항목값(it, 순서) {
    const rule = AmountRules.Normalize ? AmountRules.Normalize(it.금액규칙, it.고정금액) : (it.금액규칙 || "변동");
    const fixedAmount = rule === "고정" && 납부있음(it) ? it.고정금액 : null;
    return [
      it.Id, it.기관 || "", it.비용명 || "", it.진행흐름, it.월, it.말일 ? 1 : 0,
      it.말일 ? 0 : it.일, it.알림영업일, rule, Mmoney(fixedAmount), it.비고 || "",
      it.홈페이지명 || "", it.홈페이지주소 || "", it.묶음 || "",
      Stages.단계정의 ? Stages.단계정의(it) : "", it.진행흐름 === "사용자설정" && it.금액없음 ? 1 : 0,
      it.시작연도 == null ? null : it.시작연도, it.종료연도 == null ? null : it.종료연도, 순서,
    ];
  }

  upsertItems(items) {
    const 자리 = "?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?";
    this.tx(() => {
      for (const it of items) {
        const r = this.all("SELECT COALESCE(MAX(순서), -1) + 1 n FROM items");
        const 다음순서 = Number(r[0].n);
        this.run(
          "INSERT INTO items(" + 항목칸 + ",순서) VALUES(" + 자리 + ",?) " +
          "ON CONFLICT(id) DO UPDATE SET 기관=excluded.기관, 비용명=excluded.비용명, " +
          "진행흐름=excluded.진행흐름, 월=excluded.월, 말일=excluded.말일, 일=excluded.일, " +
          "알림영업일=excluded.알림영업일, 금액규칙=excluded.금액규칙, 고정금액=excluded.고정금액, 비고=excluded.비고, " +
          "홈페이지명=excluded.홈페이지명, 홈페이지주소=excluded.홈페이지주소, 묶음=excluded.묶음, " +
          "단계정의=excluded.단계정의, 금액없음=excluded.금액없음, 시작연도=excluded.시작연도, 종료연도=excluded.종료연도",
          ...this.항목값(it, 다음순서));
      }
      this.setMeta("master_updated_at", DTnow());
    });
  }

  upsertItem(it) { this.upsertItems([it]); }

  새항목아이디() {
    return this.tx(() => {
      let n = num(this.getMeta("next_item_no"));
      if (n == null || n < 1) n = 1;
      let result;
      while (true) {
        const id = "item-" + String(n).padStart(4, "0");
        let used = false;
        for (const table of ["items", "status", "amounts", "attachments", "events"]) {
          const r = this.all("SELECT 1 FROM " + table + " WHERE id=? OR id LIKE ? LIMIT 1", id, id + "-%");
          if (r.length) { used = true; break; }
        }
        n++;
        if (!used) { result = id; break; }
      }
      this.setMeta("next_item_no", String(n));
      return result;
    });
  }

  deleteItem(id) {
    this.tx(() => {
      this.run("DELETE FROM items WHERE id=?", id);
      this.setMeta("master_updated_at", DTnow());
    });
  }

  moveItem(id, 방향) {
    return this.tx(() => {
      const ids = this.all("SELECT id FROM items ORDER BY 순서, id").map((r) => r.id);
      const i = ids.indexOf(id);
      const j = i + (방향 < 0 ? -1 : 1);
      if (i < 0 || j < 0 || j >= ids.length) return false;
      [ids[i], ids[j]] = [ids[j], ids[i]];
      for (let k = 0; k < ids.length; k++) this.run("UPDATE items SET 순서=? WHERE id=?", k, ids[k]);
      return true;
    });
  }

  moveItemTo(id, 위치) {
    return this.tx(() => {
      const ids = this.all("SELECT id FROM items ORDER BY 순서, id").map((r) => r.id);
      const i = ids.indexOf(id);
      if (i < 0) return false;
      const j = Math.max(0, Math.min(ids.length - 1, 위치));
      if (i === j) return false;
      ids.splice(i, 1);
      ids.splice(j, 0, id);
      for (let k = 0; k < ids.length; k++) this.run("UPDATE items SET 순서=? WHERE id=?", k, ids[k]);
      return true;
    });
  }

  setStartDate(d) { this.setMeta("start_date", Ddate(d)); }

  /** 같은 거래처·차입일로 이미 가져온 차입건의 묶음 이름. 없으면 null. */
  차입묶음(거래처, 차입일) {
    const r = this.all("SELECT 묶음 FROM loans WHERE 거래처=? AND 차입일=?", 거래처, Ddate(차입일));
    return r.length ? r[0].묶음 : null;
  }

  /** 같은 거래처·차입일·차입명 으로 이미 가져온 차입건의 묶음. 없으면 null (차입명으로 사업을 구분). */
  차입묶음이름(거래처, 차입일, 차입명) {
    const r = this.all("SELECT 묶음 FROM loans WHERE 거래처=? AND 차입일=? AND 차입명=?",
      거래처, Ddate(차입일), (차입명 || "").trim());
    return r.length ? r[0].묶음 : null;
  }

  /**
   * 차입건 하나를 한 트랜잭션으로 넣는다: 차입 정보, 새 회차 항목, 그 회차 금액, 변경 기록.
   * @returns 새로 넣은 회차 id 목록
   */
  차입가져오기(묶음, plan, items, amounts, 파일, 출처, 동작) {
    const 자리 = "?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?";
    const added = [];
    this.tx(() => {
      const now = DTnow();
      this.run(
        "INSERT INTO loans(묶음,거래처,차입일,액면,이율,만기,파일,시트,가져온일시,차입명,약칭) VALUES(?,?,?,?,?,?,?,?,?,?,?) " +
        "ON CONFLICT(묶음) DO UPDATE SET 액면=excluded.액면, 이율=excluded.이율, 만기=excluded.만기, " +
        "파일=excluded.파일, 시트=excluded.시트, 가져온일시=excluded.가져온일시, " +
        "차입명=CASE WHEN excluded.차입명<>'' THEN excluded.차입명 ELSE loans.차입명 END, " +
        "약칭=CASE WHEN excluded.차입명<>'' THEN excluded.약칭 ELSE loans.약칭 END",
        묶음, plan.거래처, Ddate(plan.차입일), Mmoney(plan.액면), Mmoney(plan.이율), Ddate(plan.만기),
        파일 || "", plan.시트 || "", now, (plan.차입명 || "").trim(), (plan.약칭 || "").trim());

      for (const it of items) {
        const r = this.all("SELECT COALESCE(MAX(순서), -1) + 1 n FROM items");
        const 다음순서 = Number(r[0].n);
        const res = this.run("INSERT INTO items(" + 항목칸 + ",순서) VALUES(" + 자리 + ",?) ON CONFLICT(id) DO NOTHING",
          ...this.항목값(it, 다음순서));
        if (Number(res.changes) === 0) continue;
        added.push(it.Id);

        let 내용 = "ERP 차입스케줄 " + (파일 || "") + (plan.시트 ? " · 시트 " + plan.시트 : "");
        const a = amounts[it.Id];
        if (a) {
          this.run(
            "INSERT INTO amounts(연도,id,금액,출처,확인일,비고) VALUES(?,?,?,?,?,?) " +
            "ON CONFLICT(연도,id) DO UPDATE SET 금액=excluded.금액, 출처=excluded.출처, 확인일=excluded.확인일, 비고=excluded.비고",
            a.연도, a.Id, Mmoney(a.금액), a.출처 || "", Ddate(a.확인일), a.비고 || "");
          내용 += " · " + 천단위(a.금액) + "원";
          this.기록(a.연도, it.Id, 동작, null, null, 출처, 내용);
        } else if (it.시작연도 != null) {
          this.기록(it.시작연도, it.Id, 동작, null, null, 출처, 내용);
        }
      }
      if (added.length > 0) this.setMeta("master_updated_at", now);
    });
    return added;
  }
}

// C# 의 "N0" 천 단위 구분 (금액 기록 문구용).
function 천단위(n) {
  const neg = n < 0;
  const s = Math.abs(Math.round(n)).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return (neg ? "-" : "") + s;
}

module.exports = { Store, StageConflictError };
