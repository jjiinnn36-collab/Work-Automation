"use strict";
// 맥·리눅스에서도 쓰는 정리 화면 서버. Node 만 있으면 설치 없이 돈다.
// 윈도우 판(src/WebServer.cs)과 같은 규칙을 지킨다:
//   · localhost 로만 받는다 — 다른 PC 에서는 닿지 않는다.
//   · Host 를 확인해 다른 이름으로 들어온 요청(DNS 재바인딩)을 막는다.
//   · 쓰기 요청은 화면 스크립트만 붙이는 X-PaymentAlert 머리글과 Origin 을 본다.
const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");

const P = require("./lib/paths");
const { Env } = require("./lib/env");
const Api = require("./lib/api");
const W = require("./lib/write");
const SW = require("./lib/settings-write");
const AT = require("./lib/attach");
const D = require("./lib/dates");

const 버전 = "1.0-node";
const 기본포트 = 8317;

const 인자 = 옵션읽기(process.argv.slice(2));
const baseDir = path.resolve(인자.base || path.join(__dirname, ".."));
const dataDir = path.resolve(인자.data || P.자료폴더(baseDir));
const webRoot = path.resolve(인자.web || path.join(baseDir, "web"));

function 옵션읽기(argv) {
  const o = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith("--")) {
      const eq = a.indexOf("=");
      if (eq > 0) o[a.slice(2, eq)] = a.slice(eq + 1);
      else o[a.slice(2)] = argv[++i];
    }
  }
  return o;
}

// ── 정적 파일 ──────────────────────────────────────────────
const 형식 = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
  ".gif": "image/gif", ".ico": "image/x-icon",
  ".woff": "font/woff", ".woff2": "font/woff2", ".txt": "text/plain; charset=utf-8",
};

function 정적(res, reqPath) {
  let rel;
  try { rel = decodeURIComponent(reqPath); }
  catch { return 오류(res, 400, "주소를 읽을 수 없습니다."); }
  if (rel === "/" || rel === "") rel = "/index.html";
  // 웹 폴더 밖으로 나가는 경로는 내주지 않는다.
  const full = path.resolve(webRoot, "." + rel);
  const root = webRoot.replace(/[\\/]+$/, "") + path.sep;
  if (!(full + path.sep).startsWith(root) && full !== webRoot.replace(/[\\/]+$/, "")) return 오류(res, 403, "허용되지 않은 경로입니다.");

  let target = full;
  if (!fs.existsSync(target) || fs.statSync(target).isDirectory()) {
    // Next 정적 내보내기: /설정 → 설정.html
    if (fs.existsSync(full + ".html")) target = full + ".html";
    else if (fs.existsSync(path.join(full, "index.html"))) target = path.join(full, "index.html");
    else return 오류(res, 404, "없는 화면입니다.");
  }

  const ext = path.extname(target).toLowerCase();
  res.writeHead(200, {
    "Content-Type": 형식[ext] || "application/octet-stream",
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
  });
  fs.createReadStream(target).pipe(res);
}

// 맥·리눅스용 알림 팝업 페이지. 브라우저 앱 창으로 띄운다.
function 팝업보내기(res) {
  try {
    const html = fs.readFileSync(path.join(__dirname, "popup", "popup.html"));
    res.writeHead(200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "Referrer-Policy": "no-referrer" });
    res.end(html);
  } catch (e) {
    오류(res, 500, "팝업 페이지를 읽지 못했습니다.");
  }
}

function 보내기(res, status, obj) {
  const body = Buffer.from(JSON.stringify(obj), "utf8");
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": body.length,
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
  });
  res.end(body);
}

function 오류(res, status, message) { 보내기(res, status, { error: message }); }

