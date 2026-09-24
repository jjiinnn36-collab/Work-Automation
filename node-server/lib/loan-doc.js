"use strict";
// 차입 스케줄을 올릴 때, 그 사업건 시트만 떼어 CSV 로 증빙에 보관한다 (사용자 요청 2026-09-23).
// 회차마다 복사하지 않고 차입건(묶음) 하나에 한 부만 둔다 — 회차가 수십 개라 복사하면 낭비다.
// 파일은 자료폴더/증빙/차입/{묶음}/ 아래에 둔다 (증빙 뿌리 안이라 기존 열기·내려받기가 그대로 쓰인다).
const fs = require("node:fs");
const path = require("node:path");

const 종류 = "차입원본";

function Safe(s) {
  if (!s) return "_";
  const r = String(s).replace(/[<>:"/\|?*\x00-\x1f]/g, "_").trim();
  return r.length === 0 ? "_" : r;
}

/** 엑셀이 수식으로 읽지 않게 막고, 쉼표·따옴표·줄바꿈이 있으면 감싼다 (AC-W16 과 같은 규칙). */
function 칸(v) {
  let s = v == null ? "" : String(v);
  if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;
  if (/[",\r\n]/.test(s)) s = '"' + s.replace(/"/g, '""') + '"';
  return s;
}

/** 시트 한 장을 CSV 글자로. 엑셀이 한글을 바로 읽도록 BOM 을 붙인다. */
function csv만들기(sheet) {
  const lines = [];
  for (const row of sheet.행) lines.push(row.map(칸).join(","));
  return "\uFEFF" + lines.join("\r\n") + "\r\n";
}

function 증빙루트(dataDir) { return path.join(dataDir, "증빙"); }
function 차입폴더(dataDir, 묶음) { return path.join(증빙루트(dataDir), "차입", Safe(묶음)); }

function 두자리(x) { return String(x).padStart(2, "0"); }
function 도장(now) {
  return "" + now.getFullYear() + 두자리(now.getMonth() + 1) + 두자리(now.getDate()) +
    "-" + 두자리(now.getHours()) + 두자리(now.getMinutes()) + 두자리(now.getSeconds());
}
function 일시글(now) {
  return "" + now.getFullYear() + "-" + 두자리(now.getMonth() + 1) + "-" + 두자리(now.getDate()) +
    " " + 두자리(now.getHours()) + ":" + 두자리(now.getMinutes()) + ":" + 두자리(now.getSeconds());
}

/**
 * 이 차입건의 원본 스케줄을 보관한다.
 * 가장 최근 것과 내용이 똑같으면 새로 만들지 않는다 — 같은 파일을 여러 번 올려도 하나만 남는다.
 * 보관에 실패해도 가져오기 자체는 살린다 (스케줄 자료가 더 중요하다).
 * @returns "새로" | "같음" | "실패"
 */
function 원본보관(env, dataDir, 묶음, sheet, plan, now) {
  try {
    const buf = Buffer.from(csv만들기(sheet), "utf8");

    // 직전 것과 내용이 같으면 그대로 둔다.
    const 있던 = env.Db.all(
      "SELECT 저장파일 FROM attachments WHERE id=? AND 종류=? ORDER BY rid DESC LIMIT 1", 묶음, 종류);
    if (있던.length) {
      try {
        if (fs.readFileSync(path.join(증빙루트(dataDir), 있던[0].저장파일)).equals(buf)) return "같음";
      } catch { /* 이전 파일이 없어졌으면 새로 만든다 */ }
    }

    const folder = 차입폴더(dataDir, 묶음);
    fs.mkdirSync(folder, { recursive: true });
    const 보일이름 = Safe(plan.차입명 || sheet.이름 || "차입 스케줄") + ".csv";
    let target = path.join(folder, 도장(now) + "_" + 보일이름);
    let n = 2;
    while (fs.existsSync(target)) { target = path.join(folder, 도장(now) + "_" + n + "_" + 보일이름); n++; }
    fs.writeFileSync(target, buf);

    const rel = path.relative(증빙루트(dataDir), target);
    try {
      env.Db.run("INSERT INTO attachments(연도,id,단계,저장파일,원본파일명,첨부일시,종류) VALUES(?,?,?,?,?,?,?)",
        plan.차입일.getFullYear(), 묶음, "", rel, 보일이름, 일시글(now), 종류);
    } catch (e) {
      try { fs.unlinkSync(target); } catch { }
      throw e;
    }
    return "새로";
  } catch {
    return "실패";
  }
}

/** 이 차입건의 원본 스케줄 목록 (최신 먼저). 회차 어느 곳에서 열어도 같은 목록이 보인다. */
function 원본목록(db, 묶음) {
  if (!묶음) return [];
  return db.all(
    "SELECT 저장파일,원본파일명,첨부일시,연도 FROM attachments WHERE id=? AND 종류=? ORDER BY rid DESC",
    묶음, 종류).map((a) => ({
      file: a.저장파일,
      name: a.원본파일명,
      year: a.연도,
      at: a.첨부일시 ? String(a.첨부일시).replace("T", " ").slice(0, 16) : "",
    }));
}

/** 이 차입건의 원본 스케줄 파일들의 실제 경로 (지울 때 쓴다). */
function 원본경로들(db, dataDir, 묶음) {
  return db.all("SELECT 저장파일 FROM attachments WHERE id=? AND 종류=?", 묶음, 종류)
    .map((a) => path.join(증빙루트(dataDir), a.저장파일));
}

module.exports = { 원본보관, 원본목록, 원본경로들, csv만들기, 차입폴더, 증빙루트, 종류 };
