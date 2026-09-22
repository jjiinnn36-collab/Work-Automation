"use strict";
// 읽기 기능. src/WebServer.cs 의 Alerts / Month / Year / Items / History / Settings / Group / Events 를 옮긴 것.
const fs = require("node:fs");
const path = require("node:path");
const D = require("./dates");
const Sched = require("./scheduler");
const { Stages, AmountRules, 해당연도, 카드이름 } = require("./model");
const D2 = require("./dto");
const { head, dto, itemDto } = D2;

class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

// ── 입력 읽기 ──────────────────────────────────────────────
function 연도(s) {
  const n = Number(String(s || "").trim());
  if (!Number.isInteger(n) || n < 2000 || n > 2100) throw new HttpError(400, "연도가 올바르지 않습니다.");
  return n;
}

function 아이디(s) {
  const v = String(s || "").trim();
  if (v.length === 0) throw new HttpError(400, "항목을 고르지 않았습니다.");
  return v;
}

function 날짜(s, 기본) {
  const t = String(s || "").trim();
  if (t.length === 0) return 기본;
  const d = D.parse(t);
  if (!d || D.year(d) < 2000 || D.year(d) > 2100) throw new HttpError(400, "날짜 형식이 올바르지 않습니다 (예: 2026-09-01).");
  return d;
}

// ── 경고 ───────────────────────────────────────────────────
const 공휴일오래됨일 = 90;

function warnings(cache, years, today) {
  const parts = [];
  const missing = years.filter((y) => !cache.Years.has(y)).map(String);
  if (missing.length > 0) {
    parts.push(missing.join(", ") +
      "년 공휴일 자료가 없습니다. 해당 연도는 주말만 반영해 계산하며, 안전을 위해 알림을 이틀 앞당겼습니다. " +
      "설정 화면에서 공공데이터포털 인증키를 넣고 공휴일을 갱신하세요.");
  } else if (cache.Updated) {
    const 지남 = Math.round((today.getTime() - cache.Updated.getTime()) / 86400000);
    if (지남 > 공휴일오래됨일)
      parts.push("공휴일 자료를 갱신한 지 " + 지남 + "일 지났습니다. 임시공휴일이 반영되지 않았을 수 있습니다.");
  }
  return parts;
}

// ── 집계 ───────────────────────────────────────────────────
// 기한 지남 = 기한이 지난 미완료, 진행중 = 한 단계라도 밟은 미완료, 진행예정 = 아직 아무 단계도 안 밟은 미완료.
class 집계 {
  constructor() { this.진행중 = 0; this.진행예정 = 0; this.완료 = 0; this.지남 = 0; this.시작전 = 0; this.지난 = 0; this.지난완료 = 0; }

  /** 센 건이 시작일 이전이면 true (금액 합계에서도 빼라는 뜻). */
  더하기(env, o) {
    if (env.시작전(o)) { this.시작전++; return true; }
    const done = env.done(o);
    const 기한지남 = o.보정기한일.getTime() < env.Today.getTime();
    if (기한지남) { this.지난++; if (done) this.지난완료++; }
    if (done) this.완료++;
    else if (기한지남) this.지남++;
    else if (env.단계(o) >= 1) this.진행중++;
    else this.진행예정++;
    return false;
  }
}

// 기관 이름 정렬. 윈도우 판은 .NET 의 ko-KR 정렬을 쓰는데, 거기서는 로마자가 한글보다 앞선다.
// ICU 의 'ko' 는 그 반대라 화면 순서가 달라지므로 'en' 정렬을 쓴다 (한글끼리는 가나다순 그대로).
const 기관정렬 = new Intl.Collator("en");
function 기관순(a, b) { return 기관정렬.compare(a, b); }

function occurrenceOf(env, it, year) {
  if (!해당연도(it, year)) throw new HttpError(400, "이 항목은 " + year + "년에 기한이 없습니다 (유효연도 밖).");
  for (const o of Sched.buildOccurrences([it], env.Cal, D.make(year, 7, 1), env.Amounts, null))
    if (o.연도 === year) return o;
  throw new Error("발생 건을 만들지 못했습니다.");
}