// ── 읽기 기능 ──────────────────────────────────────────────
function get(reqPath, q, port) {
  if (reqPath === "/api/health") {
    return { ok: true, version: 버전, today: D.ymd(오늘()) };
  }

  const env = Env.open(dataDir, 오늘());
  try {
    switch (reqPath) {
      case "/api/alerts": return Api.alerts(env);
      case "/api/month": return Api.month(env, q.ym);
      case "/api/year": return Api.year(env, q.y);
      case "/api/items": return Api.items(env);
      case "/api/loans": return Api.loans(env);
      case "/api/history": return Api.history(env, q.from, q.to);
      case "/api/events": return Api.events(env, q);
      case "/api/group": return Api.group(env, Api.연도(q.y), q.group);
      case "/api/attachments": return Api.attachments(env, Api.연도(q.y), Api.아이디(q.id));
      case "/api/settings":
        return Api.settings(env, {
          version: 버전,
          dataDir,
          backupDir: P.백업폴더(baseDir, dataDir),
          backupDirDefault: P.백업기본(dataDir),
          backupDirCustom: P.백업따로정함(baseDir),
        });
    }
  } finally {
    env.close();
  }
  throw new Api.HttpError(404, "없는 기능입니다.");
}

function 오늘() {
  return 인자.today ? D.parse(인자.today) : D.today();
}

// ── 쓰기 기능 ──────────────────────────────────────────────
async function post(reqPath, f) {
  const env = Env.open(dataDir, 오늘());
  try {
    switch (reqPath) {
      case "/api/advance": return W.changeStage(env, f, "진행");
      case "/api/defer": return W.changeStage(env, f, "대기");
      case "/api/revert": return W.changeStage(env, f, "되돌리기");
      case "/api/undefer": return W.changeStage(env, f, "대기취소");
      case "/api/amount": return W.saveAmount(env, f);
      case "/api/amount/delete": return W.deleteAmount(env, f);
      case "/api/item": return W.saveItem(env, f);
      case "/api/item/delete": return W.deleteItem(env, f);
      case "/api/item/move": return W.moveItem(env, f);
      case "/api/group/amounts": return W.saveGroupAmounts(env, f);
      case "/api/settings/start-date": return W.setStartDate(env, f);
      case "/api/settings/apikey": return SW.setApiKey(dataDir, f);
      case "/api/settings/backup-dir": return SW.setBackupDir(baseDir, dataDir, webRoot, f);
      case "/api/settings/backup-dir/pick":
        // 맥·리눅스 판은 원격 브라우저라 폴더 선택 창을 띄울 수 없다. 경로를 직접 넣게 안내한다.
        throw new Api.HttpError(501, "이 실행 방식에서는 폴더 선택 창을 열 수 없습니다. 경로를 직접 입력해 주세요.");
      case "/api/backup": return SW.backupNow(env.Db, baseDir, dataDir);
      case "/api/holidays/refresh": return await SW.refreshHolidays(env.Db, dataDir, 오늘());
      case "/api/attach/delete": return AT.deleteAttach(env, dataDir, f);
      case "/api/open": return AT.openFile(env, dataDir, f);
    }
  } finally {
    env.close();
  }
  throw new Api.HttpError(404, "없는 기능입니다.");
}

function 본문버퍼(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    const 최대 = 60 * 1024 * 1024; // 첨부(50MB)까지 받는다
    req.on("data", (c) => {
      size += c.length;
      if (size > 최대) { reject(new Api.HttpError(413, "입력이 너무 큽니다.")); req.destroy(); return; }
      chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks)));
    req.on("error", reject);
  });
}

// 증빙 파일 내려받기 (GET /api/file).
function 증빙내려받기(res, q) {
  const env = Env.open(dataDir, 오늘());
  try {
    const { full, name } = AT.파일경로(env, dataDir, Api.연도(q.y), Api.아이디(q.id), q.f);
    const ext = path.extname(full).toLowerCase();
    res.writeHead(200, {
      "Content-Type": 형식[ext] || "application/octet-stream",
      "Content-Disposition": "inline; filename*=UTF-8''" + encodeURIComponent(name),
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
    });
    fs.createReadStream(full).pipe(res);
  } catch (e) {
    if (e instanceof Api.HttpError) return 오류(res, e.status, e.message);
    return 오류(res, 500, "처리하지 못했습니다: " + (e.message || e));
  } finally {
    env.close();
  }
}

function 양식파싱(body) {
  const f = {};
  const sp = new URLSearchParams(body);
  for (const [k, v] of sp) f[k] = v;
  return f;
}

