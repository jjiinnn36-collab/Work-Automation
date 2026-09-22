"use strict";
// 윈도우 판(src/DataPaths.cs, src/Ops.cs)과 같은 규칙으로 폴더를 정한다.
// 같은 폴더를 가리키면 윈도우와 맥에서 같은 자료를 그대로 쓴다.
const fs = require("node:fs");
const path = require("node:path");

const 설정파일 = "data-folder.txt";
const 백업설정 = "backup-folder.txt";

/** data-folder.txt 를 읽어 자료 폴더를 정한다. 못 읽으면 baseDir/data. */
function 자료폴더(baseDir) {
  const cfg = path.join(baseDir, 설정파일);
  try {
    if (fs.existsSync(cfg)) {
      for (const raw of fs.readFileSync(cfg, "utf8").split(/\r?\n/)) {
        const line = raw.replace(/^﻿/, "").trim();
        if (line.length === 0 || line.startsWith("#")) continue;
        let p = line.replace(/^"|"$/g, "");
        p = p.replace(/%([^%]+)%/g, (_, n) => process.env[n] || "");
        if (!path.isAbsolute(p)) p = path.resolve(baseDir, p);
        return p;
      }
    }
  } catch { /* 설정을 못 읽는다고 알림이 멈추면 안 된다 */ }
  return path.join(baseDir, "data");
}

const db = (dataDir) => path.join(dataDir, "납부알림.db");
const 증빙 = (dataDir) => path.join(dataDir, "증빙");
const 로그 = (dataDir) => path.join(dataDir, "run.log");
const apiKey = (dataDir) => path.join(dataDir, "apikey.txt");

/** 기본 백업 위치 = 자료 폴더 안 backups. */
const 백업기본 = (dataDir) => path.join(dataDir, "backups");

function 백업따로정함(baseDir) { return fs.existsSync(path.join(baseDir, 백업설정)); }

function 백업폴더(baseDir, dataDir) {
  try {
    const f = path.join(baseDir, 백업설정);
    if (fs.existsSync(f)) {
      const v = fs.readFileSync(f, "utf8").replace(/^﻿/, "").trim();
      if (v.length > 0) return v;
    }
  } catch { }
  return 백업기본(dataDir);
}

module.exports = { 설정파일, 백업설정, 자료폴더, db, 증빙, 로그, apiKey, 백업기본, 백업따로정함, 백업폴더 };
