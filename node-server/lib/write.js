"use strict";
// 쓰기 기능. src/WebServer.cs / WebServer.Features.cs 의 POST 처리를 옮긴 것.
// 값 검사 문구까지 같게 두어 화면이 같은 오류를 보이게 한다.
const D = require("./dates");
const { Stages, AmountRules, 납부있음, 해당연도 } = require("./model");
const { StageConflictError } = require("./store");
const { HttpError, 연도, 아이디, occurrenceOf } = require("./api");
const { dto, itemDto } = require("./dto");
const fs = require("node:fs");
const SW = require("./settings-write");

const 출처 = "웹";
const 아이디형식 = /^[A-Za-z0-9_-]{1,40}$/;

// ── 입력 검사 (src/WebServer.cs 와 같은 규칙) ──────────────
function 정수(s, 이름, min, max) {
  const t = (s == null ? "" : String(s)).trim();
  if (!/^\d+$/.test(t)) throw new HttpError(400, `${이름}은(는) ${min}~${max} 사이의 숫자여야 합니다.`);
  const v = Number(t);
  if (v < min || v > max) throw new HttpError(400, `${이름}은(는) ${min}~${max} 사이의 숫자여야 합니다.`);
  return v;
}

/** 원 단위 금액. 쉼표·공백·'원'은 허용하고, 음수와 소수는 거절한다. */
function 금액(s, 이름) {
  const t = (s == null ? "" : String(s)).replace(/,/g, "").replace(/ /g, "").replace(/원/g, "").trim();
  if (t.length === 0 || !/^-?\d+(\.\d+)?$/.test(t)) throw new HttpError(400, `${이름}은(는) 숫자로 넣어 주세요.`);
  const v = Number(t);
  if (v !== Math.trunc(v)) throw new HttpError(400, `${이름}은(는) 원 단위로 넣어 주세요.`);
  if (v > 999999999999999) throw new HttpError(400, `${이름}이(가) 너무 큽니다.`);
  return v;
}

function 글자(s, 이름, max, 필수) {
  const t = (s == null ? "" : String(s)).trim();
  if (필수 && t.length === 0) throw new HttpError(400, `${이름}을(를) 넣어 주세요.`);
  if (t.length > max) throw new HttpError(400, `${이름}은(는) ${max}자까지 넣을 수 있습니다.`);
  for (const ch of t) {
    const c = ch.charCodeAt(0);
    if (c < 32 || c === 127) throw new HttpError(400, `${이름}에 쓸 수 없는 문자가 있습니다.`);
  }
  return t;
}

/** 신고 홈페이지 주소. http/https 만 받는다. */
function 주소(s) {
  const t = (s == null ? "" : String(s)).trim();
  if (t.length === 0) return "";
  let u;
  try { u = new URL(t); } catch { u = null; }
  if (t.length > 300 || !u || (u.protocol !== "http:" && u.protocol !== "https:"))
    throw new HttpError(400, "홈페이지 주소는 http:// 또는 https:// 로 시작해야 합니다.");
  return u.href;
}

function 줄목록(s) {
  const t = (s == null ? "" : String(s)).replace(/\r/g, "");
  if (t.trim().length === 0) return [];
  return t.split("\n");
}

function 연도칸(s, 이름) {
  const t = (s == null ? "" : String(s)).trim();
  if (t.length === 0) return null;
  if (!/^\d+$/.test(t)) throw new HttpError(400, `${이름}는 2000~2100 사이 연도여야 합니다.`);
  const y = Number(t);
  if (y < 2000 || y > 2100) throw new HttpError(400, `${이름}는 2000~2100 사이 연도여야 합니다.`);
  return y;
}

function 월목록(s) {
  const t = (s == null ? "" : String(s)).trim();
  if (t.indexOf(",") < 0) return [정수(t, "월", 1, 12)];
  const list = [];
  for (const part of t.split(",")) {
    const p = part.trim();
    if (p.length === 0) continue;
    const m = 정수(p, "월", 1, 12);
    if (list.includes(m)) throw new HttpError(400, "같은 월이 두 번 들어 있습니다: " + m + "월");
    list.push(m);
  }
  if (list.length === 0) throw new HttpError(400, "월을 넣어 주세요.");
  list.sort((a, b) => a - b);
  return list;
}

