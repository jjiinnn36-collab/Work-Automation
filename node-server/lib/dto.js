"use strict";
// src/WebServer.cs 의 Head / ItemDto / Dto 를 옮긴 것. 화면이 쓰는 칸 이름과 순서를 그대로 지킨다.
const D = require("./dates");
const { Stages, AmountRules, 납부있음, 카드이름 } = require("./model");

const 요일 = ["일", "월", "화", "수", "목", "금", "토"];

function dow(d) { return 요일[D.dow(d)]; }

function 오늘글자(d) {
  return D.year(d) + "년 " + D.month(d) + "월 " + D.day(d) + "일 (" + dow(d) + ")";
}

function won(n) {
  // C# 의 "N0" 과 같은 천 단위 구분.
  const neg = n < 0;
  const s = Math.abs(Math.round(n)).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return (neg ? "-" : "") + s;
}

function head(env) {
  return {
    today: D.ymd(env.Today),
    todayText: 오늘글자(env.Today),
    startDate: env.시작일 ? D.ymd(env.시작일) : null,
  };
}

/** 각 지점에 도달할 때 누를 버튼 문구. 첫 칸(시작 지점)은 비어 있다. */
function 행동목록(it) {
  const names = Stages.For(it);
  const list = new Array(names.length);
  list[0] = "";
  for (let i = 1; i < names.length; i++) list[i] = Stages.다음행동(it, i - 1) || "";
  return list;
}

function itemDto(it) {
  return {
    id: it.Id,
    org: it.기관,
    name: 카드이름(it),
    flow: it.진행흐름,
    month: it.월,
    day: it.말일 ? "말일" : String(it.일),
    lead: it.알림영업일,
    rule: it.금액규칙 || "",
    fixed: it.금액규칙 === AmountRules.고정 ? it.고정금액 : null,
    memo: it.비고 || "",
    siteName: it.홈페이지명 || "",
    siteUrl: it.홈페이지주소 || "",
    group: it.묶음 || "",
    paid: 납부있음(it),
    stages: Stages.For(it),
    actions: 행동목록(it),
    hideStart: Stages.시작숨김(it),
    noAmount: it.진행흐름 === "사용자설정" && !!it.금액없음,
    loan: !!it.차입,
    startYear: it.시작연도 == null ? null : it.시작연도,
    endYear: it.종료연도 == null ? null : it.종료연도,
  };
}

/** 발생 건 하나를 화면이 그대로 그릴 수 있는 모양으로. 문구와 판정은 여기서 끝낸다. */
function dto(env, o, 오늘대기) {
  const it = o.Item;
  const stages = Stages.For(it);
  const st = env.Status[o.Key] || null;
  const stage = st == null ? 0 : Math.max(0, Math.min(st.단계, stages.length - 1));
  const done = stage >= stages.length - 1;
  const left = env.Cal.businessDaysBetween(env.Today, o.보정기한일);

  let text, severity;
  const before = env.시작전(o);
  if (done) { text = "처리 완료"; severity = "done"; }
  else if (before) { text = "추적 시작 전"; severity = "before"; }
  else {
    if (left > 0) text = "D-" + left + "영업일";
    else if (left === 0) text = "오늘이 기한";
    else text = "기한 " + (-left) + "영업일 지남";

    if (left < 0) severity = "overdue";
    else if (o.알림일.getTime() <= env.Today.getTime()) severity = "soon";
    else severity = "normal";
  }

  const amount = AmountRules.금액(o);
  const amountText = amount != null ? won(amount) + "원" : (AmountRules.미확인(o) ? "미확인" : "");

  const 오늘확인 = 오늘대기 || (st != null && st.최종확인일 != null && st.최종확인일.getTime() === env.Today.getTime());
  const confirmedToday = 오늘확인;
  // 오늘 '오늘은 대기' 를 눌렀고 그 뒤로 다른 동작이 없는 건 — 웹에서 대기 취소를 보인다.
  const deferredToday = !done && st != null && 오늘확인 && env.마지막동작[o.Key] === "대기";

  return env.기관사이트붙이기({
    year: o.연도,
    id: it.Id,
    org: it.기관,
    name: 카드이름(it),
    flow: it.진행흐름,
    stages,
    hideStart: Stages.시작숨김(it),
    stage,
    stageName: stages[stage],
    nextStage: done ? null : stages[stage + 1],
    nextAction: done ? Stages.끝남문구 : Stages.다음행동(it, stage),
    paid: 납부있음(it),
    siteName: it.홈페이지명 || "",
    siteUrl: it.홈페이지주소 || "",
    group: it.묶음 || "",
    done,
    due: D.ymd(o.원기한일),
    dueDow: dow(o.원기한일),
    payDue: D.ymd(o.보정기한일),
    payDow: dow(o.보정기한일),
    shifted: o.원기한일.getTime() !== o.보정기한일.getTime(),
    alertDate: D.ymd(o.알림일),
    daysLeft: left,
    statusText: text,
    severity,
    amount,
    amountText,
    amountEntered: o.실제금액 != null,
    amountRule: it.금액규칙 || "",
    confirmedToday,
    deferredToday,
    changedAt: st != null && st.변경일시원문 ? st.변경일시원문 : null,
    attachments: env.첨부수[o.연도 + "\t" + it.Id] || 0,
    beforeStart: before,
    loan: !!it.차입,
    memo: it.비고 || "",
  }, it);
}

module.exports = { head, itemDto, dto, dow, won, 오늘글자 };
