"use strict";
// src/Model.cs 의 Stages / AmountRules / PaymentItem 계산 부분을 옮긴 것.
const D = require("./dates");

const 신고납부 = ["신고 전", "신고", "전표발행", "납부"];
const 납부만 = ["고지서수령", "전표발행", "납부"];
const 제출만 = ["제출자료 작성", "제출"];
const 차입이자 = ["지급액 확인", "전표발행", "납부"];
const 사용자기본 = ["시작", "완료"];

const 신고납부행동 = ["", "신고완료", "전표발행", "납부완료"];
const 납부만행동 = ["", "전표발행", "납부완료"];
const 제출만행동 = ["", "제출"];

const 끝남문구 = "모두 완료";

const Stages = {
  끝남문구,

  /** 항목의 지점 목록. 사용자설정이면 사용자가 정한 단계. */
  For(it) {
    if (it.진행흐름 === "사용자설정" && it.사용자단계 && it.사용자단계.length >= 2) return it.사용자단계;
    if (it.차입 && it.진행흐름 === "납부만") return 차입이자;
    switch (it.진행흐름) {
      case "신고납부": return 신고납부;
      case "납부만": return 납부만;
      case "제출만": return 제출만;
      case "사용자설정": return 사용자기본;
      default: throw new Error("알 수 없는 진행흐름: " + it.진행흐름);
    }
  },

  finalIndex(it) { return Stages.For(it).length - 1; },

  /** 시작점(0번)을 화면에 보이지 않는 흐름인가 — 신고 후 납부. 단계 번호는 그대로다. */
  시작숨김(it) { return !!it && it.진행흐름 === "신고납부"; },

  /** 화면에 그리는 지점: 시작숨김이면 0번을 뺀다. */
  보이는지점(it) {
    const all = Stages.For(it);
    if (!Stages.시작숨김(it) || all.length < 2) return all.slice();
    return all.slice(1);
  },

  /** stage 까지 끝냈을 때 지금 누를 행동. 다 끝났으면 null. */
  다음행동(it, stage) {
    const next = stage + 1;
    if (it.진행흐름 === "사용자설정") {
      const names = Stages.For(it);
      if (next < 1 || next >= names.length) return null;
      const a = it.사용자행동 && next < it.사용자행동.length ? (it.사용자행동[next] || "").trim() : "";
      return a.length > 0 ? a : names[next];
    }
    let acts;
    if (it.진행흐름 === "신고납부") acts = 신고납부행동;
    else if (it.진행흐름 === "납부만") acts = 납부만행동;
    else acts = 제출만행동;
    return next >= 1 && next < acts.length ? acts[next] : null;
  },

  /** DB 의 items.단계정의 글자를 사용자단계·사용자행동으로 푼다. */
  단계정의읽기(it, text) {
    it.사용자단계 = null;
    it.사용자행동 = null;
    if (!text) return;
    const names = [], actions = [];
    for (const raw of String(text).replace(/\r/g, "").split("\n")) {
      if (raw.trim().length === 0) continue;
      const bar = raw.indexOf("|");
      names.push((bar < 0 ? raw : raw.slice(0, bar)).trim());
      actions.push(bar < 0 ? "" : raw.slice(bar + 1).trim());
    }
    it.사용자단계 = names;
    it.사용자행동 = actions;
  },
};

const AmountRules = {
  고정: "고정",
  변동: "변동",

  /** 옛 규칙 값을 두 가지로 줄인다. 고정금액이 적혀 있던 건은 고정으로 본다. */
  Normalize(rule, fixedAmount) {
    const r = (rule || "").trim();
    if (r === "고정" || fixedAmount != null) return "고정";
    return "변동";
  },

  IsValid(rule) { return rule === "고정" || rule === "변동"; },

  /** 그 해에 쓸 금액. 없으면 null. */
  금액(o) {
    const it = o.Item;
    if (!납부있음(it)) return null;
    if (o.실제금액 != null) return o.실제금액.금액;
    if (it.금액규칙 === "고정") return it.고정금액;
    return null;
  },

  /** 금액이 있어야 하는데 아직 모르는가. */
  미확인(o) {
    return 납부있음(o.Item) && AmountRules.금액(o) == null;
  },
};

function 납부있음(it) {
  if (it.진행흐름 === "제출만") return false;
  if (it.진행흐름 === "사용자설정") return !it.금액없음;
  return true;
}

function 카드이름(it) { return it.차입 ? "차입금 이자" : it.비용명; }
function 표시명(it) { return 카드이름(it) + " (" + it.기관 + ")"; }

function 해당연도(it, year) {
  if (it.시작연도 != null && year < it.시작연도) return false;
  if (it.종료연도 != null && year > it.종료연도) return false;
  return true;
}

/** 보정 전 기한일. */
function 원기한일(it, year) {
  const day = it.말일 ? D.daysInMonth(year, it.월) : it.일;
  return D.make(year, it.월, day);
}

module.exports = { Stages, AmountRules, 납부있음, 카드이름, 표시명, 해당연도, 원기한일 };
