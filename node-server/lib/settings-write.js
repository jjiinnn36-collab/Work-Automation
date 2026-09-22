"use strict";
// 설정 저장. src/WebServer.cs 의 SetApiKey/SetBackupDir/BackupNow/RefreshHolidays 와 같다.
const fs = require("node:fs");
const path = require("node:path");
const P = require("./paths");
const D = require("./dates");
const { HttpError } = require("./api");

// ── 인증키 ──
function setApiKey(dataDir, f) {
  const key = (f.key || "").trim();
  const p = P.apiKey(dataDir);
  if (key.length === 0) {
    if (fs.existsSync(p)) fs.unlinkSync(p);
    return { ok: true, apiKeySet: false };
  }
  if (key.length > 300) throw new HttpError(400, "인증키가 너무 깁니다.");
  for (const ch of key) { const c = ch.charCodeAt(0); if (c <= 32 || c === 127) throw new HttpError(400, "인증키에 공백이나 줄바꿈이 들어 있습니다."); }
  fs.mkdirSync(dataDir, { recursive: true });
  fs.writeFileSync(p, key, "utf8");
  return { ok: true, apiKeySet: true };
}

// ── 백업 폴더 ──
const 동기화폴더 = ["OneDrive", "Dropbox", "Google Drive", "GoogleDrive", "iCloudDrive", "iCloud Drive", "내 드라이브", "My Drive", "MYBOX", "네이버 MYBOX"];

function 안에있음(p, root) {
  const a = path.resolve(p).replace(/[\\/]+$/, "") + path.sep;
  const b = path.resolve(root).replace(/[\\/]+$/, "") + path.sep;
  return a.toLowerCase().startsWith(b.toLowerCase());
}

// 사본에는 실제 자료가 그대로 들어 있어 PC 밖으로 나가는 곳은 막는다. 맥·리눅스는 드라이브 문자가 없으므로
// 절대경로만 확인하고 클라우드 동기화 폴더 이름을 거른다.
function 폴더검사(folder, webRoot) {
  const p = (folder || "").trim().replace(/^"|"$/g, "");
  if (p.length === 0) return null;
  if (p.startsWith("\\\\") || p.startsWith("//")) return "네트워크 폴더는 쓸 수 없습니다. 이 PC 의 폴더를 넣으세요.";
  const win = process.platform === "win32";
  if (win) {
    if (p.length < 3 || !/[A-Za-z]/.test(p[0]) || p[1] !== ":" || (p[2] !== "\\" && p[2] !== "/"))
      return "C:\\백업 처럼 드라이브부터 쓴 전체 경로를 넣으세요.";
  } else if (!p.startsWith("/")) {
    return "/ 로 시작하는 전체 경로를 넣으세요.";
  }
  let full;
  try { full = path.resolve(p); } catch { return "폴더 경로가 올바르지 않습니다."; }
  for (const seg of full.split(/[\\/]/))
    for (const name of 동기화폴더)
      if (seg.toLowerCase().startsWith(name.toLowerCase()))
        return "클라우드 동기화 폴더(" + seg + ")는 쓸 수 없습니다. 사본이 PC 밖으로 올라갑니다.";
  const od = process.env.OneDrive || process.env.OneDriveCommercial || process.env.OneDriveConsumer;
  if (od && 안에있음(full, od)) return "OneDrive 폴더는 쓸 수 없습니다. 사본이 PC 밖으로 올라갑니다.";
  if (webRoot && 안에있음(full, webRoot)) return "웹 화면 폴더 안에는 둘 수 없습니다.";
  try {
    fs.mkdirSync(full, { recursive: true });
    const probe = path.join(full, ".쓰기확인-" + Date.now() + ".tmp");
    fs.writeFileSync(probe, "");
    fs.unlinkSync(probe);
  } catch (e) { return "그 폴더에 쓸 수 없습니다: " + (e.message || e); }
  return null;
}

function 폴더저장(baseDir, folder) {
  const cfg = path.join(baseDir, P.백업설정);
  const p = (folder || "").trim().replace(/^"|"$/g, "");
  if (p.length === 0) { if (fs.existsSync(cfg)) fs.unlinkSync(cfg); return; }
  fs.writeFileSync(cfg, "# 백업 폴더 (설정 화면에서 바꿈)\r\n" + path.resolve(p) + "\r\n", "utf8");
}

function setBackupDir(baseDir, dataDir, webRoot, f) {
  const dir = (f.dir || "").trim();
  const 이유 = 폴더검사(dir, webRoot);
  if (이유) throw new HttpError(400, 이유);
  폴더저장(baseDir, dir);
  return { ok: true, backupDir: P.백업폴더(baseDir, dataDir), custom: P.백업따로정함(baseDir) };
}

