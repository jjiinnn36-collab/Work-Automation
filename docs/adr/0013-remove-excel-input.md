# ADR-0013 엑셀 입력 경로를 없애고 편집은 웹 하나로

- 상태: 채택
- 날짜: 2026-09-16
- 관련: AC-W4(명세 Round 4 Contrarian), AC-W19, AC-W63

## 맥락

항목은 `data/payment-master-template.xlsx` → `convert-excel.bat` → `payment-master.tsv` → (DB 로) 흘렀다.
웹 명세는 편집 경로를 웹 하나로 모으고 엑셀은 **내려받기 전용**으로 돌리기로 했다.
두 경로가 함께 있으면 엑셀을 고치고 가져오기를 잊었을 때, 또는 웹에서 고친 항목을 엑셀 가져오기가 통째로 덮을 때
기록이 조용히 어긋난다. 이를 막으려 팝업에 "엑셀 양식이 더 최신" 경고까지 두고 있었다.

## 결정

- 지운다: `convert-excel.bat`, `tools/convert-excel.ps1`, `data/payment-master-template.xlsx`, `--import-master` 옵션,
  `Importer.항목다시가져오기`, 팝업의 엑셀 최신 경고.
- 남긴다: `import-notice.bat` + `--import-amounts` (부가세 납부서 판독의 배치 경로 — 금액만 합치고 지우지 않는다),
  옛 TSV 최초 이전(`Importer.최초이전`) — 이미 쓰던 PC 가 처음 새 판을 켤 때 필요하다.
- 처음 설치(옛 TSV 없음): `Importer.자료준비` 가 **빈 DB** 를 만들고 함께 배포하는 `data/holidays.tsv` 만 넣는다.
  예전에는 "납부 자료가 없습니다" 로 멈췄고, 그 상태로는 웹 화면조차 켤 수 없었다.
  팝업은 항목이 없으면 "웹 화면 '항목 관리' 에서 추가" 를 안내한다.
- 엑셀로 보는 길은 웹의 **엑셀 내려받기**(UTF-8 BOM CSV) — 이번 달·연간·이력에만 (AC-W63).

## 결과

- 치르는 값: 여러 항목을 한꺼번에 붙여 넣는 방법이 없다. 분할납부는 월 쉼표로 한 번에 만들 수 있다(ADR-0010).
  대량 입력이 다시 필요해지면 "CSV 가져오기" 를 웹에 추가하는 방향으로 논의한다.

## 검증

`DbTests` DB-13(금액 다시 가져오기), DB-19(빈 DB·공휴일·옛 TSV 이전).