// ── 서버 ───────────────────────────────────────────────────
function 시작(시작포트) {
  return new Promise((resolve, reject) => {
    let p = 시작포트;
    const server = http.createServer();

    server.on("request", async (req, res) => {
      const port = server.address().port;
      try {
        // 다른 이름으로 들어온 요청(DNS 재바인딩)을 막는다.
        const host = req.headers.host || "";
        if (host !== "localhost:" + port && host !== "127.0.0.1:" + port)
          return 오류(res, 403, "허용되지 않은 주소입니다.");

        const u = new URL(req.url, "http://localhost:" + port);
        const reqPath = u.pathname;
        const q = Object.fromEntries(u.searchParams);

        if (req.method === "GET") {
          if (reqPath === "/api/file") return 증빙내려받기(res, q);
          if (reqPath.startsWith("/api/")) return 보내기(res, 200, get(reqPath, q, port));
          if (reqPath === "/popup" || reqPath === "/popup.html") return 팝업보내기(res);
          return 정적(res, reqPath);
        }

        if (req.method !== "POST") return 오류(res, 405, "지원하지 않는 요청입니다.");

        // 다른 사이트가 몰래 보내는 요청을 막는다. 화면 스크립트만 이 머리글을 붙인다.
        if (req.headers["x-paymentalert"] !== "1") return 오류(res, 403, "허용되지 않은 요청입니다.");
        const origin = req.headers.origin;
        if (origin && origin !== "http://localhost:" + port && origin !== "http://127.0.0.1:" + port)
          return 오류(res, 403, "허용되지 않은 요청입니다.");

        // 첨부·가져오기는 본문이 파일 자체라 양식으로 읽지 않는다.
        if (reqPath === "/api/attach" || reqPath === "/api/import/loans") {
          const buf = await 본문버퍼(req);
          const env = Env.open(dataDir, 오늘());
          try {
            if (reqPath === "/api/attach")
              return 보내기(res, 200, AT.attach(env, dataDir, q, req.headers["x-file-name"], buf));
            const LI = require("./lib/loan-import");
            return 보내기(res, 200, LI.importLoans(env, u.searchParams, req.headers["x-file-name"], buf));
          } finally {
            env.close();
          }
        }

        const body = (await 본문버퍼(req)).toString("utf8");
        const f = 양식파싱(body);
        const out = await post(reqPath, f);
        return 보내기(res, 200, out);
      } catch (e) {
        if (e instanceof Api.HttpError) return 오류(res, e.status, e.message);
        console.error("웹 요청 오류:", req.url, e);
        return 오류(res, 500, "처리하지 못했습니다: " + (e.message || e));
      }
    });

    server.on("error", (e) => {
      if (e.code === "EADDRINUSE" && p < 시작포트 + 10) {
        server.listen(++p, "127.0.0.1");
        return;
      }
      reject(e);
    });

    server.listen(p, "127.0.0.1", () => resolve(server));
  });
}

async function main() {
  // 첫 실행: 필요한 폴더와 빈 DB 를 자동으로 만든다 (윈도우 판과 같게, 자료 복사 없이도 시작).
  const { Store } = require("./lib/store");
  const 새로만듦 = !fs.existsSync(P.db(dataDir));
  try {
    fs.mkdirSync(dataDir, { recursive: true });
    fs.mkdirSync(P.증빙(dataDir), { recursive: true });
    fs.mkdirSync(P.백업기본(dataDir), { recursive: true });
  } catch (e) { console.error("폴더를 만들지 못했습니다: " + (e.message || e)); }
  try {
    Store.초기화(P.db(dataDir));
  } catch (e) {
    console.error("자료 파일을 준비하지 못했습니다: " + (e.message || e));
    process.exit(1);
  }

  if (!fs.existsSync(webRoot)) {
    console.error("정리 화면(web) 폴더가 없습니다: " + webRoot);
    console.error("빌드된 web 폴더를 함께 두거나 --web <폴더> 로 지정하세요.");
    process.exit(1);
  }

  const server = await 시작(Number(인자.port) || Number(process.env.PA_PORT) || 기본포트);
  const port = server.address().port;
  if (새로만듦) console.log("자료 파일을 새로 만들었습니다 (빈 상태).");
  console.log("정리 화면: http://localhost:" + port + "/");
  console.log("자료 폴더: " + dataDir);
  console.log("웹 폴더:   " + webRoot);
}

if (require.main === module) main().catch((e) => { console.error(e); process.exit(1); });

module.exports = { 시작, get, dataDir, webRoot };
