"use strict";
// src/LoanSchedule.cs 를 옮긴 것. ERP 지급스케줄을 분기 지급으로 묶는다.
const D = require("./dates");

const 구분신규 = "신규", 구분이자 = "이자지급", 구분상환 = "상환";
const 지급일허용일 = 7;

class 형식오류 extends Error {}

function 양식인가(sheet) { return 머리글찾기(sheet) >= 0; }

function 머리글찾기(sheet) {
  for (let r = 0; r < sheet.행.length && r < 15; r++) {
    const row = sheet.행[r];
    if (열(row, "기준일자") >= 0 && 열(row, "현금흐름구분") >= 0 && 열(row, "액면이자금액") >= 0) return r;
  }
  return -1;
}

function 열(row, name) {
  for (let i = 0; i < row.length; i++) if (정리(row[i]) === name) return i;
  return -1;
}

function 정리(s) { return (s || "").replace(/ /g, "").replace(/ /g, "").trim(); }
function 칸(row, i) { return i >= 0 && i < row.length ? (row[i] || "").trim() : ""; }

function ymd(d) { return D.ymd(d); }

function 분기예상(plan) {
  return plan.이율 != null ? Math.round(plan.액면 * plan.이율 / 100 / 4) : null;
}

function parse(sheet) {
  const h = 머리글찾기(sheet);
  if (h < 0) throw new 형식오류("차입 스케줄 양식이 아닙니다 (기준일자·현금흐름구분·액면이자금액 열이 없음).");
  const head = sheet.행[h];
  const c날짜 = 열(head, "기준일자"), c구분 = 열(head, "현금흐름구분"), c이자 = 열(head, "액면이자금액");
  const c액면 = 열(head, "액면금액"), c이율 = 열(head, "액면이자율"), c거래처 = 열(head, "거래처명");

  const plan = { 시트: sheet.이름 || "", 거래처: "", 차입명: "", 약칭: "", 차입일: null, 액면: 0, 이율: null, 만기: null, 이자합계: 0, 지급: [], 경고: [] };
  let 신규있음 = false;
  let 합계행 = null;
  const rows = [];

  for (let r = h + 1; r < sheet.행.length; r++) {
    const row = sheet.행[r];
    const 구분 = 정리(칸(row, c구분));
    const 날짜글 = 칸(row, c날짜);
    const 줄 = r + 1;

    if (구분.length === 0 && 날짜글.length === 0) {
      const t = 금액읽기(칸(row, c이자));
      if (t.ok && t.v > 0) 합계행 = t.v;
      continue;
    }

    const dr = 날짜읽기(날짜글);
    if (!dr.ok) throw new 형식오류(줄 + "행: 기준일자 '" + 날짜글 + "' 를 날짜로 읽을 수 없습니다.");
    const d = dr.d;

    if (plan.거래처.length === 0) plan.거래처 = 칸(row, c거래처);

    if (구분 === 구분신규) {
      if (신규있음) throw new 형식오류(줄 + "행: '신규' 행이 두 번 있습니다. 차입건마다 시트를 나눠 주세요.");
      신규있음 = true;
      plan.차입일 = d;
      const face = 금액읽기(칸(row, c액면));
      if (!face.ok || face.v <= 0) throw new 형식오류(줄 + "행: 액면금액을 읽을 수 없습니다.");
      plan.액면 = face.v;
      const rate = 비율읽기(칸(row, c이율));
      if (rate.ok) plan.이율 = rate.v;
    } else if (구분 === 구분이자) {
      const amt = 금액읽기(칸(row, c이자));
      if (!amt.ok) throw new 형식오류(줄 + "행: 액면이자금액을 읽을 수 없습니다.");
      if (amt.v < 0) throw new 형식오류(줄 + "행: 액면이자금액이 음수입니다.");
      rows.push({ 날짜: d, 금액: amt.v });
    } else if (구분 === 구분상환) {
      if (plan.만기 == null) plan.만기 = d;
    } else {
      plan.경고.push(줄 + "행: 모르는 현금흐름구분 '" + 구분 + "' 은 건너뜁니다.");
    }
  }

  if (!신규있음) throw new 형식오류("'신규' 행(차입일·액면금액)이 없습니다.");
  if (rows.length === 0) throw new 형식오류("'이자지급' 행이 없습니다.");
  if (plan.거래처.length === 0) plan.거래처 = plan.시트;

  rows.sort((a, b) => a.날짜.getTime() - b.날짜.getTime());
  if (rows[0].날짜.getTime() < plan.차입일.getTime())
    throw new 형식오류("이자 행(" + ymd(rows[0].날짜) + ")이 차입일(" + ymd(plan.차입일) + ") 이전입니다.");
  for (const x of rows) plan.이자합계 += x.금액;

  분기로묶기(plan, rows);

  if (합계행 != null && 합계행 !== plan.이자합계)
    plan.경고.push(`합계행의 이자(${N0(합계행)})와 이자 행의 합(${N0(plan.이자합계)})이 다릅니다.`);
  return plan;
}

