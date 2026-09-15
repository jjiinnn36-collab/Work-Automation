'use strict';
// 납부 기한 알림 웹 화면. 자료는 모두 같은 PC 의 PaymentAlert.exe --web 서버에서 받는다.
(function () {
  const main = document.getElementById('main');
  const dlg = document.getElementById('dlg');
  const toastEl = document.getElementById('toast');
  const badge = document.getElementById('badge');

  const S = { view: 'alerts', ym: null, year: null, org: '', status: '', q: '', from: null, to: null };
  const occ = new Map();      // "연도|id" → 마지막으로 받은 발생 건
  let items = new Map();      // id → 항목
  let yearData = null;        // 연간 목록 다시 그리기용
  let csv = null;             // 지금 화면의 내려받기 자료
  let dirty = false;          // 대화상자에서 무엇을 바꿨으면 닫을 때 다시 읽는다
  let renderSeq = 0;

  // ── 도움 함수 ─────────────────────────────────────────────
  const ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  const esc = v => String(v == null ? '' : v).replace(/[&<>"']/g, c => ESC[c]);
  const md = d => (d ? d.slice(5) : '');
  const won = n => (n == null ? '' : Number(n).toLocaleString('ko-KR'));
  const k = o => ` data-y="${o.year}" data-id="${esc(o.id)}"`;
  const remember = list => list.forEach(o => occ.set(o.year + '|' + o.id, o));

  let toastTimer = 0;
  function toast(msg, bad) {
    toastEl.textContent = msg;
    toastEl.classList.toggle('bad', !!bad);
    toastEl.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { toastEl.hidden = true; }, bad ? 4500 : 2400);
  }

  async function readJson(r) {
    let j = null;
    try { j = await r.json(); } catch (e) { /* 본문 없음 */ }
    if (!r.ok) throw new Error((j && j.error) || '요청을 처리하지 못했습니다 (' + r.status + ')');
    return j;
  }
  const clean = o => { const r = {}; Object.keys(o || {}).forEach(x => { if (o[x] != null) r[x] = o[x]; }); return r; };
  async function get(path, params) {
    const q = params ? '?' + new URLSearchParams(clean(params)) : '';
    return readJson(await fetch(path + q, { cache: 'no-store' }));
  }
  async function post(path, data) {
    return readJson(await fetch(path, {
      method: 'POST',
      headers: { 'X-PaymentAlert': '1', 'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8' },
      body: new URLSearchParams(clean(data))
    }));
  }

  function setBadge(n) {
    badge.hidden = !n;
    badge.textContent = n || '';
  }

  // ── 조각 ─────────────────────────────────────────────────
  function head(title, sub, d) {
    return `<header class="head"><h1>${esc(title)}</h1><span class="sub">${sub}</span><span class="date">${esc(d.todayText)}</span></header>`;
  }

  function stat(key, value, desc, alert, small) {
    return `<div class="stat${alert ? ' alert' : ''}"><div class="k">${esc(key)}</div><div class="v${small ? ' sm' : ''}">${esc(value)}</div>${desc != null ? `<div class="d">${esc(desc)}</div>` : ''}</div>`;
  }

  function names(list) {
    if (!list.length) return '없음';
    const head = list.slice(0, 3).map(o => o.name).join(' · ');
    return list.length > 3 ? head + ` 외 ${list.length - 3}건` : head;
  }

  // 지난 단계는 회색, 지금 할 단계는 파랑, 남은 단계는 빈 점. 선은 지금 할 단계까지 채운다.
  function steps(o, bare) {
    const n = o.stages.length;
    const cur = o.done ? n : o.stage + 1;
    let h = `<div class="steps${bare ? ' bare' : ''}" role="img" aria-label="${esc(stepNo(o))}">`;
    o.stages.forEach((s, i) => {
      const cls = i < cur ? 'past' : (i === cur ? 'cur' : 'next');
      const fill = i > 0 && (o.done || i <= cur) ? ' fill' : '';
      h += `<div class="step ${cls}${fill}"><span class="lbl">${esc(s)}</span><span class="dot"></span></div>`;
    });
    return h + '</div>';
  }

  function stepNo(o) {
    const n = o.stages.length;
    return o.done ? `${n}/${n} 완료` : `${o.stage + 2}/${n} ${o.nextStage}`;
  }

  function amountBtn(o) {
    const inner = o.amount != null
      ? `${won(o.amount)}원`
      : (o.amountText ? '<span class="blue">금액 미확인</span>' : '<span class="faint">금액 없음</span>');
    return `<button type="button" class="amt" data-act="amount"${k(o)} title="금액 입력">${inner}</button>`;
  }

  function actions(o, sm) {
    const c = sm ? ' sm' : '';
    let h = '';
    if (!o.done) h += `<button type="button" class="pill primary${c}" data-act="advance"${k(o)}>${esc(o.nextStage)}</button>`;
    if (!o.done && !sm) {
      h += o.confirmedToday
        ? '<span class="tag">오늘 대기함</span>'
        : `<button type="button" class="pill${c}" data-act="defer"${k(o)}>오늘은 대기</button>`;
    }
    if (o.stage > 0) h += `<button type="button" class="pill${c}" data-act="revert"${k(o)}>되돌리기</button>`;
    h += `<button type="button" class="pill${c}" data-act="docs"${k(o)}>${o.attachments ? '증빙 ' + o.attachments + '건' : '증빙 첨부'}</button>`;
    return h;
  }

  function dueText(o) {
    return `원기한 ${md(o.due)} (${esc(o.dueDow)})${o.shifted ? ` &rarr; ${md(o.payDue)} (${esc(o.payDow)})` : ''}`;
  }

  function card(o) {
    return `<article class="card sev-${o.severity}${o.confirmedToday ? ' dim' : ''}"><div class="card-body">
      <div class="card-top"><span class="st">${esc(o.statusText)}</span><span class="org">${esc(o.org)}</span></div>
      <div><div class="due">${dueText(o)}</div><h3 class="name">${esc(o.name)}</h3></div>
      <div class="amt-line">${amountBtn(o)}<span class="stepno">${esc(stepNo(o))}</span></div>
      ${steps(o, false)}
      <div class="grow"></div>
      <div class="acts">${actions(o, false)}</div>
    </div></article>`;
  }

  function mrow(o) {
    const od = o.severity === 'overdue';
    const sub = o.done ? (o.changedAt ? md(o.changedAt.slice(0, 10)) + ' 처리 완료' : '처리 완료') : o.statusText;
    return `<div class="mrow${o.done ? ' done' : ''}${od ? ' od' : ''}">
      <div class="mdate"><div class="d">${md(o.due)}</div><div class="w">${esc(o.dueDow)}${o.shifted ? ' &rarr; ' + md(o.payDue) : ''}</div></div>
      <div class="mname"><div class="n">${esc(o.name)}</div><div class="s">${esc(o.org)} · <span class="${od ? 'red' : ''}">${esc(sub)}</span></div></div>
      <div class="mstep">${o.done ? `<span class="muted b">${esc(stepNo(o))}</span>` : steps(o, true) + `<div class="next">${esc(o.nextStage)}</div>`}</div>
      <div class="mamt">${amountBtn(o)}</div>
      <div class="macts">${actions(o, true)}</div>
    </div>`;
  }

  // ── 받은 알림 ────────────────────────────────────────────
  async function viewAlerts() {
    const d = await get('/api/alerts');
    remember(d.rows); remember(d.overdue);
    setBadge(d.pending);
    csv = null;

    const waiting = d.rows.length - d.pending;
    let sub = d.rows.length ? `알림일이 지난 ${d.rows.length}건` : '처리할 건 없음';
    if (waiting > 0) sub += ` · 오늘 대기 ${waiting}건`;

    let h = head('지금 처리할 건', esc(sub), d);
    h += d.rows.length
      ? `<div class="cards">${d.rows.map(card).join('')}</div>`
      : '<div class="empty">지금 처리할 건이 없습니다.</div>';

    if (d.overdue.length) {
      h += `<div class="sec"><h2>오래 밀린 건</h2><span class="warn">기한이 지나고 5영업일이 넘은 미처리 ${d.overdue.length}건</span></div>
            <div class="mlist">${d.overdue.map(mrow).join('')}</div>`;
    }

    h += `<div class="note"><span>올해 나머지 <b>${d.notYet}건</b>은 아직 알림일이 아닙니다 &mdash; <a href="#month">이번 달</a>과 <a href="#year">연간</a>에서 볼 수 있습니다.</span></div>`;
    return h;
  }

  // ── 이번 달 ──────────────────────────────────────────────
  function shiftMonth(ym, n) {
    const p = ym.split('-').map(Number);
    const d = new Date(p[0], p[1] - 1 + n, 1);
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0');
  }

  async function viewMonth() {
    const d = await get('/api/month', { ym: S.ym });
    S.ym = d.year + '-' + String(d.month).padStart(2, '0');
    remember(d.rows);
    const prog = d.rows.filter(o => !o.done);
    const done = d.rows.filter(o => o.done);
    const od = prog.filter(o => o.severity === 'overdue');
    csv = occCsv(`납부기한_${S.ym}`, d.rows);

    const thisMonth = S.ym === d.today.slice(0, 7);
    let h = head('이번 달', esc(`${d.year}년 ${d.month}월`), d);
    h += `<div class="stats s3">
      ${stat('진행중', d.inProgress + '건', names(prog))}
      ${stat('완료', d.done + '건', names(done))}
      ${stat('기한 지남', d.overdue + '건', od.length ? od.map(o => md(o.due) + ' ' + o.name).join(' · ') : '없음', d.overdue > 0)}
    </div>`;
    h += `<div class="toolbar">
      <button type="button" class="pill" data-act="month-prev" aria-label="이전 달">&lsaquo;</button>
      <span class="tag num">${d.year}년 ${d.month}월</span>
      <button type="button" class="pill" data-act="month-next" aria-label="다음 달">&rsaquo;</button>
      ${thisMonth ? '' : '<button type="button" class="pill soft" data-act="month-now">이번 달로</button>'}
      <span class="spacer"></span>
      <button type="button" class="pill" data-act="csv">엑셀 내려받기</button>
    </div>`;
    h += `<div class="total">이 달 납부 합계 <b>${won(d.total)}원</b>${d.amountUnknown ? ` · <span class="blue b">금액 미확인 ${d.amountUnknown}건</span>` : ''}</div>`;

    h += `<div class="sec"><h2>진행중</h2><span class="sub">${prog.length}건</span>${od.length ? `<span class="warn">기한 지남 ${od.length}건</span>` : ''}</div>`;
    h += prog.length ? `<div class="mlist">${prog.map(mrow).join('')}</div>` : '<div class="empty">진행중인 건이 없습니다.</div>';
    h += `<div class="sec"><h2>완료</h2><span class="sub">${done.length}건</span></div>`;
    h += done.length ? `<div class="mlist">${done.map(mrow).join('')}</div>` : '<div class="empty">아직 끝난 건이 없습니다.</div>';
    return h;
  }

  // ── 연간 ─────────────────────────────────────────────────
  function statusOf(o, today) {
    if (o.done) return 'done';
    if (o.severity === 'overdue') return 'overdue';
    return o.alertDate <= today ? 'progress' : 'upcoming';
  }

  function yrow(o) {
    const od = o.severity === 'overdue';
    return `<tr class="${od ? 'od' : ''}${o.done ? ' done' : ''}">
      <td class="num strong nowrap dday">${md(o.due)}${o.shifted ? `<span class="faint sm"> &rarr; ${md(o.payDue)}</span>` : ''}</td>
      <td class="sm muted">${esc(o.org)}</td>
      <td class="strong">${esc(o.name)}<div class="sm ${od ? 'red' : 'muted'}">${esc(o.statusText)}</div></td>
      <td class="sm strong nowrap">${esc(stepNo(o))}</td>
      <td class="r nowrap">${amountBtn(o)}</td>
      <td class="nowrap">${o.done ? '' : `<button type="button" class="link" data-act="advance"${k(o)}>${esc(o.nextStage)}</button> · `}<button type="button" class="link" data-act="docs"${k(o)}>증빙${o.attachments ? ' ' + o.attachments : ''}</button></td>
    </tr>`;
  }

  function ytable(rows, empty) {
    if (!rows.length) return `<div class="empty">${esc(empty)}</div>`;
    return `<div class="tablewrap"><table class="grid"><thead><tr><th>기한</th><th>기관</th><th>비용명</th><th>진행</th><th class="r">금액</th><th>바로가기</th></tr></thead><tbody>${rows.map(yrow).join('')}</tbody></table></div>`;
  }

  function yearLists() {
    const d = yearData;
    const q = S.q.trim().toLowerCase();
    const match = o => (!S.org || o.org === S.org) && (!S.status || statusOf(o, d.today) === S.status) &&
      (!q || o.name.toLowerCase().includes(q) || o.org.toLowerCase().includes(q));
    const rem = d.remaining.filter(match);
    const fin = d.finished.filter(match);
    const rp = rem.filter(o => statusOf(o, d.today) === 'progress').length;
    const ru = rem.filter(o => statusOf(o, d.today) === 'upcoming').length;
    const ro = rem.filter(o => o.severity === 'overdue').length;
    csv = occCsv(`납부기한_${d.year}년`, rem.concat(fin));
    return `<div class="sec"><h2>남은 건</h2><span class="sub">${rem.length}건 · 진행중 ${rp} · 진행예정 ${ru}</span>${ro ? `<span class="warn">기한 지남 ${ro}건</span>` : ''}</div>
      ${ytable(rem, '남은 건이 없습니다.')}
      <div class="sec"><h2>끝난 건</h2><span class="sub">${fin.length}건 · 최근 것이 위로</span></div>
      ${ytable(fin, '끝난 건이 없습니다.')}`;
  }

  async function viewYear() {
    const d = await get('/api/year', { y: S.year });
    S.year = d.year;
    yearData = d;
    remember(d.remaining); remember(d.finished);
    if (S.org && d.orgs.indexOf(S.org) < 0) S.org = '';

    const inProg = d.remaining.filter(o => statusOf(o, d.today) === 'progress');
    const upcoming = d.remaining.filter(o => statusOf(o, d.today) === 'upcoming');
    const od = d.remaining.filter(o => o.severity === 'overdue');
    const unknown = d.remaining.concat(d.finished).filter(o => o.amount == null && o.amountText);
    const opt = (v, label, cur) => `<option value="${esc(v)}"${v === cur ? ' selected' : ''}>${esc(label)}</option>`;

    let h = head('연간', esc(`${d.year}년 · 전체 ${d.count}건`), d);
    h += `<div class="stats">
      ${stat('지난 건', d.past + '건', `완료 ${d.pastDone} · 지남 ${d.past - d.pastDone}`)}
      ${stat('진행중', d.inProgress + '건', names(inProg))}
      ${stat('진행예정', d.upcoming + '건', upcoming.length ? '가장 이른 건 ' + md(upcoming[0].due) : '없음')}
      ${stat('기한 지남', d.overdue + '건', od.length ? od.map(o => md(o.due)).join(' · ') : '없음', d.overdue > 0)}
      ${stat('금액 미확인', d.amountUnknown + '건', names(unknown))}
    </div>`;
    h += `<div class="toolbar">
      <button type="button" class="pill" data-act="year-prev" aria-label="이전 해">&lsaquo;</button>
      <span class="tag num">${d.year}년</span>
      <button type="button" class="pill" data-act="year-next" aria-label="다음 해">&rsaquo;</button>
      <select class="field" data-filter="org" aria-label="기관">${opt('', '기관 전체', S.org)}${d.orgs.map(g => opt(g, g, S.org)).join('')}</select>
      <select class="field" data-filter="status" aria-label="상태">${opt('', '상태 전체', S.status)}${opt('progress', '진행중', S.status)}${opt('upcoming', '진행예정', S.status)}${opt('overdue', '기한 지남', S.status)}${opt('done', '완료', S.status)}</select>
      <input class="field search" type="search" data-filter="q" placeholder="비용명·기관 검색" value="${esc(S.q)}" aria-label="검색">
      <span class="spacer"></span>
      <button type="button" class="pill" data-act="csv">엑셀 내려받기</button>
    </div>`;
    h += `<div id="ylists">${yearLists()}</div>`;
    return h;
  }

  // ── 항목 관리 ────────────────────────────────────────────
  async function viewItems() {
    const d = await get('/api/items');
    items = new Map(d.items.map(i => [i.id, i]));
    csv = {
      name: '납부항목',
      header: ['id', '기관', '비용명', '진행흐름', '월', '기한일', '알림영업일', '금액규칙', '고정금액', '올해 금액', '비고'],
      rows: d.items.map(i => [i.id, i.org, i.name, i.flow, i.month, i.day, i.lead, i.rule, i.fixed, i.thisYearAmount, i.memo])
    };

    let h = `<header class="head"><h1>항목 관리</h1><span class="sub">전체 ${d.items.length}건</span>
      <span class="date">${esc(d.todayText)}</span></header>`;
    h += `<div class="toolbar"><div class="note grow"><span><b>기관·비용명·기한·진행흐름</b>은 여기서 정합니다. 그 해 실제 금액은 이번 달·연간 화면의 금액을 눌러 넣습니다.</span></div></div>`;
    h += `<div class="toolbar"><button type="button" class="pill primary" data-act="item-add">+ 항목 추가</button><span class="spacer"></span><button type="button" class="pill" data-act="csv">엑셀 내려받기</button></div>`;

    if (!d.items.length) return h + '<div class="empty">항목이 없습니다. 항목 추가로 시작하세요.</div>';

    const year = d.today.slice(0, 4);
    h += `<div class="tablewrap"><table class="grid"><thead><tr>
      <th>기관</th><th>비용명</th><th>흐름</th><th>월</th><th>기한일</th><th>알림</th><th>금액규칙</th><th class="r">${year}년 금액</th><th>작업</th>
      </tr></thead><tbody>${d.items.map(i => `<tr>
        <td class="sm muted">${esc(i.org)}</td>
        <td class="strong">${esc(i.name)}</td>
        <td class="sm nowrap">${esc(i.flow)}</td>
        <td class="sm num">${i.month}월</td>
        <td class="sm num nowrap">${i.day === '말일' ? '말일' : esc(i.day) + '일'}</td>
        <td class="sm num nowrap">${i.lead}영업일</td>
        <td class="sm muted">${esc(i.rule)}</td>
        <td class="r num nowrap">${i.thisYearAmount != null ? won(i.thisYearAmount) : (i.thisYearUnknown ? '<span class="blue sm b">미확인</span>' : '<span class="faint">&mdash;</span>')}</td>
        <td><button type="button" class="link" data-act="item-edit" data-item="${esc(i.id)}">수정</button></td>
      </tr>`).join('')}</tbody></table></div>`;
    return h;
  }

  // ── 이력 ─────────────────────────────────────────────────
  async function viewHistory() {
    const d = await get('/api/history', { from: S.from, to: S.to });
    S.from = d.from; S.to = d.to;
    remember(d.rows);
    csv = {
      name: `처리이력_${d.from}_${d.to}`,
      header: ['처리일', '연도', '원기한', '기관', '비용명', '진행흐름', '금액', '증빙'],
      rows: d.rows.map(o => [o.doneAt, o.year, o.due, o.org, o.name, o.flow, o.amount, o.attachments])
    };

    const whole = d.from.slice(5) === '01-01' && d.to.slice(5) === '12-31' && d.from.slice(0, 4) === d.to.slice(0, 4);
    const period = whole ? d.from.slice(0, 4) + '년' : `${d.from} ~ ${d.to}`;

    let h = head('이력', '처리가 끝난 건', d);
    h += `<div class="stats s2">${stat('조회 기간', period, null, false, !whole)}${stat('처리 완료', d.rows.length + '건')}</div>`;
    h += `<div class="toolbar">
      <span class="label">조회 기간</span>
      <input class="field num" type="date" id="hfrom" value="${esc(d.from)}" aria-label="시작일">
      <span class="faint">&mdash;</span>
      <input class="field num" type="date" id="hto" value="${esc(d.to)}" aria-label="끝일">
      <button type="button" class="pill dark" data-act="hist-query">조회</button>
      <button type="button" class="pill soft" data-act="hist-preset" data-preset="this">당해년도</button>
      <button type="button" class="pill soft" data-act="hist-preset" data-preset="last">전년도</button>
      <button type="button" class="pill soft" data-act="hist-preset" data-preset="12m">최근 12개월</button>
      <span class="spacer"></span>
      <button type="button" class="pill" data-act="csv">엑셀 내려받기</button>
    </div>`;

    if (d.rows.length) {
      h += `<div class="tablewrap"><table class="grid"><thead><tr><th>처리일</th><th>원기한</th><th>비용명</th><th>기관</th><th>흐름</th><th class="r">금액</th><th>증빙</th></tr></thead><tbody>
        ${d.rows.map(o => `<tr>
          <td class="num strong nowrap">${esc(o.doneAt)}</td>
          <td class="sm muted num nowrap">${o.year}-${md(o.due)}</td>
          <td class="strong">${esc(o.name)}</td>
          <td class="sm muted">${esc(o.org)}</td>
          <td class="sm muted nowrap">${esc(o.flow)}</td>
          <td class="r nowrap">${amountBtn(o)}</td>
          <td class="nowrap"><button type="button" class="link" data-act="docs"${k(o)}>${o.attachments ? '열기 ' + o.attachments + '건' : '첨부'}</button>
            &middot; <button type="button" class="link" data-act="revert"${k(o)}>되돌리기</button></td>
        </tr>`).join('')}
        <tr class="sum"><td colspan="5" class="sm muted">조회 기간 합계 · ${d.rows.length}건</td><td class="r num nowrap">${won(d.total)}</td><td></td></tr>
      </tbody></table></div>`;
    } else {
      h += '<div class="empty">이 기간에 처리가 끝난 건이 없습니다.</div>';
    }

    if (d.startDate) {
      h += `<div class="note"><span>추적 시작일이 <b>${esc(d.startDate)}</b> 입니다. 그 전 건은 진행 기록이 없어 이력에도 남지 않습니다 &mdash; 실제로 놓친 것이 아닙니다.</span></div>`;
    }
    return h;
  }

  // ── 내려받기 ─────────────────────────────────────────────
  function occCsv(name, rows) {
    return {
      name: name,
      header: ['연도', '원기한', '실납부기한', '기관', '비용명', '진행흐름', '현재 단계', '상태', '금액', '증빙'],
      rows: rows.map(o => [o.year, o.due, o.payDue, o.org, o.name, o.flow, o.stageName, o.statusText,
        o.amount != null ? o.amount : o.amountText, o.attachments])
    };
  }

  function csvCell(v) {
    let s = String(v == null ? '' : v);
    if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;   // 엑셀이 수식으로 읽지 않게
    return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  }

  function download() {
    if (!csv) return;
    const text = '﻿' + [csv.header].concat(csv.rows).map(r => r.map(csvCell).join(',')).join('\r\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
    a.download = csv.name + '.csv';
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 0);
  }

  // ── 대화상자 ─────────────────────────────────────────────
  function modal(title, body, handlers) {
    dlg.innerHTML = `<form class="dlg" novalidate><h2>${esc(title)}</h2>${body}</form>`;
    const form = dlg.firstElementChild;
    const err = form.querySelector('[data-err]');
    const run = async (fn, btn) => {
      if (err) err.textContent = '';
      if (btn) btn.disabled = true;
      try {
        const keep = await fn(form, btn);
        if (!keep) dlg.close();
      } catch (e) {
        if (err) err.textContent = e.message; else toast(e.message, true);
      } finally {
        if (btn) btn.disabled = false;
      }
    };
    form.addEventListener('submit', e => {
      e.preventDefault();
      if (handlers.submit) run(handlers.submit, form.querySelector('[type=submit]'));
    });
    form.addEventListener('click', e => {
      const b = e.target.closest('[data-dlg]');
      if (!b) return;
      if (b.dataset.dlg === 'close') { dlg.close(); return; }
      if (handlers[b.dataset.dlg]) run(handlers[b.dataset.dlg], b);
    });
    dlg.showModal();
    const first = form.querySelector('input:not([readonly]):not([hidden]), select, textarea');
    if (first) first.focus();
    return form;
  }

  dlg.addEventListener('close', () => {
    dlg.innerHTML = '';
    if (dirty) { dirty = false; render(); }
  });

  function openAmount(o) {
    modal(`${o.name} 금액`, `
      <p class="desc">${o.year}년 · ${esc(o.org)}${o.amountRule ? ' · 금액규칙 ' + esc(o.amountRule) : ''}</p>
      <div class="form"><label class="wide">금액 (원)
        <input name="amount" inputmode="numeric" autocomplete="off" value="${o.amountEntered ? esc(won(o.amount)) : ''}"
          placeholder="${o.amount != null ? esc(won(o.amount)) + ' (고정금액)' : '예: 1,234,500'}">
      </label></div>
      <p class="err" data-err></p>
      <div class="dlg-foot">
        ${o.amountEntered ? '<button type="button" class="pill danger" data-dlg="remove">입력한 금액 지우기</button>' : ''}
        <span class="spacer"></span>
        <button type="button" class="pill" data-dlg="close">취소</button>
        <button type="submit" class="pill primary">저장</button>
      </div>`, {
      submit: async form => {
        await post('/api/amount', { y: o.year, id: o.id, amount: form.elements.amount.value });
        dirty = true;
        toast('금액을 저장했습니다');
      },
      remove: async () => {
        await post('/api/amount/delete', { y: o.year, id: o.id });
        dirty = true;
        toast('입력한 금액을 지웠습니다');
      }
    });
  }

  function openDocs(o) {
    const form = modal(`${o.name} 증빙`, `
      <p class="desc">${o.year}년 · ${esc(o.org)} · 지금 단계 ${esc(o.stageName)}</p>
      <ul class="files" data-files><li class="muted">불러오는 중…</li></ul>
      <label class="drop" data-drop>파일을 여기로 끌어 놓거나 <span class="blue b">골라서 첨부</span>
        <input type="file" multiple hidden data-file></label>
      <p class="err" data-err></p>
      <div class="dlg-foot"><span class="spacer"></span><button type="button" class="pill" data-dlg="close">닫기</button></div>`, {
      remove: async (f, btn) => {
        if (!confirm('이 증빙을 지울까요? 보관한 파일도 함께 지워집니다.')) return true;
        await post('/api/attach/delete', { y: o.year, id: o.id, f: btn.dataset.f });
        dirty = true;
        await load();
        return true;
      }
    });

    const list = form.querySelector('[data-files]');
    const err = form.querySelector('[data-err]');
    const drop = form.querySelector('[data-drop]');
    const input = form.querySelector('[data-file]');

    async function load() {
      const r = await get('/api/attachments', { y: o.year, id: o.id });
      list.innerHTML = r.files.length ? r.files.map(f => {
        const href = '/api/file?' + new URLSearchParams({ y: o.year, id: o.id, f: f.file });
        return `<li><span class="fn" title="${esc(f.name)}">${esc(f.name)}</span><span class="meta">${esc(f.stage)} · ${esc(f.at)}</span>
          <a class="link" href="${esc(href)}" target="_blank" rel="noopener">열기</a>
          <button type="button" class="link" data-dlg="remove" data-f="${esc(f.file)}">삭제</button></li>`;
      }).join('') : '<li class="muted">첨부한 증빙이 없습니다.</li>';
    }

    async function upload(files) {
      if (!files.length) return;
      err.textContent = '';
      drop.classList.add('over');
      try {
        for (const f of files) {
          const r = await fetch('/api/attach?' + new URLSearchParams({ y: o.year, id: o.id }), {
            method: 'POST',
            headers: { 'X-PaymentAlert': '1', 'X-File-Name': encodeURIComponent(f.name), 'Content-Type': 'application/octet-stream' },
            body: f
          });
          await readJson(r);
        }
        dirty = true;
        toast(files.length + '개 첨부했습니다');
      } catch (e) {
        err.textContent = e.message;
      } finally {
        drop.classList.remove('over');
        await load().catch(() => {});
      }
    }

    input.addEventListener('change', () => { upload(Array.from(input.files)); input.value = ''; });
    drop.addEventListener('dragover', e => { e.preventDefault(); drop.classList.add('over'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('over'));
    drop.addEventListener('drop', e => { e.preventDefault(); upload(Array.from(e.dataTransfer.files)); });
    load().catch(e => { err.textContent = e.message; });
  }

  function openItem(it) {
    const isNew = !it;
    it = it || { id: '', org: '', name: '', flow: '납부만', month: new Date().getMonth() + 1, day: '말일', lead: 3, rule: '', fixed: null, memo: '' };
    const opt = (v, cur, label) => `<option value="${esc(v)}"${String(v) === String(cur) ? ' selected' : ''}>${esc(label)}</option>`;
    const months = Array.from({ length: 12 }, (_, i) => opt(i + 1, it.month, (i + 1) + '월')).join('');
    const days = ['말일'].concat(Array.from({ length: 31 }, (_, i) => String(i + 1)))
      .map(x => opt(x, it.day, x === '말일' ? '말일' : x + '일')).join('');

    modal(isNew ? '항목 추가' : '항목 수정', `
      <div class="form">
        <label>id <span class="hint">영문·숫자·_·- , 만든 뒤 못 바꿈</span>
          <input name="id" value="${esc(it.id)}" ${isNew ? 'maxlength="40" placeholder="예: vat-q3" autocomplete="off"' : 'readonly'}></label>
        <label>진행흐름<select name="flow">${['신고납부', '납부만', '제출만'].map(f => opt(f, it.flow, f)).join('')}</select></label>
        <label>기관<input name="org" value="${esc(it.org)}" maxlength="60"></label>
        <label>비용명<input name="name" value="${esc(it.name)}" maxlength="60"></label>
        <label>월<select name="month">${months}</select></label>
        <label>기한일<select name="day">${days}</select></label>
        <label>알림 <span class="hint">기한 몇 영업일 전</span><input name="lead" type="number" min="1" max="60" value="${esc(it.lead)}"></label>
        <label>금액규칙<input name="rule" value="${esc(it.rule)}" maxlength="40" list="rules" placeholder="고정 · 변동 · 해당없음"></label>
        <label>고정금액 <span class="hint">원, 매년 같을 때</span><input name="fixed" inputmode="numeric" value="${it.fixed != null ? esc(won(it.fixed)) : ''}"></label>
        <label class="wide">비고<textarea name="memo" maxlength="200">${esc(it.memo)}</textarea></label>
      </div>
      <datalist id="rules"><option value="고정"><option value="변동"><option value="해당없음"></datalist>
      <p class="err" data-err></p>
      <div class="dlg-foot">
        ${isNew ? '' : '<button type="button" class="pill danger" data-dlg="remove">항목 삭제</button>'}
        <span class="spacer"></span>
        <button type="button" class="pill" data-dlg="close">취소</button>
        <button type="submit" class="pill primary">${isNew ? '추가' : '저장'}</button>
      </div>`, {
      submit: async form => {
        const data = {};
        new FormData(form).forEach((v, name) => { data[name] = v; });
        data.mode = isNew ? 'new' : 'edit';
        const r = await post('/api/item', data);
        dirty = true;
        const msg = isNew ? '항목을 추가했습니다' : '항목을 저장했습니다';
        toast(r.popup ? msg + ' · 오늘 알릴 건이라 알림 팝업을 띄웠습니다' : msg);
      },
      remove: async () => {
        if (!confirm(`'${it.name}' 항목을 지울까요?\n진행 기록·금액·증빙은 남아서, 같은 id 로 다시 만들면 이어집니다.`)) return true;
        await post('/api/item/delete', { id: it.id });
        dirty = true;
        toast('항목을 지웠습니다');
      }
    });
  }

  // ── 화면 전환 ────────────────────────────────────────────
  const views = { alerts: viewAlerts, month: viewMonth, year: viewYear, items: viewItems, history: viewHistory };

  async function render() {
    const seq = ++renderSeq;
    const view = S.view;
    try {
      const html = await views[view]();
      if (seq !== renderSeq) return;   // 그사이 다른 화면으로 넘어갔다
      const y = window.scrollY;
      main.innerHTML = html;
      window.scrollTo(0, y);
    } catch (e) {
      if (seq !== renderSeq) return;
      main.innerHTML = `<div class="empty">${esc(e.message)}</div>`;
    }
    if (view !== 'alerts') get('/api/alerts').then(d => setBadge(d.pending)).catch(() => {});
  }

  function route() {
    const h = location.hash.replace('#', '');
    const next = views[h] ? h : 'alerts';
    const changed = next !== S.view;
    S.view = next;
    document.querySelectorAll('.nav a').forEach(a => {
      const on = a.dataset.view === next;
      a.classList.toggle('on', on);
      if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    });
    if (changed) window.scrollTo(0, 0);
    render();
  }

  main.addEventListener('click', async e => {
    const b = e.target.closest('[data-act]');
    if (!b) return;
    const o = b.dataset.id ? occ.get(b.dataset.y + '|' + b.dataset.id) : null;
    const today = (yearData && yearData.today) || '';
    try {
      switch (b.dataset.act) {
        case 'advance':
          b.disabled = true;
          await post('/api/advance', { y: o.year, id: o.id });
          toast(`${o.name} · ${o.nextStage}`);
          await render();
          break;
        case 'defer':
          b.disabled = true;
          await post('/api/defer', { y: o.year, id: o.id });
          toast(`${o.name} · 오늘은 대기`);
          await render();
          break;
        case 'revert':
          if (!confirm(`${o.name} 을(를) '${o.stages[o.stage - 1]}' 단계로 되돌릴까요?`)) return;
          b.disabled = true;
          await post('/api/revert', { y: o.year, id: o.id });
          toast('되돌렸습니다');
          await render();
          break;
        case 'amount': openAmount(o); break;
        case 'docs': openDocs(o); break;
        case 'item-add': openItem(null); break;
        case 'item-edit': openItem(items.get(b.dataset.item)); break;
        case 'month-prev': S.ym = shiftMonth(S.ym, -1); render(); break;
        case 'month-next': S.ym = shiftMonth(S.ym, 1); render(); break;
        case 'month-now': S.ym = null; render(); break;
        case 'year-prev': S.year = S.year - 1; render(); break;
        case 'year-next': S.year = S.year + 1; render(); break;
        case 'csv': download(); break;
        case 'hist-query':
          S.from = document.getElementById('hfrom').value || null;
          S.to = document.getElementById('hto').value || null;
          render();
          break;
        case 'hist-preset': {
          const t = new Date();
          const ty = S.from ? Number(S.to.slice(0, 4)) : t.getFullYear();
          const base = today ? Number(today.slice(0, 4)) : ty;
          if (b.dataset.preset === 'this') { S.from = base + '-01-01'; S.to = base + '-12-31'; }
          else if (b.dataset.preset === 'last') { S.from = (base - 1) + '-01-01'; S.to = (base - 1) + '-12-31'; }
          else {
            const iso = d => d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
            const from = new Date(t.getFullYear() - 1, t.getMonth(), t.getDate() + 1);
            S.from = iso(from); S.to = iso(t);
          }
          render();
          break;
        }
      }
    } catch (err) {
      toast(err.message, true);
      b.disabled = false;
    }
  });

  function onFilter(e) {
    const f = e.target.dataset && e.target.dataset.filter;
    if (!f || !yearData) return;
    S[f] = e.target.value;
    const box = document.getElementById('ylists');
    if (box) box.innerHTML = yearLists();
  }
  main.addEventListener('change', onFilter);
  main.addEventListener('input', onFilter);

  // 팝업이나 다른 창에서 바꾼 내용을 돌아왔을 때 반영한다.
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible' && !dlg.open) render();
  });

  window.addEventListener('hashchange', route);
  route();
})();
