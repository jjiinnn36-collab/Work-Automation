"use strict";
// src/SheetReader.cs 를 옮긴 것. ERP 엑셀(.xlsx)·CSV 를 글자 표로 읽는다.
// Node 에 들어 있는 zlib 만 쓴다 (추가 라이브러리 없음). XML 은 가볍게 훑어 값을 뽑는다.
const zlib = require("node:zlib");
const path = require("node:path");

const 최대크기 = 20 * 1024 * 1024;
const 최대행 = 20000;
const 최대열 = 200;

class 읽기오류 extends Error {}

function read(data, fileName) {
  if (!data || data.length === 0) throw new 읽기오류("빈 파일입니다.");
  if (data.length > 최대크기) throw new 읽기오류("20MB 보다 큰 파일은 읽지 않습니다.");

  if (data.length >= 4 && data[0] === 0x50 && data[1] === 0x4b && data[2] === 0x03 && data[3] === 0x04)
    return xlsx(data);

  if (data.length >= 4 && data[0] === 0xd0 && data[1] === 0xcf && data[2] === 0x11 && data[3] === 0xe0)
    throw new 읽기오류("암호가 걸렸거나 문서보안(DRM)이 적용된 파일, 또는 옛 엑셀(.xls) 파일이라 읽을 수 없습니다. " +
      "ERP 에서 CSV 로 받거나, 엑셀에서 암호·보안을 푼 뒤 .xlsx 로 저장해 올려 주세요.");

  const ext = (path.extname(fileName || "") || "").toLowerCase();
  if (ext === ".xlsx" || ext === ".xlsm")
    throw new 읽기오류("엑셀 파일 형식이 아닙니다. 파일이 손상됐거나 확장자만 바뀐 파일입니다.");

  const sheet = csv(text(data));
  sheet.이름 = path.basename(fileName || "", path.extname(fileName || ""));
  return [sheet];
}

// ── CSV ──
function text(data) {
  if (data.length >= 3 && data[0] === 0xef && data[1] === 0xbb && data[2] === 0xbf)
    return data.toString("utf8", 3);
  // UTF-8 로 읽어 보고 U+FFFD(깨짐)가 생기면 CP949 로 다시 읽는다. ERP CSV 는 대개 CP949.
  const u = data.toString("utf8");
  if (u.includes("\uFFFD")) {
    try {
      const dec = new TextDecoder("euc-kr", { fatal: false });
      return dec.decode(data);
    } catch { return u; }
  }
  return u;
}

function csv(t) {
  const sheet = { 이름: "", 행: [] };
  if (!t) return sheet;
  const eol = t.search(/[\r\n]/);
  const first = eol < 0 ? t : t.slice(0, eol);
  const sep = first.indexOf(",") < 0 && first.indexOf("\t") >= 0 ? "\t" : ",";

  let row = [], cell = "", quoted = false;
  for (let i = 0; i < t.length; i++) {
    const ch = t[i];
    if (quoted) {
      if (ch === '"') {
        if (i + 1 < t.length && t[i + 1] === '"') { cell += '"'; i++; }
        else quoted = false;
      } else cell += ch;
      continue;
    }
    if (ch === '"' && cell.length === 0) { quoted = true; continue; }
    if (ch === sep) { row.push(cell); cell = ""; continue; }
    if (ch === "\r" || ch === "\n") {
      if (ch === "\r" && i + 1 < t.length && t[i + 1] === "\n") i++;
      row.push(cell); cell = "";
      행추가(sheet, row); row = [];
      continue;
    }
    cell += ch;
  }
  if (cell.length > 0 || row.length > 0) { row.push(cell); 행추가(sheet, row); }
  return sheet;
}

function 행추가(sheet, row) {
  if (sheet.행.length >= 최대행) throw new 읽기오류("행이 너무 많습니다 (" + 최대행 + "행까지).");
  if (row.length > 최대열) row = row.slice(0, 최대열);
  sheet.행.push(row.slice());
}

// ── XLSX ──
function xlsx(data) {
  let entries;
  try { entries = unzip(data); }
  catch { throw new 읽기오류("엑셀 파일이 손상돼 열 수 없습니다."); }

  const get = (name) => {
    if (entries.has(name)) return entries.get(name);
    const lc = name.toLowerCase();
    for (const [k, v] of entries) if (k.toLowerCase() === lc) return v;
    return null;
  };

  const wb = get("xl/workbook.xml");
  if (!wb) throw new 읽기오류("엑셀 통합 문서 정보(workbook.xml)가 없습니다.");
  const wbXml = wb.toString("utf8");

  const rels = get("xl/_rels/workbook.xml.rels");
  const targets = new Map();
  if (rels) {
    const rx = /<Relationship\b([^>]*)\/?>/g;
    let m;
    while ((m = rx.exec(rels.toString("utf8")))) {
      const id = attr(m[1], "Id");
      let t = attr(m[1], "Target");
      if (!id || !t) continue;
      t = t.startsWith("/") ? t.replace(/^\/+/, "") : "xl/" + t;
      targets.set(id, t);
    }
  }

  const shared = sharedStrings(get("xl/sharedStrings.xml"));
  const result = [];
  const sx = /<sheet\b([^>]*)\/?>/g;
  let m;
  while ((m = sx.exec(wbXml))) {
    const rid = attr(m[1], "r:id");
    const name = unxml(attr(m[1], "name") || "");
    const p = targets.get(rid);
    if (!p) continue;
    const doc = get(p);
    if (!doc) continue;
    const sheet = sheetOf(doc.toString("utf8"), shared);
    sheet.이름 = name;
    result.push(sheet);
  }
  if (result.length === 0) throw new 읽기오류("읽을 수 있는 시트가 없습니다.");
  return result;
}

