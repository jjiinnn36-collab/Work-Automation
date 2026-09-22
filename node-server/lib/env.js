"use strict";
// src/WebServer.cs 의 Env 를 옮긴 것. 요청 하나마다 열고 닫는다.
const path = require("node:path");
const { Store } = require("./store");
const { BusinessDayCalendar } = require("./businessdays");
const { Stages } = require("./model");
const Sched = require("./scheduler");
const D = require("./dates");

class Env {
  static open(dataDir, today) {
    const e = new Env();
    e.dataDir = dataDir;
    e.Today = today;
    e.Db = new Store(path.join(dataDir, "납부알림.db"));
    try {
      e.Master = e.Db.loadMaster();
      const cache = e.Db.loadHolidays();
      e.공휴일 = cache;
      e.Cal = new BusinessDayCalendar([...cache.Dates.keys()], [...cache.Years]);
      e.Amounts = e.Db.loadAmounts();
      e.시작일 = e.Db.loadStartDate();
      e.Status = e.Db.loadStatus();
      e.첨부수 = e.Db.첨부수();
    } catch (err) {
      e.Db.close();
      throw err;
    }
    return e;
  }

  close() { this.Db.close(); }

  get 마지막동작() {
    if (!this._마지막동작) this._마지막동작 = this.Db.마지막동작();
    return this._마지막동작;
  }

  /** 기관마다 홈페이지 하나: 같은 기관 중 목록 순서로 먼저 주소가 있는 항목. */
  기관사이트(기관) {
    if (!this._기관사이트) {
      this._기관사이트 = new Map();
      for (const x of this.Master) {
        const k = (x.기관 || "").trim();
        if (k.length > 0 && (x.홈페이지주소 || "").trim().length > 0 && !this._기관사이트.has(k))
          this._기관사이트.set(k, x);
      }
    }
    return this._기관사이트.get((기관 || "").trim()) || null;
  }

  기관사이트붙이기(o, it) {
    const src = it && (it.홈페이지주소 || "").trim().length > 0 ? it : (it ? this.기관사이트(it.기관) : null);
    o.orgSiteUrl = src ? src.홈페이지주소 : "";
    o.orgSiteName = src ? (src.홈페이지명 || "") : "";
    return o;
  }

  Item(id) {
    for (const it of this.Master) if (it.Id === id) return it;
    return null;
  }

  /** 그 해의 발생 건 전부. 추적 시작일 이전 건도 넣는다 (화면이 '지난 건' 에 흐리게 보여 준다). */
  occurrencesOf(year) {
    const 기준 = year === D.year(this.Today) ? this.Today : D.make(year, 7, 1);
    return Sched.buildOccurrences(this.Master, this.Cal, 기준, this.Amounts, null)
      .filter((o) => o.연도 === year);
  }

  /** 추적 시작일보다 기한이 이른 건. 할 일이 아니므로 집계·행동에서 뺀다. */
  시작전(o) {
    return !!this.시작일 && o.보정기한일.getTime() < this.시작일.getTime();
  }

  단계(o) {
    const st = this.Status[o.Key];
    return st ? st.단계 : 0;
  }

  done(o) {
    const st = this.Status[o.Key];
    return !!st && st.단계 >= Stages.finalIndex(o.Item);
  }
}

module.exports = { Env };