// ── 백업 ──
function backupNow(db, baseDir, dataDir) {
  const folder = P.백업폴더(baseDir, dataDir);
  fs.mkdirSync(folder, { recursive: true });
  const n = new Date();
  const p2 = (x) => String(x).padStart(2, "0");
  const stamp = "" + n.getFullYear() + p2(n.getMonth() + 1) + p2(n.getDate()) + "-" +
    p2(n.getHours()) + p2(n.getMinutes()) + p2(n.getSeconds());
  const out = path.join(folder, "납부알림-수동-" + stamp + ".db");
  // WAL 이 있어도 안전한 단일 파일 사본. SQLite 권장 방식.
  db.run("VACUUM INTO ?", out);
  return { ok: true, name: path.basename(out) };
}

// ── 공휴일 자동 받기 ──
const ApiBase = "https://apis.data.go.kr/B090041/openapi/service/SpcdeInfoService/getRestDeInfo";

async function fetchMonth(apiKey, year, month) {
  const url = `${ApiBase}?serviceKey=${encodeURIComponent(apiKey)}&solYear=${year}&solMonth=${String(month).padStart(2, "0")}&numOfRows=50&_type=xml`;
  const r = await fetch(url, { headers: { "User-Agent": "PaymentAlert" }, signal: AbortSignal.timeout(15000) });
  const xml = await r.text();
  const dates = [];
  // 가벼운 XML 훑기 — item 마다 locdate/isHoliday/dateName 을 뽑는다.
  const items = xml.split(/<item>/).slice(1);
  for (const chunk of items) {
    const body = chunk.split(/<\/item>/)[0];
    const loc = /<locdate>\s*(\d{8})\s*<\/locdate>/.exec(body);
    if (!loc) continue;
    const hol = /<isHoliday>\s*([^<]*)<\/isHoliday>/.exec(body);
    if (hol && hol[1].trim() !== "Y") continue;
    const nm = /<dateName>\s*([^<]*)<\/dateName>/.exec(body);
    const s = loc[1];
    dates.push({ ymd: `${s.slice(0, 4)}-${s.slice(4, 6)}-${s.slice(6, 8)}`, name: nm ? nm[1].trim() : "" });
  }
  if (dates.length === 0 && /errMsg|SERVICE_KEY_IS_NOT_REGISTERED|SERVICE_ACCESS_DENIED/.test(xml)) {
    if (xml.includes("SERVICE_KEY_IS_NOT_REGISTERED_ERROR"))
      throw new Error("서비스키가 등록되어 있지 않습니다. '한국천문연구원_특일 정보' 활용신청을 확인하세요.");
    if (xml.includes("SERVICE_ACCESS_DENIED_ERROR")) throw new Error("해당 서비스에 대한 사용 권한이 없습니다.");
    const m = /<errMsg>([^<]*)<\/errMsg>/.exec(xml);
    if (m) throw new Error("공공데이터포털 오류: " + m[1]);
  }
  return dates;
}

async function refreshHolidays(db, dataDir, today) {
  const p = P.apiKey(dataDir);
  if (!fs.existsSync(p)) throw new HttpError(400, "공공데이터포털 인증키를 먼저 넣어 주세요.");
  const key = fs.readFileSync(p, "utf8").trim();

  const cache = db.loadHolidays();
  const before = cache.Dates.size;
  const y = D.year(today);
  const years = [y - 1, y, y + 1];
  let added = 0;
  for (const year of years) {
    for (let m = 1; m <= 12; m++) {
      const ds = await fetchMonth(key, year, m);
      for (const d of ds) { cache.Dates.set(d.ymd, d.name); cache.Years.add(Number(d.ymd.slice(0, 4))); added++; }
    }
    cache.Years.add(year);
  }
  cache.Updated = today;
  saveHolidays(db, cache);
  return { ok: true, added, message: `공휴일 ${before}건 → ${cache.Dates.size}건.` };
}

function saveHolidays(db, cache) {
  db.tx(() => {
    db.run("DELETE FROM holidays");
    for (const [ymd, name] of cache.Dates) db.run("INSERT INTO holidays(날짜,명칭) VALUES(?,?)", ymd, name || "");
    const ys = [...cache.Years].sort((a, b) => a - b).join(",");
    db.setMeta("holidays.years", ys);
    db.setMeta("holidays.updated", cache.Updated ? D.ymd(cache.Updated) : null);
  });
}

module.exports = { setApiKey, setBackupDir, backupNow, refreshHolidays, 폴더검사 };