// ── 화면별 자료 ────────────────────────────────────────────
function alerts(env) {
  const occs = Sched.buildOccurrences(env.Master, env.Cal, env.Today, env.Amounts, env.시작일);

  // 오늘 대기를 누른 건도 화면에는 남겨 둔다. 사라지면 누른 게 먹혔는지 알 수 없다.
  const 오늘대기 = new Set();
  for (const key of Object.keys(env.Status)) {
    const st = env.Status[key];
    if (st.최종확인일 && st.최종확인일.getTime() === env.Today.getTime()) {
      오늘대기.add(st.연도 + "\t" + st.Id);
      st.최종확인일 = null;
    }
  }

  const set = Sched.buildRows(occs, env.Status, env.Cal, env.Today);

  const rows = [];
  let 남은 = 0;
  for (const r of set.Rows) {
    const 대기함 = 오늘대기.has(r.Occ.Key);
    if (!대기함) 남은++;
    rows.push(dto(env, r.Occ, 대기함));
  }
  const overdue = set.Overdue.map((r) => dto(env, r.Occ, 오늘대기.has(r.Occ.Key)));

  let 알림전 = 0;
  for (const o of occs) {
    if (o.연도 !== D.year(env.Today) || o.알림일.getTime() <= env.Today.getTime()) continue;
    if (!env.done(o)) 알림전++;
  }

  return Object.assign(head(env), {
    rows, pending: 남은, overdue, notYet: 알림전,
    warnings: warnings(env.공휴일, Sched.targetYears(env.Today), env.Today),
  });
}

function month(env, ym) {
  let y = D.year(env.Today), m = D.month(env.Today);
  if (ym) {
    const mm = /^(\d{4})-(\d{2})$/.exec(String(ym).trim());
    if (!mm || +mm[1] < 2000 || +mm[1] > 2100 || +mm[2] < 1 || +mm[2] > 12)
      throw new HttpError(400, "월 형식이 올바르지 않습니다 (예: 2026-09).");
    y = +mm[1]; m = +mm[2];
  }

  const list = env.occurrencesOf(y).filter((o) => D.month(o.원기한일) === m);
  list.sort((a, b) => {
    const c = a.원기한일.getTime() - b.원기한일.getTime();
    if (c !== 0) return c < 0 ? -1 : 1;
    return a.Item.Id < b.Item.Id ? -1 : a.Item.Id > b.Item.Id ? 1 : 0;
  });

  const rows = [];
  const cnt = new 집계();
  let 합계 = 0, 미확인 = 0;
  for (const o of list) {
    if (!cnt.더하기(env, o)) {
      const a = AmountRules.금액(o);
      if (a != null) 합계 += a;
      else if (AmountRules.미확인(o)) 미확인++;
    }
    rows.push(dto(env, o, false));
  }

  return Object.assign(head(env), {
    year: y, month: m, rows,
    inProgress: cnt.진행중, upcoming: cnt.진행예정, done: cnt.완료, overdue: cnt.지남,
    beforeStart: cnt.시작전, total: 합계, amountUnknown: 미확인,
  });
}

function year(env, ys) {
  const y = ys ? 연도(ys) : D.year(env.Today);
  const occs = env.occurrencesOf(y);
  occs.sort((a, b) => {
    const c = a.보정기한일.getTime() - b.보정기한일.getTime();
    if (c !== 0) return c < 0 ? -1 : 1;
    return a.Item.Id < b.Item.Id ? -1 : a.Item.Id > b.Item.Id ? 1 : 0;
  });

  const cnt = new 집계();
  let 미확인 = 0;
  const remaining = [], finished = [], orgs = [];
  for (const o of occs) {
    const before = cnt.더하기(env, o);
    const done = env.done(o);
    if (!before && AmountRules.미확인(o)) 미확인++;
    if (!orgs.includes(o.Item.기관)) orgs.push(o.Item.기관);

    // 지난 건 묶음 = 끝난 건 + 추적 시작일 이전 건, 최근 것이 위로
    if (done || before) finished.unshift(dto(env, o, false));
    else if (o.보정기한일.getTime() < env.Today.getTime()) remaining.splice(cnt.지남 - 1, 0, dto(env, o, false)); // 놓친 기한은 맨 위에 고정
    else remaining.push(dto(env, o, false));
  }
  orgs.sort(기관순);

  return Object.assign(head(env), {
    year: y,
    count: occs.length - cnt.시작전,
    beforeStart: cnt.시작전,
    past: cnt.지난, pastDone: cnt.지난완료,
    inProgress: cnt.진행중, upcoming: cnt.진행예정,
    overdue: cnt.지남, amountUnknown: 미확인,
    remaining, finished, orgs,
  });
}