function 항목찾기(env, s) {
  const it = env.Item(아이디(s));
  if (!it) throw new HttpError(404, "항목을 찾을 수 없습니다: " + s);
  return it;
}

function 연도확인(it, year) {
  if (!해당연도(it, year)) throw new HttpError(400, "이 항목은 " + year + "년에 기한이 없습니다 (유효연도 밖).");
}

function 일수(y, m) { return D.daysInMonth(y, m); }

// ── 단계 변경 ──────────────────────────────────────────────
function changeStage(env, f, 동작) {
  const y = 연도(f.y);
  const it = 항목찾기(env, f.id);
  연도확인(it, y);
  let 기대 = -1;
  const s = f.stage;
  if (s != null && s !== "") {
    if (!/^\d+$/.test(String(s)) || Number(s) > Stages.finalIndex(it)) throw new HttpError(400, "단계 값이 올바르지 않습니다.");
    기대 = Number(s);
  }
  // 기한 당일·지난 미완료 건은 한 단계 진행해도 '오늘 확인' 을 찍지 않아 납부까지 계속 알린다 (사용자 요청 2026-09-22).
  const 기한임박 = occurrenceOf(env, it, y).보정기한일.getTime() <= env.Today.getTime();
  try {
    const 단계들 = Stages.For(it);
    let st;
    if (동작 === "진행") st = env.Db.advance(y, it.Id, 단계들, 기대, env.Today, 출처, 기한임박);
    else if (동작 === "대기") st = env.Db.defer(y, it.Id, 기대, env.Today, 출처);
    else if (동작 === "대기취소") st = env.Db.대기취소(y, it.Id, 기대, env.Today, 출처);
    else st = env.Db.revert(y, it.Id, 단계들, 기대, 출처);
    env.Status[st.연도 + "\t" + st.Id] = st;
    env._마지막동작 = null; // 방금 남긴 기록을 다시 읽게 한다
  } catch (e) {
    if (e instanceof StageConflictError) throw new HttpError(409, e.message);
    throw e;
  }
  return dto(env, occurrenceOf(env, it, y), 동작 === "대기");
}

// ── 금액 ──────────────────────────────────────────────────
function saveAmount(env, f) {
  const y = 연도(f.y);
  const it = 항목찾기(env, f.id);
  연도확인(it, y);
  const v = 금액(f.amount, "금액");
  const rec = { 연도: y, Id: it.Id, 금액: v, 출처: "웹 입력", 확인일: env.Today, 비고: 글자(f.memo, "메모", 200, false) };
  env.Db.upsertAmounts([rec], 출처);
  env.Amounts[y + "\t" + it.Id] = rec;
  return dto(env, occurrenceOf(env, it, y), false);
}

function deleteAmount(env, f) {
  const y = 연도(f.y);
  const id = 아이디(f.id);
  env.Db.deleteAmount(y, id, 출처);
  delete env.Amounts[y + "\t" + id];
  return { ok: true };
}

