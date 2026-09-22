"use strict";
// src/BusinessDays.cs 를 그대로 옮긴 것. 결과가 달라지면 안 되므로 계산 순서까지 같게 둔다.
const D = require("./dates");

class BusinessDayCalendar {
  /**
   * @param {string[]} holidayDates 'YYYY-MM-DD' 목록
   * @param {number[]} years 공휴일 자료를 가진 연도
   */
  constructor(holidayDates, years) {
    this.holidays = new Set(holidayDates || []);
    this.coveredYears = new Set(years || []);
  }

  /** 해당 연도의 공휴일 자료를 갖고 있는가. */
  hasYear(year) { return this.coveredYears.has(year); }

  isHoliday(d) { return this.holidays.has(D.ymd(d)); }

  isBusinessDay(d) {
    const w = D.dow(d);
    if (w === 0 || w === 6) return false;
    return !this.isHoliday(d);
  }

  /** 영업일이 아니면 다음 영업일로 민다. 영업일이면 그대로. */
  nextBusinessDayOrSame(d) {
    let x = d;
    let guard = 0;
    while (!this.isBusinessDay(x)) {
      x = D.addDays(x, 1);
      if (++guard > 400) throw new Error("영업일을 찾지 못했습니다. 공휴일 자료를 확인하세요.");
    }
    return x;
  }

  /** from 에서 n 영업일 앞선 날짜. n=3 이면 3영업일 전. */
  subtractBusinessDays(from, n) {
    let x = from;
    let moved = 0;
    let guard = 0;
    while (moved < n) {
      x = D.addDays(x, -1);
      if (this.isBusinessDay(x)) moved++;
      if (++guard > 1000) throw new Error("영업일 역산에 실패했습니다.");
    }
    return x;
  }

  /** from(제외)부터 to(포함)까지의 영업일 수. to 가 과거면 음수. */
  businessDaysBetween(from, to) {
    const a = from.getTime(), b = to.getTime();
    if (a === b) return 0;

    const sign = b > a ? 1 : -1;
    const lo = sign > 0 ? from : to;
    const hi = sign > 0 ? to : from;

    let count = 0;
    let x = D.addDays(lo, 1);
    while (x.getTime() <= hi.getTime()) {
      if (this.isBusinessDay(x)) count++;
      x = D.addDays(x, 1);
    }
    return count * sign;
  }
}

module.exports = { BusinessDayCalendar };