function items(env) {
  const y = D.year(env.Today);
  const list = env.Master.map((it) => {
    // 올해 기한이 없는 항목은 올해 금액이 해당 없다 — '미확인' 으로 보이면 안 된다.
    const 올해있음 = require("./model").납부있음(it) && 해당연도(it, y);
    const rec = 올해있음 ? env.Amounts[y + "\t" + it.Id] : null;
    const 입력함 = !!rec;
    let 올해 = null;
    if (입력함) 올해 = rec.금액;
    else if (올해있음 && it.금액규칙 === AmountRules.고정) 올해 = it.고정금액;

    return env.기관사이트붙이기(Object.assign(itemDto(it), {
      thisYearAmount: 올해,
      thisYearEntered: 입력함,
      thisYearUnknown: 올해 == null && 올해있음,
    }), it);
  });
  return Object.assign(head(env), { items: list });
}

function history(env, from, to) {
  const y = D.year(env.Today);
  const a = 날짜(from, D.make(y, 1, 1));
  const b = 날짜(to, D.make(y, 12, 31));
  if (b.getTime() < a.getTime()) throw new HttpError(400, "조회 기간의 끝이 시작보다 빠릅니다.");

  const rows = [];
  let 합계 = 0;
  for (const key of Object.keys(env.Status)) {
    const st = env.Status[key];
    const it = env.Item(st.Id);
    if (!it || !st.변경일시) continue;
    if (st.단계 < Stages.finalIndex(it)) continue;
    const 처리일 = st.변경일시;
    if (처리일.getTime() < a.getTime() || 처리일.getTime() > b.getTime()) continue;

    const o = occurrenceOf(env, it, st.연도);
    const amt = AmountRules.금액(o);
    if (amt != null) 합계 += amt;
    const row = dto(env, o, false);
    row.doneAt = st.변경일시원문 ? st.변경일시원문.slice(0, 10) : D.ymd(st.변경일시);
    rows.push({ at: st.변경일시원문 || D.ymd(st.변경일시), row });
  }
  rows.sort((x, y2) => (x.at < y2.at ? 1 : x.at > y2.at ? -1 : 0));

  return Object.assign(head(env), {
    from: D.ymd(a), to: D.ymd(b),
    rows: rows.map((r) => r.row),
    total: 합계,
  });
}

function group(env, y, g) {
  const name = String(g || "").trim();
  if (name.length === 0) throw new HttpError(400, "묶음 이름이 없습니다.");
  const all = env.Master.filter((x) => x.묶음 === name);
  if (all.length === 0) throw new HttpError(404, "그런 분할납부 묶음이 없습니다: " + name);
  const list = all.filter((x) => 해당연도(x, y));
  if (list.length === 0) throw new HttpError(404, "이 묶음은 " + y + "년에 회차가 없습니다: " + name);
  list.sort((a, b) => (a.월 !== b.월 ? a.월 - b.월 : a.Id < b.Id ? -1 : a.Id > b.Id ? 1 : 0));

  const rows = [];
  let total = 0, entered = 0;
  for (const it of list) {
    const o = occurrenceOf(env, it, y);
    const a = AmountRules.금액(o);
    if (a != null) total += a;
    if (o.실제금액 != null) entered++;
    rows.push(dto(env, o, false));
  }
  return { year: y, group: list[0].묶음, org: list[0].기관, name: 카드이름(list[0]), rows, total, entered, count: list.length };
}

