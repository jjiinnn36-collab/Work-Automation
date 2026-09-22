"use strict";
// 증빙 파일 첨부·삭제·열기. src/Attachments.cs + WebServer 의 Attach/DeleteAttachment/OpenFile 를 옮긴 것.
// 파일은 자료폴더/증빙/{연도}/{안전한 id}/ 아래에 둔다 (윈도우 판과 같은 자리).
const fs = require("node:fs");
const path = require("node:path");
const { execFile } = require("node:child_process");
const D = require("./dates");
const { Stages } = require("./model");
const { HttpError, 연도, 아이디 } = require("./api");

const 첨부최대 = 50 * 1024 * 1024;
const 받은문서 = "받은문서";
const 증빙 = "증빙";

/** 파일 이름에 쓸 수 없는 문자를 걷어낸다 (윈도우·맥 공통으로 넉넉히 막는다). */
function Safe(s) {
  if (!s) return "_";
  const r = String(s).replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").trim();
  return r.length === 0 ? "_" : r;
}

function 파일이름(s) {
  let n = String(s).replace(/\//g, "\\");
  const slash = n.lastIndexOf("\\");
  if (slash >= 0) n = n.slice(slash + 1);
  let r = n.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").trim().replace(/\.+$/, "");
  if (r.length === 0) throw new HttpError(400, "파일 이름이 올바르지 않습니다.");
  if (r.length > 120) r = r.slice(r.length - 120);
  return r;
}

function 증빙루트(dataDir) { return path.join(dataDir, "증빙"); }
function 폴더(dataDir, year, id) { return path.join(증빙루트(dataDir), String(year), Safe(id)); }

function stamp() {
  const n = new Date(); const p = (x) => String(x).padStart(2, "0");
  return "" + n.getFullYear() + p(n.getMonth() + 1) + p(n.getDate()) + "-" + p(n.getHours()) + p(n.getMinutes()) + p(n.getSeconds());
}

/** 첨부: 본문이 파일 자체. env, 질의(q), 헤더의 파일이름, 버퍼를 받는다. */
function attach(env, dataDir, q, rawName, buf) {
  const y = 연도(q.y);
  const it = env.Item(아이디(q.id));
  if (!it) throw new HttpError(404, "항목을 찾을 수 없습니다: " + q.id);
  if (!require("./model").해당연도(it, y)) throw new HttpError(400, "이 항목은 " + y + "년에 기한이 없습니다 (유효연도 밖).");

  if (!rawName) throw new HttpError(400, "파일 이름이 없습니다.");
  let name;
  try { name = decodeURIComponent(rawName); } catch { throw new HttpError(400, "파일 이름이 올바르지 않습니다."); }
  name = 파일이름(name);

  if (!buf || buf.length <= 0) throw new HttpError(400, "빈 파일은 첨부할 수 없습니다.");
  if (buf.length > 첨부최대) throw new HttpError(413, "50MB 보다 큰 파일은 첨부할 수 없습니다.");

  const st = env.Status[y + "\t" + it.Id] || { 단계: 0 };
  const stages = Stages.For(it);
  const idx = Math.max(0, Math.min(st.단계, stages.length - 1));
  const 단계 = stages[idx];
  const 종류 = q.kind === 받은문서 ? 받은문서 : 증빙;

  const folder = 폴더(dataDir, y, it.Id);
  fs.mkdirSync(folder, { recursive: true });
  const s = stamp();
  let target = path.join(folder, `${Safe(단계)}_${s}_${Safe(name)}`);
  let n = 2;
  while (fs.existsSync(target)) { target = path.join(folder, `${Safe(단계)}_${s}_${n}_${Safe(name)}`); n++; }

  fs.writeFileSync(target, buf);
  const rel = path.relative(증빙루트(dataDir), target);

  const now = new Date(); const p = (x) => String(x).padStart(2, "0");
  const 첨부일시 = "" + now.getFullYear() + "-" + p(now.getMonth() + 1) + "-" + p(now.getDate()) + " " + p(now.getHours()) + ":" + p(now.getMinutes()) + ":" + p(now.getSeconds());

  try {
    env.Db.run("INSERT INTO attachments(연도,id,단계,저장파일,원본파일명,첨부일시,종류) VALUES(?,?,?,?,?,?,?)",
      y, it.Id, 단계, rel, name, 첨부일시, 종류);
    env.Db.addEvent(y, it.Id, 종류 === 받은문서 ? "문서첨부" : "첨부", "웹", name);
  } catch (e) {
    try { fs.unlinkSync(target); } catch { }
    throw e;
  }

  const cnt = env.Db.all("SELECT COUNT(*) n FROM attachments WHERE 연도=? AND id=?", y, it.Id)[0].n;
  return { ok: true, file: rel, name, count: Number(cnt) };
}

function deleteAttach(env, dataDir, f) {
  const y = 연도(f.y);
  const id = 아이디(f.id);
  const file = f.f;
  const rows = env.Db.all("SELECT 저장파일,원본파일명 FROM attachments WHERE 연도=? AND id=? AND 저장파일=?", y, id, file);
  if (rows.length === 0) throw new HttpError(404, "증빙을 찾을 수 없습니다.");
  env.Db.tx(() => {
    env.Db.run("DELETE FROM attachments WHERE 연도=? AND id=? AND 저장파일=?", y, id, file);
    env.Db.기록(y, id, "첨부삭제", null, null, "웹", rows[0].원본파일명 || "");
  });
  try {
    const full = 전체경로(dataDir, file);
    if (안에있는지(dataDir, full) && fs.existsSync(full)) fs.unlinkSync(full);
  } catch { }
  const cnt = env.Db.all("SELECT COUNT(*) n FROM attachments WHERE 연도=? AND id=?", y, id)[0].n;
  return { ok: true, count: Number(cnt) };
}

function 전체경로(dataDir, rel) {
  return path.isAbsolute(rel) ? rel : path.join(증빙루트(dataDir), rel);
}
function 안에있는지(dataDir, full) {
  const root = path.resolve(증빙루트(dataDir)).replace(/[\\/]+$/, "") + path.sep;
  return (path.resolve(full) + path.sep).toLowerCase().startsWith(root.toLowerCase());
}

/** 증빙 파일 하나를 내려보낸다 (GET /api/file). {full, name} 또는 예외. */
function 파일경로(env, dataDir, y, id, f) {
  const rows = env.Db.all("SELECT 저장파일,원본파일명 FROM attachments WHERE 연도=? AND id=? AND 저장파일=?", y, id, f);
  if (rows.length === 0) throw new HttpError(404, "증빙을 찾을 수 없습니다.");
  const full = 전체경로(dataDir, rows[0].저장파일);
  if (!안에있는지(dataDir, full)) throw new HttpError(403, "증빙 폴더 밖의 파일입니다.");
  if (!fs.existsSync(full)) throw new HttpError(404, "증빙 파일이 지워졌습니다.");
  return { full, name: rows[0].원본파일명 || path.basename(full) };
}

/** 이 PC 의 기본 프로그램으로 연다 (맥 open / 리눅스 xdg-open / 윈도우 start). */
function openFile(env, dataDir, f) {
  const y = 연도(f.y);
  const id = 아이디(f.id);
  const rows = env.Db.all("SELECT 저장파일 FROM attachments WHERE 연도=? AND id=? AND 저장파일=?", y, id, f.f);
  if (rows.length === 0) throw new HttpError(404, "증빙을 찾을 수 없습니다.");
  const full = 전체경로(dataDir, rows[0].저장파일);
  if (!안에있는지(dataDir, full)) throw new HttpError(403, "증빙 폴더 밖의 파일입니다.");
  if (!fs.existsSync(full)) throw new HttpError(404, "증빙 파일이 지워졌습니다.");

  if (process.platform === "darwin") execFile("open", [full]);
  else if (process.platform === "win32") execFile("cmd", ["/c", "start", "", full]);
  else execFile("xdg-open", [full]);
  return { ok: true };
}

module.exports = { attach, deleteAttach, 파일경로, openFile };
