"use strict";
// src/Scheduler.cs 를 그대로 옮긴 것.
const D = require("./dates");
const { Stages, 해당연도, 원기한일 } = require("./model");

/** 공휴일 자료가 없는 연도에 적용할 안전 여유(일). */
const 안전여유일 = 2;

/** 기한이 지난 뒤에도 이만큼(영업일)은 계속 강제 표시한다. */
const 기한후유예영업일 = 5;

/** 계산 대상 연도. 작년 미완료 건도 요약에는 잡힌다. */
function targetYears(today) {
  const y = D.year(today);
  return [y - 1, y, y + 1];
}

/**
 * 발생 이벤트를 만든다. 시작일이 주어지면 그보다 기한이 이른 건은 아예 만들지 않는다.
 * @param {object} amounts key '연도\tid' → AmountRecord
 */
function buildOccurrences(items, cal, today, amounts, 시작일) {
  const result = [];
  for (const year of targetYears(today)) {
    for (const item of items) {
      // 유효연도 밖의 해에는 기한이 없다.
      if (!해당연도(item, year)) continue;

      const occ = { Item: item, 연도: year, 공휴일자료없음: false, 실제금액: null };
      occ.원기한일 = 원기한일(item, year);
      occ.보정기한일 = cal.nextBusinessDayOrSame(occ.원기한일);

      // 추적 시작일 이전 건은 이 프로그램의 관심사가 아니다.
      if (시작일 && occ.보정기한일.getTime() < 시작일.getTime()) continue;

      occ.알림일 = cal.subtractBusinessDays(occ.보정기한일, item.알림영업일);

      // 공휴일 자료가 없으면 늦는 쪽이 위험하므로 여유를 두고 앞당긴다.
      if (!cal.hasYear(year)) {
        occ.공휴일자료없음 = true;
        occ.알림일 = D.addDays(occ.알림일, -안전여유일);
      }

      occ.Key = year + "\t" + item.Id;
      if (amounts && Object.prototype.hasOwnProperty.call(amounts, occ.Key)) occ.실제금액 = amounts[occ.Key];

      result.push(occ);
    }
  }
  return result;
}

/**
 * 오늘 팝업에 띄울 행과, 유예를 넘긴 미처리 건을 나눠 돌려준다.
 * 강제 표시 조건: 알림일 <= 오늘 <= 기한 + 유예, 최종 단계 아님, 오늘 미확인.
 */
function buildRows(occurrences, statusMap, cal, today) {
  const set = { Rows: [], Overdue: [] };

  for (const occ of occurrences) {
    if (occ.알림일.getTime() > today.getTime()) continue; // 아직 알릴 때가 아님

    let st = statusMap[occ.Key];
    if (!st) {
      st = { 연도: occ.연도, Id: occ.Item.Id, 단계: 0, 변경일시: null, 최종확인일: null, 메모: "" };
      statusMap[occ.Key] = st;
    }

    // 단계 값이 범위를 벗어나면 보정한다. 잘못된 파일 때문에 죽지 않는다.
    const last = Stages.finalIndex(occ.Item);
    if (st.단계 < 0) st.단계 = 0;
    if (st.단계 > last) st.단계 = last;

    if (st.단계 >= last) continue; // 이미 끝난 건

    const row = { Occ: occ, Status: st };

    // 기한 + 유예를 넘겼으면 강제 표시하지 않고 요약으로 돌린다.
    const cutoff = addBusinessDays(cal, occ.보정기한일, 기한후유예영업일);
    if (today.getTime() > cutoff.getTime()) {
      set.Overdue.push(row);
      continue;
    }

    // 오늘 이미 확인한 건은 다시 묻지 않는다.
    if (st.최종확인일 && st.최종확인일.getTime() === today.getTime()) continue;

    set.Rows.push(row);
  }

  set.Rows.sort(byDueDate);
  set.Overdue.sort(byDueDate);
  return set;
}

function byDueDate(a, b) {
  const c = a.Occ.보정기한일.getTime() - b.Occ.보정기한일.getTime();
  if (c !== 0) return c < 0 ? -1 : 1;
  const x = a.Occ.Item.Id, y = b.Occ.Item.Id;
  return x < y ? -1 : x > y ? 1 : 0;
}

function addBusinessDays(cal, from, n) {
  let x = from, moved = 0, guard = 0;
  while (moved < n) {
    x = D.addDays(x, 1);
    if (cal.isBusinessDay(x)) moved++;
    if (++guard > 1000) break;
  }
  return x;
}

module.exports = { targetYears, buildOccurrences, buildRows, addBusinessDays, 기한후유예영업일, 안전여유일 };