// ── 항목 ──────────────────────────────────────────────────
function 항목읽기(env, f, id, month) {
  const it = {
    Id: id, 묶음: "", 차입: false, 사용자단계: null, 사용자행동: null, 금액없음: false,
    고정금액: null, 시작연도: null, 종료연도: null,
  };
  it.기관 = 글자(f.org, "기관", 60, true);
  it.비용명 = 글자(f.name, "비용명", 60, true);
  const flow = (f.flow || "").trim();
  if (!["신고납부", "납부만", "제출만", "사용자설정"].includes(flow))
    throw new HttpError(400, "알 수 없는 진행흐름: '" + f.flow + "' (신고납부 / 납부만 / 제출만 / 사용자설정 중 하나여야 합니다)");
  it.진행흐름 = flow;

  if (it.진행흐름 === "사용자설정") {
    const names = 줄목록(f.stages).map((x) => x.trim());
    const raw = 줄목록(f.actions);
    const acts = new Array(names.length);
    for (let i = 0; i < names.length; i++) acts[i] = i < raw.length ? raw[i].trim() : "";
    if (acts.length > 0) acts[0] = "";
    const 문제 = 단계검사(names, acts);
    if (문제) throw new HttpError(400, 문제);
    it.사용자단계 = names;
    it.사용자행동 = acts;
    const na = (f.noAmount || "").trim();
    it.금액없음 = na === "1" || na === "true";
  }

  it.월 = month;
  const day = (f.day || "").trim();
  if (day === "말일" || day.toUpperCase() === "EOM") { it.말일 = true; it.일 = 0; }
  else { it.말일 = false; it.일 = 정수(day, "기한일", 1, 일수(2023, it.월)); }

  const lead = (f.lead || "").trim();
  it.알림영업일 = lead.length === 0 ? 3 : 정수(lead, "알림 영업일", 1, 60);

  let rule = (f.rule || "").trim();
  if (rule.length === 0) rule = "변동";
  if (!AmountRules.IsValid(rule)) throw new HttpError(400, "금액규칙은 고정 또는 변동이어야 합니다.");
  it.금액규칙 = rule;
  const fixedText = (f.fixed || "").trim();
  if (납부있음(it) && rule === "고정") {
    if (fixedText.length === 0) throw new HttpError(400, "고정 규칙은 금액을 넣어야 합니다.");
    it.고정금액 = 금액(fixedText, "고정금액");
  }

  it.비고 = 글자(f.memo, "비고", 200, false);
  it.홈페이지명 = 글자(f.siteName, "홈페이지 이름", 40, false);
  it.홈페이지주소 = 주소(f.siteUrl);
  if (it.홈페이지주소.length > 0 && it.홈페이지명.length === 0) it.홈페이지명 = "홈페이지";
  return it;
}

// src/Model.cs Stages.단계검사 를 옮긴 것.
function 단계검사(names, actions) {
  const 최소 = 2, 최대 = 8, 이름최대 = 20;
  if (!names || names.length < 최소 || names.length > 최대)
    return `단계는 ${최소}~${최대}개여야 합니다 (첫 단계는 시작 지점).`;
  const seen = new Set();
  for (let i = 0; i < names.length; i++) {
    const n = (names[i] || "").trim();
    const a = actions && i < actions.length ? (actions[i] || "").trim() : "";
    if (n.length === 0) return (i + 1) + "번째 단계 이름을 넣어 주세요.";
    if (n.length > 이름최대 || a.length > 이름최대) return `단계 이름과 버튼 문구는 ${이름최대}자까지입니다.`;
    for (const ch of n + a) { const c = ch.charCodeAt(0); if (ch === "|" || c < 32 || c === 127) return "단계 이름에 | 나 줄바꿈은 쓸 수 없습니다."; }
    if (seen.has(n)) return "같은 단계 이름이 두 번 있습니다: " + n;
    seen.add(n);
  }
  return null;
}

function saveItem(env, f) {
  let id = (f.id || "").trim();
  const 새항목 = f.mode === "new";
  if (새항목 && id.length === 0) id = env.Db.새항목아이디();
  if (!아이디형식.test(id)) throw new HttpError(400, "id 는 영문·숫자·_·- 로 1~40자여야 합니다.");

  const months = 월목록(f.month);
  if (months.length > 1) {
    if (!새항목) throw new HttpError(400, "여러 회차는 새로 추가할 때만 만들 수 있습니다. 회차마다 따로 고치세요.");
    return 분할항목추가(env, f, id, months);
  }

  if (새항목 && env.Item(id)) throw new HttpError(409, "같은 id 의 항목이 이미 있습니다: " + id);
  if (!새항목 && !env.Item(id)) throw new HttpError(404, "고칠 항목이 없습니다: " + id);

  const it = 항목읽기(env, f, id, months[0]);
  const 기존 = env.Item(id);
  it.묶음 = 기존 ? 기존.묶음 : "";
  it.차입 = !!(기존 && 기존.차입);
  it.시작연도 = f.startYear != null ? 연도칸(f.startYear, "시작 연도") : (기존 ? 기존.시작연도 : null);
  it.종료연도 = f.endYear != null ? 연도칸(f.endYear, "종료 연도") : (기존 ? 기존.종료연도 : null);
  if (it.시작연도 != null && it.종료연도 != null && it.종료연도 < it.시작연도)
    throw new HttpError(400, "종료 연도가 시작 연도보다 빠릅니다.");

  env.Db.upsertItem(it);
  // 맥 판은 팝업을 브라우저 창으로 따로 띄우므로 즉시 팝업 요청은 하지 않는다.
  return { ok: true, item: itemDto(it), popup: false };
}

