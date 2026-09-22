#!/usr/bin/env node
"use strict";
// 알림 팝업을 브라우저 '앱 창'으로 띄운다 (맥·리눅스). 매일 아침 예약 실행이 이걸 부른다.
// 서버가 없으면 먼저 서버를 조용히 띄운 뒤, 오늘 처리할 건이 있을 때만 창을 연다.
const http = require("node:http");
const { spawn, execFile, execFileSync } = require("node:child_process");
const path = require("node:path");
const fs = require("node:fs");

// 팝업 앱 창 크기 (윈도우 팝업과 비슷한 작은 창). 콘텐츠 236 + 제목 바 여유.
const 창폭 = 360, 창높이 = 264, 오른쪽여백 = 12, 아래여백 = 8;

const PORT = Number(process.env.PA_PORT || 8317);
const BASE = "http://localhost:" + PORT;
const ROOT = path.resolve(__dirname, "..");

function 요청(pathname) {
  return new Promise((resolve) => {
    const req = http.get(BASE + pathname, { timeout: 3000 }, (res) => {
      let s = "";
      res.on("data", (d) => (s += d));
      res.on("end", () => { try { resolve(JSON.parse(s)); } catch { resolve(null); } });
    });
    req.on("error", () => resolve(null));
    req.on("timeout", () => { req.destroy(); resolve(null); });
  });
}

function 잠깐(ms) { return new Promise((r) => setTimeout(r, ms)); }

async function 서버깨우기() {
  if (await 요청("/api/health")) return true;
  // 서버가 없으면 백그라운드로 띄운다.
  const out = fs.openSync(path.join(ROOT, "server.out.log"), "a");
  const child = spawn(process.execPath, [path.join(ROOT, "server.js")], {
    cwd: ROOT, detached: true, stdio: ["ignore", out, out],
    env: Object.assign({}, process.env, { PA_PORT: String(PORT) }),
  });
  child.unref();
  for (let i = 0; i < 20; i++) { await 잠깐(300); if (await 요청("/api/health")) return true; }
  return false;
}

// 화면 크기를 알아내 우하단 바닥에 붙일 창 위치를 구한다 (설치 없이 OS 기본 도구만 쓴다).
function 화면크기() {
  try {
    if (process.platform === "darwin") {
      // Finder 로 바탕화면(주 모니터) 크기를 얻는다. "0, 0, 1440, 900" 형태.
      const out = execFileSync("osascript", ["-e", 'tell application "Finder" to get bounds of window of desktop'], { encoding: "utf8" });
      const m = out.match(/-?\d+/g);
      if (m && m.length >= 4) return { w: Number(m[2]), h: Number(m[3]) };
    } else if (process.platform !== "win32") {
      const out = execFileSync("sh", ["-c", "xdpyinfo | grep dimensions"], { encoding: "utf8" });
      const m = out.match(/(\d+)x(\d+)/);
      if (m) return { w: Number(m[1]), h: Number(m[2]) };
    }
  } catch { /* 화면 크기를 못 구하면 위치는 브라우저 기본값에 맡긴다 */ }
  return null;
}

// 크롬 계열 실행 인자: 우하단 바닥에 붙는 작은 앱 창.
function 크롬인자(url) {
  const args = ["--app=" + url, "--window-size=" + 창폭 + "," + 창높이, "--user-data-dir=" + path.join(ROOT, ".popup-profile")];
  const s = 화면크기();
  if (s) {
    const x = Math.max(0, s.w - 창폭 - 오른쪽여백);
    const y = Math.max(0, s.h - 창높이 - 아래여백);   // 바닥에 붙임
    args.push("--window-position=" + x + "," + y);
  }
  return args;
}

function 앱창열기(url) {
  const p = process.platform;
  if (p === "darwin") {
    // 크로미엄 계열이 있으면 작은 앱 창(--app). 없으면(사파리·파이어폭스·Zen 등) 기본 브라우저 새 탭 —
    // 이때는 팝업 화면이 가운데 작은 카드로 보인다(popup.html 이 폭을 380px 로 제한).
    const chrome = [
      "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
      "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
      "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
      "/Applications/Vivaldi.app/Contents/MacOS/Vivaldi",
      "/Applications/Arc.app/Contents/MacOS/Arc",
      "/Applications/Chromium.app/Contents/MacOS/Chromium",
    ].find((x) => fs.existsSync(x));
    if (chrome) spawn(chrome, 크롬인자(url), { detached: true, stdio: "ignore" }).unref();
    else execFile("open", [url]);
  } else if (p === "win32") {
    execFile("cmd", ["/c", "start", "", url]);
  } else {
    const chrome = ["google-chrome", "chromium", "microsoft-edge"].find(() => true);
    try { spawn(chrome, 크롬인자(url), { detached: true, stdio: "ignore" }).unref(); }
    catch { execFile("xdg-open", [url]); }
  }
}

(async () => {
  const 강제 = process.argv.includes("--always");
  if (!(await 서버깨우기())) { console.error("서버를 띄우지 못했습니다."); process.exit(1); }
  const d = await 요청("/api/alerts");
  const 건수 = d ? ((d.rows || []).length + (d.overdue || []).length) : 0;
  if (건수 === 0 && !강제) { console.log("오늘 처리할 기한이 없습니다."); return; }
  앱창열기(BASE + "/popup");
})();