function events(env, q) {
  let list;
  if (q.id) {
    list = env.Db.all(
      "SELECT rid,시각,연도,id,동작,이전단계,이후단계,출처,내용 FROM events WHERE 연도=? AND id=? ORDER BY rid DESC",
      연도(q.y), 아이디(q.id));
  } else {
    const from = 날짜(q.from, D.addDays(env.Today, -30));
    const to = 날짜(q.to, env.Today);
    list = env.Db.all(
      "SELECT rid,시각,연도,id,동작,이전단계,이후단계,출처,내용 FROM events WHERE 시각 >= ? AND 시각 < ? ORDER BY rid DESC LIMIT ?",
      D.ymd(from), D.ymd(D.addDays(to, 1)), 500);
  }

  const rows = list.map((e) => {
    const it = env.Item(e.id);
    const stages = it ? Stages.For(it) : null;
    const 이름 = (s) => (s == null || !stages || s < 0 || s >= stages.length ? null : stages[s]);
    return env.기관사이트붙이기({
      at: String(e.시각 || "").replace("T", " ").slice(0, 19),
      year: Number(e.연도), id: e.id,
      name: it ? 카드이름(it) : (e.id === "-" ? "설정" : e.id),
      org: it ? it.기관 : "",
      action: e.동작, source: e.출처, detail: e.내용,
      from: 이름(e.이전단계 == null ? null : Number(e.이전단계)),
      to: 이름(e.이후단계 == null ? null : Number(e.이후단계)),
    }, it);
  });
  return { rows };
}

function loans(env) {
  const 회차수 = new Map();
  for (const it of env.Master) if (it.묶음) 회차수.set(it.묶음, (회차수.get(it.묶음) || 0) + 1);

  const list = env.Db.차입목록().map((x) => ({
    group: x.묶음, org: x.표시이름, name: x.차입명, short: x.약칭,
    start: x.차입일 ? D.ymd(x.차입일) : null,
    face: x.액면, rate: x.이율,
    maturity: x.만기 ? D.ymd(x.만기) : null,
    count: 회차수.get(x.묶음) || 0,
    file: x.파일,
  }));
  return Object.assign(head(env), { loans: list });
}

function attachments(env, y, id) {
  const rows = env.Db.all(
    "SELECT 저장파일,원본파일명,단계,종류,첨부일시 FROM attachments WHERE 연도=? AND id=? ORDER BY rid", y, id);
  return {
    year: y, id,
    files: rows.map((a) => ({
      file: a.저장파일, name: a.원본파일명, stage: a.단계, kind: a.종류,
      at: a.첨부일시 ? String(a.첨부일시).replace("T", " ").slice(0, 16) : "",
    })),
  };
}

function settings(env, opts) {
  const years = [...env.공휴일.Years].sort((a, b) => a - b);
  const backupDir = opts.backupDir;
  let backups = [];
  try {
    backups = fs.readdirSync(backupDir)
      .filter((n) => n.toLowerCase().endsWith(".db"))
      .map((n) => {
        const s = fs.statSync(path.join(backupDir, n));
        return { name: n, size: s.size, at: 일시(s.mtime), ms: s.mtimeMs };
      })
      .sort((a, b) => b.ms - a.ms)
      .slice(0, 10)
      .map(({ name, size, at }) => ({ name, size, at }));
  } catch { }

  return Object.assign(head(env), {
    version: opts.version,
    schema: env.Db.버전,
    integrity: env.Db.무결성검사(),
    dataDir: opts.dataDir,
    dbPath: path.join(opts.dataDir, "납부알림.db"),
    logPath: path.join(opts.dataDir, "납부알림.log"),
    backupDir,
    backupDirDefault: opts.backupDirDefault,
    backupDirCustom: opts.backupDirCustom,
    backups,
    apiKeySet: fs.existsSync(path.join(opts.dataDir, "apikey.txt")),
    holidayYears: years,
    holidayCount: env.공휴일.Dates.size,
    holidayUpdated: env.공휴일.Updated ? D.ymd(env.공휴일.Updated) : null,
    holidayJob: { running: false, message: "" },
    warnings: warnings(env.공휴일, Sched.targetYears(env.Today), env.Today),
  });
}

function 일시(d) {
  const p = (n) => String(n).padStart(2, "0");
  return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) + " " + p(d.getHours()) + ":" + p(d.getMinutes());
}

module.exports = {
  HttpError, 연도, 아이디, 날짜, warnings, occurrenceOf,
  alerts, month, year, items, history, group, events, loans, attachments, settings,
};