function 분할항목추가(env, f, baseId, months) {
  const items = [];
  for (const m of months) {
    const id = baseId + "-" + String(m).padStart(2, "0");
    if (env.Item(id)) throw new HttpError(409, "같은 id 의 항목이 이미 있습니다: " + id);
    const it = 항목읽기(env, f, id, m);
    it.묶음 = baseId;
    items.push(it);
  }

  const yText = (f.amountYear || "").trim();
  const year = yText.length === 0 ? D.year(env.Today) : 연도(yText);
  const source = 글자(f.source, "출처", 60, false);
  const amounts = [];
  for (const it of items) {
    const raw = (f["amt_" + String(it.월).padStart(2, "0")] || "").trim();
    if (raw.length === 0 || !납부있음(it)) continue;
    amounts.push(금액기록(env, year, it.Id, 금액(raw, it.월 + "월 금액"), source));
  }

  env.Db.upsertItems(items);
  if (amounts.length > 0) env.Db.upsertAmounts(amounts, 출처);
  return { ok: true, group: baseId, ids: items.map((it) => it.Id), amounts: amounts.length, popup: false };
}

function 금액기록(env, year, id, amount, source) {
  return { 연도: year, Id: id, 금액: amount, 출처: source.length === 0 ? "웹 입력" : source, 확인일: env.Today, 비고: "" };
}

function deleteItem(env, f) {
  const it = 항목찾기(env, f.id);
  env.Db.deleteItem(it.Id);
  return { ok: true };
}

function moveItem(env, f) {
  const it = 항목찾기(env, f.id);
  if (f.to != null && f.to !== "") {
    if (!/^\d+$/.test(String(f.to))) throw new HttpError(400, "to 는 0 이상의 숫자여야 합니다.");
    return { ok: true, moved: env.Db.moveItemTo(it.Id, Number(f.to)) };
  }
  const dir = f.dir;
  if (dir !== "up" && dir !== "down") throw new HttpError(400, "dir 은 up 또는 down 이어야 합니다.");
  return { ok: true, moved: env.Db.moveItem(it.Id, dir === "up" ? -1 : 1) };
}

// ── 묶음 금액 ──────────────────────────────────────────────
function saveGroupAmounts(env, f) {
  const Api = require("./api");
  const y = 연도(f.y);
  const g = String(f.group || "").trim();
  if (g.length === 0) throw new HttpError(400, "묶음 이름이 없습니다.");
  const all = env.Master.filter((x) => x.묶음 === g);
  if (all.length === 0) throw new HttpError(404, "그런 분할납부 묶음이 없습니다: " + g);
  const items = all.filter((x) => 해당연도(x, y)).sort((a, b) => (a.월 !== b.월 ? a.월 - b.월 : a.Id < b.Id ? -1 : 1));
  if (items.length === 0) throw new HttpError(404, "이 묶음은 " + y + "년에 회차가 없습니다: " + g);
  const source = 글자(f.source, "출처", 60, false);
  const records = [];
  for (const it of items) {
    const raw = (f["amt_" + it.Id] || "").trim();
    if (raw.length === 0 || !납부있음(it)) continue;
    records.push(금액기록(env, y, it.Id, 금액(raw, it.월 + "월 금액"), source));
  }
  if (records.length === 0) throw new HttpError(400, "넣은 금액이 없습니다.");
  env.Db.upsertAmounts(records, 출처);
  for (const r of records) env.Amounts[r.연도 + "\t" + r.Id] = r;
  const out = Api.group(env, y, items[0].묶음);
  out.saved = records.length;
  return out;
}

// ── 설정 ──────────────────────────────────────────────────
function setStartDate(env, f) {
  const s = (f.date || "").trim();
  let d = null;
  if (s.length > 0) {
    const v = D.parse(s);
    if (!v || !/^\d{4}-\d{2}-\d{2}$/.test(s) || D.year(v) < 2000 || D.year(v) > 2100)
      throw new HttpError(400, "날짜 형식이 올바르지 않습니다 (예: 2026-09-01).");
    d = v;
  }
  env.Db.setStartDate(d);
  env.Db.addEvent(D.year(env.Today), "-", "설정", 출처, "추적 시작일 " + (d ? s : "없음"));
  return { ok: true, startDate: d ? s : null };
}