function attr(s, name) {
  const esc = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const m = new RegExp('(?:^|\\s)' + esc + '\\s*=\\s*"([^"]*)"').exec(s);
  return m ? m[1] : null;
}

function unxml(s) {
  return s.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'").replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(+n))
    .replace(/&#x([0-9a-fA-F]+);/g, (_, n) => String.fromCodePoint(parseInt(n, 16)))
    .replace(/&amp;/g, "&");
}

// <si> 안의 <t> 를 이어 붙인다. 읽는 법 표시(rPh)는 뺀다.
function sharedStrings(buf) {
  const list = [];
  if (!buf) return list;
  const xml = buf.toString("utf8");
  const sx = /<si\b[^>]*>([\s\S]*?)<\/si>/g;
  let m;
  while ((m = sx.exec(xml))) list.push(runs(m[1]));
  return list;
}

function runs(inner) {
  // rPh 블록은 통째로 뺀 뒤 <t>…</t> 만 모은다.
  const noRph = inner.replace(/<rPh\b[\s\S]*?<\/rPh>/g, "");
  let out = "";
  const tx = /<t\b[^>]*>([\s\S]*?)<\/t>|<t\b[^>]*\/>/g;
  let m;
  while ((m = tx.exec(noRph))) out += m[1] != null ? unxml(m[1]) : "";
  return out;
}

function sheetOf(xml, shared) {
  const sheet = { 이름: "", 행: [] };
  const rowx = /<row\b([^>]*)>([\s\S]*?)<\/row>|<row\b([^>]*)\/>/g;
  let rm;
  while ((rm = rowx.exec(xml))) {
    const rowAttr = rm[1] != null ? rm[1] : rm[3];
    const inner = rm[2] != null ? rm[2] : "";
    const cells = [];
    let next = 0;
    const cx = /<c\b([^>]*)>([\s\S]*?)<\/c>|<c\b([^>]*)\/>/g;
    let cm;
    while ((cm = cx.exec(inner))) {
      const cAttr = cm[1] != null ? cm[1] : cm[3];
      const cInner = cm[2] != null ? cm[2] : "";
      let col = 열번호(attr(cAttr, "r"));
      if (col < 0) col = next;
      next = col + 1;
      if (col >= 최대열) continue;
      while (cells.length < col) cells.push("");
      cells.push(값(cAttr, cInner, shared));
    }
    const rowNo = parseInt(attr(rowAttr, "r"), 10);
    if (Number.isInteger(rowNo)) while (sheet.행.length < rowNo - 1) 행추가(sheet, []);
    행추가(sheet, cells);
  }
  return sheet;
}

function 값(cAttr, cInner, shared) {
  const t = attr(cAttr, "t");
  if (t === "inlineStr") {
    const m = /<is\b[^>]*>([\s\S]*?)<\/is>/.exec(cInner);
    return m ? runs(m[1]) : "";
  }
  const vm = /<v\b[^>]*>([\s\S]*?)<\/v>/.exec(cInner);
  if (!vm) return "";
  const raw = unxml(vm[1]);
  if (t === "s") {
    const i = parseInt(raw, 10);
    return Number.isInteger(i) && i >= 0 && i < shared.length ? shared[i] : "";
  }
  if (t === "b") return raw === "1" ? "TRUE" : "FALSE";
  return raw;
}

function 열번호(cellRef) {
  if (!cellRef) return -1;
  let n = 0, i = 0;
  for (; i < cellRef.length; i++) {
    const ch = cellRef[i].toUpperCase();
    if (ch < "A" || ch > "Z") break;
    n = n * 26 + (ch.charCodeAt(0) - 64);
    if (n > 16384) return -1;
  }
  return i === 0 ? -1 : n - 1;
}

// ── 최소 ZIP 리더 (중앙 디렉터리 기준) ──
function unzip(buf) {
  const map = new Map();
  // End Of Central Directory 를 뒤에서 찾는다.
  let eocd = -1;
  for (let i = buf.length - 22; i >= 0 && i >= buf.length - 22 - 65536; i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error("ZIP EOCD 없음");
  const count = buf.readUInt16LE(eocd + 10);
  let off = buf.readUInt32LE(eocd + 16);

  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(off) !== 0x02014b50) break;
    const method = buf.readUInt16LE(off + 10);
    const compSize = buf.readUInt32LE(off + 20);
    const nameLen = buf.readUInt16LE(off + 28);
    const extraLen = buf.readUInt16LE(off + 30);
    const commentLen = buf.readUInt16LE(off + 32);
    const localOff = buf.readUInt32LE(off + 42);
    const name = buf.toString("utf8", off + 46, off + 46 + nameLen);

    // 로컬 헤더에서 실제 데이터 시작 위치를 구한다 (extra 길이가 다를 수 있다).
    const lhNameLen = buf.readUInt16LE(localOff + 26);
    const lhExtraLen = buf.readUInt16LE(localOff + 28);
    const dataStart = localOff + 30 + lhNameLen + lhExtraLen;
    const comp = buf.subarray(dataStart, dataStart + compSize);

    if (method === 0) map.set(name, Buffer.from(comp));
    else if (method === 8) map.set(name, zlib.inflateRawSync(comp));
    // 다른 압축 방식은 건너뛴다 (xlsx 는 0/8 만 쓴다).

    off += 46 + nameLen + extraLen + commentLen;
  }
  return map;
}

module.exports = { read, csv, 읽기오류, 최대크기, 열번호 };
