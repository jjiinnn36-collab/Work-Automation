"use strict";
// 날짜는 모두 'YYYY-MM-DD' 글자와 UTC 자정 Date 로만 다룬다.
// 지역 시간대를 쓰면 자정 근처에서 하루가 밀릴 수 있어서다.

function ymd(d) {
  return d.toISOString().slice(0, 10);
}

function parse(s) {
  if (!s) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(s).trim());
  if (!m) return null;
  return new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
}

function make(y, m, d) {
  return new Date(Date.UTC(y, m - 1, d));
}

function addDays(d, n) {
  return new Date(d.getTime() + n * 86400000);
}

function daysInMonth(y, m) {
  return new Date(Date.UTC(y, m, 0)).getUTCDate();
}

function year(d) { return d.getUTCFullYear(); }
function month(d) { return d.getUTCMonth() + 1; }
function day(d) { return d.getUTCDate(); }
function dow(d) { return d.getUTCDay(); } // 0=일, 6=토

/** 오늘 (이 PC 의 지역 날짜를 UTC 자정으로 옮긴 것). */
function today() {
  const n = new Date();
  return make(n.getFullYear(), n.getMonth() + 1, n.getDate());
}

module.exports = { ymd, parse, make, addDays, daysInMonth, year, month, day, dow, today };