/** "2026-09-23 17:40:12" 꼴의 지금 시각. */
function 지금글() {
  const n = new Date(); const p = (x) => String(x).padStart(2, "0");
  return n.getFullYear() + "-" + p(n.getMonth() + 1) + "-" + p(n.getDate()) + " " +
    p(n.getHours()) + ":" + p(n.getMinutes()) + ":" + p(n.getSeconds());
}

/** 지울 차입건의 회차 id 들. */
function 회차ids(env, 묶음) {
  return env.Db.all("SELECT id FROM items WHERE 묶음=?", 묶음).map((r) => r.id);
}

/**
 * 차입건 하나를 통째로 지운다 (사용자 결정 2026-09-23, ㄷ안).
 * 기본은 다른 항목 삭제와 같게 금액·진행·변경기록을 남긴다 — 실수로 지웠다가 다시 가져오면 이어지도록.
 * purge 가 켜져 있을 때만 그것까지 지운다. 회차에 직접 붙인 증빙은 어느 쪽이든 남긴다.
 * 차입 원본 스케줄 파일은 늘 함께 지운다 (차입건이 없어지면 쓸 곳이 없다).
 */
function deleteLoan(env, dataDir, baseDir, f) {
  const LD = require("./loan-doc");
  const 묶음 = String(f.group || "").trim();
  if (!묶음) throw new HttpError(400, "차입건을 고르지 않았습니다.");
  const 차입 = env.Db.차입목록().find((x) => x.묶음 === 묶음);
  if (!차입) throw new HttpError(404, "그런 차입건이 없습니다: " + 묶음);
  const 기록까지 = f.purge === "1" || f.purge === "true";

  const ids = 회차ids(env, 묶음);
  // 지울 파일 경로를 트랜잭션 전에 읽어 둔다 — 행을 지우고 나면 어디 있었는지 알 수 없다.
  const 파일들 = LD.원본경로들(env.Db, dataDir, 묶음);

  // 되돌릴 수단을 먼저 만든다. 실패해도 삭제는 진행한다 (사용자가 이미 확인했다).
  let 백업 = null;
  try { 백업 = SW.backupNow(env.Db, baseDir, dataDir).name; } catch { }

  let 지운금액 = 0, 지운진행 = 0, 지운기록 = 0;
  env.Db.tx(() => {
    env.Db.run("DELETE FROM attachments WHERE id=? AND 종류=?", 묶음, LD.종류);
    env.Db.run("DELETE FROM items WHERE 묶음=?", 묶음);
    if (기록까지 && ids.length) {
      const q = ids.map(() => "?").join(",");
      지운금액 = Number(env.Db.run(`DELETE FROM amounts WHERE id IN (${q})`, ...ids).changes || 0);
      지운진행 = Number(env.Db.run(`DELETE FROM status WHERE id IN (${q})`, ...ids).changes || 0);
      지운기록 = Number(env.Db.run(`DELETE FROM events WHERE id IN (${q})`, ...ids).changes || 0);
    }
    env.Db.run("DELETE FROM loans WHERE 묶음=?", 묶음);
    // 무엇을 지웠는지는 남긴다 — 차입건 자체가 사라져도 흔적은 있어야 한다.
    env.Db.기록(D.year(차입.차입일), 묶음, "차입삭제", null, null, "웹",
      (차입.차입명 || 묶음) + " · 회차 " + ids.length + "건" + (기록까지 ? " · 기록까지" : ""));
    env.Db.setMeta("master_updated_at", 지금글());
  });

  // DB 가 먼저 정리된 뒤에만 파일을 지운다. 반대로 하면 되돌려졌을 때 파일만 사라진다.
  let 지운파일 = 0;
  for (const p of 파일들) {
    try { if (fs.existsSync(p)) { fs.unlinkSync(p); 지운파일++; } } catch { }
  }
  try { fs.rmdirSync(LD.차입폴더(dataDir, 묶음)); } catch { }

  return {
    ok: true, group: 묶음, name: 차입.차입명 || 묶음,
    items: ids.length, docs: 지운파일,
    amounts: 지운금액, status: 지운진행, events: 지운기록,
    purged: 기록까지, backup: 백업,
  };
}

module.exports = { changeStage, saveAmount, deleteAmount, saveItem, deleteItem, moveItem, saveGroupAmounts, setStartDate, deleteLoan };
