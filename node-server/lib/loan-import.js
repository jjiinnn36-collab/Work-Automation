"use strict";
// src/WebServer.Loans.cs 의 차입가져오기 를 옮긴 것. 미리보기(preview)와 저장(save) 두 가지.
const D = require("./dates");
const SR = require("./sheetreader");
const LS = require("./loanschedule");
const { Store } = require("./store");
const LD = require("./loan-doc");
const { HttpError, 연도 } = require("./api");

const 출처 = "웹";
const 차입출처 = "ERP 차입스케줄";
const 차입알림영업일 = 3;

function 파일이름(s) {
  let n = String(s).replace(/\//g, "\\");
  const slash = n.lastIndexOf("\\");
  if (slash >= 0) n = n.slice(slash + 1);
  let r = n.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").trim().replace(/\.+$/, "");
  if (r.length === 0) throw new HttpError(400, "파일 이름이 올바르지 않습니다.");
  if (r.length > 120) r = r.slice(r.length - 120);
  return r;
}

function 같은거래처(a, b) {
  return (a || "").replace(/ /g, "").toLowerCase() === (b || "").replace(/ /g, "").toLowerCase();
}

function 자르기(s, max) { s = s || ""; return s.length <= max ? s : s.slice(0, max); }

function N0(n) {
  const neg = n < 0;
  const s = Math.abs(Math.round(n)).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return (neg ? "-" : "") + s;
}

function 회차아이디(묶음, p) {
  return 묶음 + "-" + D.year(p.지급일) + String(D.month(p.지급일)).padStart(2, "0");
}

function 회차수(env, 묶음, 기준) {
  // 기준이 null 이면 전체. 아니면 그 스케줄 첫 지급일보다 앞선 회차만.
  let n = 0;
  for (const it of env.Db.loadMaster()) {
    if (it.묶음 !== 묶음) continue;
    if (기준 == null || (it.시작연도 != null && require("./model").원기한일(it, it.시작연도).getTime() < 기준.getTime())) n++;
  }
  return n;
}

function 차입회차(plan, p, 묶음) {
  const 지급일 = p.지급일;
  const it = {
    Id: 회차아이디(묶음, p),
    기관: 자르기(Store.차입표시이름(plan.약칭, plan.차입명, plan.거래처), 60),
    차입: true,
    비용명: "차입금 이자",
    진행흐름: "납부만",
    월: D.month(지급일),
    말일: false, 일: D.day(지급일),
    알림영업일: 차입알림영업일,
    금액규칙: "변동", 고정금액: null,
    시작연도: D.year(지급일), 종료연도: D.year(지급일),
    묶음,
    홈페이지명: "", 홈페이지주소: "",
    사용자단계: null, 사용자행동: null, 금액없음: false,
    비고: "이자기간 " + D.ymd(p.기간시작) + "~" + D.ymd(D.addDays(지급일, -1)),
  };
  // 2월 29일은 그 해에만 있으므로 말일로 둔다.
  if (D.month(지급일) === 2 && D.day(지급일) === D.daysInMonth(D.year(지급일), 2) && D.day(지급일) === 29) {
    it.말일 = true; it.일 = 0;
  }
  return it;
}

function 근거(p) {
  const parts = p.행.map((r) => D.month(r.날짜) + "/" + D.day(r.날짜) + " " + N0(r.금액));
  return 자르기(parts.join(" + "), 200);
}

function 차입Dto(env, plan, 묶음, 앞회차) {
  const 있는항목 = new Map();
  let 금액들 = env.Amounts;
  if (묶음 != null) {
    for (const it of env.Db.loadMaster()) if (it.묶음 === 묶음) 있는항목.set(it.Id, it);
    금액들 = env.Db.loadAmounts();
  }

  const rows = [];
  let 새로 = 0, 있음 = 0, 다름 = 0;
  for (const p of plan.지급) {
    const id = 묶음 == null ? null : 회차아이디(묶음, p);
    let state = "new";
    let 기존금액 = null;
    if (id != null && 있는항목.has(id)) {
      const a = 금액들[D.year(p.지급일) + "\t" + id];
      if (a) 기존금액 = a.금액;
      state = 기존금액 != null && 기존금액 !== p.금액 ? "diff" : "exists";
    }
    if (state === "new") 새로++; else if (state === "exists") 있음++; else 다름++;

    const 실제 = env.Cal.nextBusinessDayOrSame(p.지급일);
    rows.push({
      no: p.회차 + 앞회차,
      id,
      from: D.ymd(p.기간시작),
      scheduled: D.ymd(p.지급일),
      payDue: D.ymd(실제),
      shifted: 실제.getTime() !== p.지급일.getTime(),
      amount: p.금액,
      unexpected: p.예상과다름,
      rows: p.행.map((r) => ({ date: D.ymd(r.날짜), amount: r.금액 })),
      state,
      existingAmount: 기존금액,
    });
  }

  return {
    sheet: plan.시트, ok: true, org: plan.거래처, name: plan.차입명, short: plan.약칭,
    start: D.ymd(plan.차입일), face: plan.액면, rate: plan.이율,
    maturity: plan.만기 ? D.ymd(plan.만기) : null,
    interestTotal: plan.이자합계, quarterExpected: LS.분기예상(plan),
    known: 묶음 != null, payments: rows,
    newCount: 새로, existingCount: 있음, diffCount: 다름, warnings: plan.경고,
  };
}

function 확인줄(label, value, level) { return { label, value, level }; }

function 연장확인(plan, 대상) {
  const list = [];
  list.push(확인줄("거래처", plan.거래처, "ok"));
  list.push(확인줄("액면", N0(plan.액면) + (plan.액면 === 대상.액면 ? "" : " (지금 " + N0(대상.액면) + ")"), plan.액면 === 대상.액면 ? "ok" : "warn"));
  if (대상.만기) {
    const 이어짐 = Math.abs((plan.차입일.getTime() - 대상.만기.getTime()) / 86400000) <= 7;
    list.push(확인줄("새 스케줄 시작", D.ymd(plan.차입일) + (이어짐 ? " (지금 만기에서 이어짐)" : " (지금 만기 " + D.ymd(대상.만기) + "과 떨어짐)"), 이어짐 ? "ok" : "warn"));
  }
  if (plan.이율 != null) {
    const 같음 = 대상.이율 != null && 대상.이율 === plan.이율;
    list.push(확인줄("이율", (같음 || 대상.이율 == null ? "" : 대상.이율 + "% → ") + plan.이율 + "%", 같음 ? "ok" : "warn"));
  }
  list.push(확인줄("새 만기", plan.만기 ? D.ymd(plan.만기) : "—", "ok"));
  return list;
}

function importLoans(env, sp, rawName, buf, dataDir) {
  const mode = sp.get("mode") || "preview";
  if (mode !== "preview" && mode !== "save") throw new HttpError(400, "mode 는 preview 또는 save 여야 합니다.");
  if (!rawName) throw new HttpError(400, "파일 이름이 없습니다.");
  let name;
  try { name = 파일이름(decodeURIComponent(rawName)); }
  catch (e) { if (e instanceof HttpError) throw e; throw new HttpError(400, "파일 이름이 올바르지 않습니다."); }

  if (!buf || buf.length === 0) throw new HttpError(400, "빈 파일입니다.");
  if (buf.length > SR.최대크기) throw new HttpError(413, "20MB 보다 큰 파일은 읽지 않습니다.");

  let sheets;
  try { sheets = SR.read(buf, name); }
  catch (e) { if (e instanceof SR.읽기오류) throw new HttpError(400, e.message); throw e; }

  let only = null;
  const onlyRaw = sp.get("only");
  if (onlyRaw) { only = new Set(); for (const s of onlyRaw.split(",")) if (s.trim()) only.add(s.trim()); }

  const 고친금액 = 고친금액읽기(sp);
  const 이름들 = 차입이름읽기(sp);

  let 대상 = null;
  if (sp.get("extend") != null) {
    const g = sp.get("extend").trim();
    for (const x of env.Db.차입목록()) if (x.묶음 === g) 대상 = x;
    if (!대상) throw new HttpError(404, "그런 차입건이 없습니다: " + g);
  }

  const loans = [], skipped = [], created = [];
  let 새회차 = 0, 있던회차 = 0;
  const 할일 = [];

  for (const sheet of sheets) {
    if (!LS.양식인가(sheet)) {
      let 비었음 = true;
      for (const row of sheet.행) for (const c of row) if (c && c.trim().length > 0) { 비었음 = false; break; }
      skipped.push({ sheet: sheet.이름, reason: 비었음 ? "빈 시트" : "차입 스케줄 양식이 아님 (기준일자·현금흐름구분·액면이자금액 열이 없음)" });
      continue;
    }
    let plan;
    try { plan = LS.parse(sheet); }
    catch (e) { if (e instanceof LS.형식오류) { loans.push({ sheet: sheet.이름, ok: false, error: e.message }); continue; } throw e; }

    let 묶음, 앞회차 = 0, checks = null;
    if (대상) {
      if (!같은거래처(plan.거래처, 대상.거래처)) {
        skipped.push({ sheet: sheet.이름, reason: "다른 거래처 (" + plan.거래처 + ") — " + 대상.거래처 + " 차입건에 붙일 수 없음" });
        continue;
      }
      묶음 = 대상.묶음;
      앞회차 = 회차수(env, 묶음, plan.지급[0].지급일);
      checks = 연장확인(plan, 대상);
    } else {
      묶음 = null;   // 차입명이 정해진 뒤(아래) 차입명 기준으로 기존 건을 찾는다.
    }

    할일.push({ Sheet: sheet, Plan: plan, 묶음, 앞회차, Checks: checks, 선택됨: only == null || only.has(sheet.이름) });
  }

  // 이름을 먼저 정하고, 그 다음 차입명 기준으로 기존 차입건(같은 거래처·차입일·차입명)을 찾는다.
  // 거래처만 같은 다른 사업은 새 건으로 들어간다 (사용자 요청 2026-09-22).
  for (const job of 할일) {
    if (대상) { job.Plan.차입명 = 대상.차입명; job.Plan.약칭 = 대상.약칭; continue; }
    const nm = 이름들[job.Sheet.이름];
    if (nm) { job.Plan.차입명 = nm[0]; job.Plan.약칭 = nm[1]; }
    // 차입명이 비면 시트명을 차입명으로 쓴다 (사용자 요청 2026-09-22).
    if (!job.Plan.차입명 || job.Plan.차입명.trim().length === 0) job.Plan.차입명 = (job.Sheet.이름 || "").trim();
    job.묶음 = env.Db.차입묶음이름(job.Plan.거래처, job.Plan.차입일, job.Plan.차입명);
    // 이름을 안 보냈으면 시트로도 되찾는다 — 이름을 바꿔 둔 차입건에 같은 파일을 다시 올렸을 때
    // 새 차입건이 하나 더 생기면 안 된다. 되찾았으면 그 차입건의 이름을 그대로 잇는다.
    if (job.묶음 == null && !nm) {
      job.묶음 = env.Db.차입묶음시트(job.Plan.거래처, job.Plan.차입일, job.Sheet.이름);
      if (job.묶음 != null) {
        for (const 있던 of env.Db.차입목록())
          if (있던.묶음 === job.묶음) { job.Plan.차입명 = 있던.차입명; job.Plan.약칭 = 있던.약칭; }
      }
    }
  }

  for (const job of 할일) {
    const { Sheet: sheet, Plan: plan, 앞회차 } = job;
    let 묶음 = job.묶음;
    const 선택됨 = job.선택됨;
    let dto = 차입Dto(env, plan, 묶음, 앞회차);

    if (mode === "save" && 선택됨) {
      if (묶음 == null) 묶음 = env.Db.새항목아이디();
      const items = [];
      const amounts = {};
      for (const p of plan.지급) {
        const it = 차입회차(plan, p, 묶음);
        items.push(it);
        const key = 고친금액키(sheet.이름, p.지급일);
        const 고침 = Object.prototype.hasOwnProperty.call(고친금액, key);
        const 고친값 = 고침 ? 고친금액[key] : null;
        amounts[it.Id] = {
          연도: D.year(p.지급일), Id: it.Id,
          금액: 고침 ? 고친값 : p.금액,
          출처: 고침 && 고친값 !== p.금액 ? 차입출처 + " (고침)" : 차입출처,
          확인일: env.Today, 비고: 근거(p),
        };
      }
      const added = env.Db.차입가져오기(묶음, plan, items, amounts, name, 출처, 대상 != null ? "연장" : "가져오기");
      for (const it of items) if (added.includes(it.Id)) created.push(it);
      새회차 += added.length;
      있던회차 += items.length - added.length;
      dto = 차입Dto(env, plan, 묶음, 앞회차);
      dto.group = 묶음; dto.added = added.length;
      // 이 사업건 시트만 떼어 원본 스케줄로 보관한다 — 차입건에 한 부, 직전과 같은 내용이면 새로 만들지 않는다.
      dto.doc = LD.원본보관(env, dataDir, 묶음, sheet, plan, new Date());
    }
    if (job.Checks) dto.checks = job.Checks;
    dto.selected = 선택됨;
    loans.push(dto);
  }

  const result = { file: name, mode, loans, skipped };
  if (대상) {
    result.extend = {
      group: 대상.묶음, org: 대상.표시이름,
      maturity: 대상.만기 ? D.ymd(대상.만기) : null,
      count: 회차수(env, 대상.묶음, null),
    };
  }
  if (mode === "save") { result.added = 새회차; result.existing = 있던회차; result.popup = false; }
  return result;
}

// ov=날짜|금액|시트 (회차마다). 화면에서 고친 지급액.
function 고친금액읽기(sp) {
  const out = {};
  const vals = sp.getAll("ov");
  if (vals.length > 500) throw new HttpError(400, "고친 금액 형식이 올바르지 않습니다.");
  for (const v of vals) {
    const parts = (v || "").split("|");
    const first3 = [parts[0], parts[1], parts.slice(2).join("|")];
    if (parts.length < 3 || !/^\d{4}-\d{2}-\d{2}$/.test(first3[0]))
      throw new HttpError(400, "고친 금액 형식이 올바르지 않습니다.");
    const 금액 = Number(String(first3[1]).replace(/,/g, "").replace(/원/g, "").replace(/ /g, "").trim());
    if (!Number.isFinite(금액) || 금액 !== Math.trunc(금액)) throw new HttpError(400, "지급액은 원 단위로 넣어 주세요.");
    out[고친금액키(first3[2], first3[0])] = 금액;
  }
  return out;
}
function 고친금액키(sheet, 지급일) { return (sheet || "") + "\n" + (지급일 instanceof Date ? D.ymd(지급일) : 지급일); }

// nm=시트|차입명|약칭 (시트마다).
function 차입이름읽기(sp) {
  const out = {};
  const vals = sp.getAll("nm");
  if (vals.length > 500) throw new HttpError(400, "차입명 형식이 올바르지 않습니다.");
  for (const v of vals) {
    const parts = (v || "").split("|");
    if (parts.length < 3) throw new HttpError(400, "차입명 형식이 올바르지 않습니다.");
    const 이름 = parts[1].trim(), 약칭 = parts.slice(2).join("|").trim();
    if (이름.length > 60) throw new HttpError(400, "차입명은 60자까지입니다.");
    if (약칭.length > 20) throw new HttpError(400, "약칭은 20자까지입니다.");
    if (/[\r\n\t]/.test(이름 + 약칭)) throw new HttpError(400, "차입명에 줄바꿈은 쓸 수 없습니다.");
    out[parts[0]] = [이름, 약칭];
  }
  return out;
}

module.exports = { importLoans };