function 분기로묶기(plan, rows) {
  const 마지막행 = rows[rows.length - 1].날짜;
  let 직전 = plan.차입일;
  let 하한 = D.addDays(plan.차입일, -1);
  let used = 0;
  const 예상 = 분기예상(plan);

  for (let k = 1; ; k++) {
    const 기준 = D.make(D.year(plan.차입일), D.month(plan.차입일) + 3 * k, D.day(plan.차입일));
    // addMonths 는 말일 보정이 필요하다 — JS Date 는 넘치면 다음 달로 가므로 아래에서 맞춘다.
    const 기준d = addMonths(plan.차입일, 3 * k);
    if (기준d.getTime() > D.addDays(마지막행, 지급일허용일).getTime()) break;

    let 지급행 = null;
    for (const x of rows) {
      if (x.날짜.getTime() <= 직전.getTime()) continue;
      if (Math.abs((x.날짜.getTime() - 기준d.getTime()) / 86400000) > 지급일허용일) continue;
      if (지급행 == null || 더나은지급행(x, 지급행, 기준d)) 지급행 = x;
    }
    if (지급행 == null) throw new 형식오류(k + "회차 지급일(" + ymd(기준d) + " 무렵)의 이자 행을 찾지 못했습니다.");

    const p = { 회차: k, 지급일: 지급행.날짜, 기간시작: 직전, 금액: 0, 행: [], 예상과다름: false };
    for (const x of rows) {
      if (x.날짜.getTime() <= 하한.getTime() || x.날짜.getTime() > 지급행.날짜.getTime()) continue;
      p.행.push(x); p.금액 += x.금액; used++;
    }
    if (예상 != null && Math.abs(p.금액 - 예상) > Math.max(10, 예상 * 0.02)) {
      p.예상과다름 = true;
      plan.경고.push(`${k}회차 이자 ${N0(p.금액)}원이 액면×이율÷4(${N0(예상)}원)와 다릅니다. 스케줄을 확인하세요.`);
    }
    plan.지급.push(p);
    직전 = 지급행.날짜;
    하한 = 지급행.날짜;
  }

  if (used !== rows.length)
    throw new 형식오류(`마지막 지급일(${ymd(직전)}) 뒤에 이자 행이 ${rows.length - used}개 남습니다. 분기 지급일과 맞지 않는 스케줄입니다.`);
}

// C# DateTime.AddMonths 와 같은 말일 보정: 넘치는 날은 그 달 말일로 자른다.
function addMonths(d, months) {
  const y = D.year(d);
  const m0 = D.month(d) - 1 + months;
  const ny = y + Math.floor(m0 / 12);
  const nm = ((m0 % 12) + 12) % 12 + 1;
  const day = Math.min(D.day(d), D.daysInMonth(ny, nm));
  return D.make(ny, nm, day);
}

function 더나은지급행(a, b, 기준) {
  const aEnd = 월말(a.날짜), bEnd = 월말(b.날짜);
  if (aEnd !== bEnd) return !aEnd;
  return Math.abs((a.날짜.getTime() - 기준.getTime())) < Math.abs((b.날짜.getTime() - 기준.getTime()));
}

function 월말(d) { return D.day(d) === D.daysInMonth(D.year(d), D.month(d)); }

function N0(n) {
  const neg = n < 0;
  const s = Math.abs(Math.round(n)).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return (neg ? "-" : "") + s;
}

// ── 값 읽기 ──
const 날짜형식 = [
  /^(\d{4})-(\d{1,2})-(\d{1,2})$/, /^(\d{4})(\d{2})(\d{2})$/, /^(\d{4})\.(\d{1,2})\.(\d{1,2})$/, /^(\d{4})\/(\d{1,2})\/(\d{1,2})$/,
];

function 날짜읽기(s) {
  let t = (s || "").trim();
  const sp = t.indexOf(" ");
  if (t.length >= 10 && sp > 0) t = t.slice(0, sp);
  for (const rx of 날짜형식) {
    const m = rx.exec(t);
    if (m) {
      const y = +m[1], mo = +m[2], da = +m[3];
      if (mo >= 1 && mo <= 12 && da >= 1 && da <= 31) return { ok: true, d: D.make(y, mo, da) };
    }
  }
  // 엑셀 날짜 일련번호(1900 체계).
  if (t.length <= 12 && /^-?\d+(\.\d+)?$/.test(t)) {
    const serial = Number(t);
    if (serial >= 20000 && serial < 80000) {
      // OADate: 1899-12-30 기준.
      const base = Date.UTC(1899, 11, 30);
      const d = new Date(base + Math.floor(serial) * 86400000);
      return { ok: true, d: D.make(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate()) };
    }
  }
  return { ok: false, d: null };
}

function 금액읽기(s) {
  const t = (s || "").replace(/,/g, "").replace(/원/g, "").replace(/ /g, "").trim();
  if (t.length === 0) return { ok: false, v: 0 };
  if (!/^-?\d+(\.\d+)?$/.test(t)) return { ok: false, v: 0 };
  const v = Number(t);
  if (v !== Math.trunc(v)) return { ok: false, v: 0 };
  return { ok: true, v };
}

function 비율읽기(s) {
  const t = (s || "").replace(/%/g, "").trim();
  const d = Number(t);
  if (!Number.isFinite(d) || d <= 0 || d >= 100) return { ok: false, v: 0 };
  return { ok: true, v: Math.round(d * 10000) / 10000 };
}

module.exports = { 양식인가, parse, 형식오류, 분기예상, 날짜읽기, 금액읽기 };
