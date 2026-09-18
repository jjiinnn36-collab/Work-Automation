# ADR-0008 운영: 백업·기록 순환·경고·설정 화면·자동 시작

- 상태: 채택
- 날짜: 2026-09-16
- 관련: AC-W2, 명세 "미검증 리스크"(localhost 차단), `docs/decisions-needed.md` Q7

## 맥락

자료가 SQLite 파일 하나(ADR-0002)에 모이면서 그 파일이 망가지면 모든 기록을 잃는다.
`Store.Backup` 은 있었지만 아무도 부르지 않았다. `run.log` 는 끝없이 커졌다.
공휴일 경고는 팝업에만 떴고, 추적 시작일·인증키는 파일을 직접 고쳐야 했다.
웹서버 로그온 자동 시작(`install-web.bat`)은 명세에 있었지만 만들지 않았다.

## 결정

### 백업 (`Backups`)
- 팝업 실행·웹서버 시작 때 **하루 한 번** `납부알림-YYYY-MM-DD.db` 를 `VACUUM INTO` 로 만든다.
  `.tmp` 로 만든 뒤 이름을 바꿔 반쪽 사본이 오늘 사본으로 남지 않게 한다.
- 날짜별 사본은 **최근 30개**. 수동(`납부알림-수동-…`)·이관전 사본은 자동으로 지우지 않는다.
- 위치 기본값 `자료폴더\backups`. 실행 파일 옆 `backup-folder.txt` 로 바꿀 수 있다 (다른 디스크 권장 — Q7).
  2026-09-18: 설정 화면의 [변경] 으로 바꾼다. 브라우저는 폴더의 전체 경로를 알 수 없어 이 PC 의 웹 서버가 윈도 폴더 선택 창을 띄운다 (`POST /api/settings/backup-dir/pick`, `FolderPicker` — IFileOpenDialog). `POST /api/settings/backup-dir` (dir, 빈 값 = 기본 위치) 도 남겨 둔다. 사본에 실제 자료가 들어 있어 드라이브부터 쓴 이 PC 의 경로만 받고,
  네트워크(UNC·네트워크 드라이브)·클라우드 동기화 폴더(OneDrive·Dropbox·Google Drive·iCloud·MYBOX)·웹 화면 폴더 안은 거절한다. 쓰기 가능한지 확인한 뒤 저장하고, 이미 만든 사본은 옮기지 않는다.
- 설정 화면에 `지금 백업` 과 최근 사본 10개 목록.
- 백업 실패는 기록만 남기고 알림은 계속한다.

### 실행 기록 (`LogFile`)
- 1 MB 를 넘으면 `run.1.log` → `run.2.log` → `run.3.log` 로 밀어내고 3개만 보관.

### 경고 (`Warnings`)
- 공휴일 자료가 없는 연도, 90일 넘게 갱신 안 됨 — 팝업과 웹(`/api/alerts`, `/api/settings`)이 같은 문구.

### 설정 화면 API
- `GET /api/settings` — 판, 스키마, `PRAGMA quick_check` 결과, 자료·DB·기록·백업 경로, 백업 목록,
  인증키 **있음/없음만**(값은 돌려주지 않음), 공휴일 연도·건수·갱신일, 갱신 작업 상태, 경고.
- `POST /api/settings/start-date` (비우면 제한 없음, 변경은 events 에 `설정` 으로 기록)
- `POST /api/settings/apikey` (공백·제어문자 거절, 비우면 파일 삭제)
- `POST /api/holidays/refresh` — 외부 호출 수십 번이라 **뒤 스레드**에서 돌린다. 도는 중이면 409.
  사용자가 누른 갱신은 30일 주기와 무관하게 받는다.
- `POST /api/backup`, `GET /api/health`

### 자동 시작
- `install-web.bat`: 로그온 트리거 예약 작업 → schtasks ONLOGON → 시작프로그램 바로가기, 3단 폴백.
  `--web --no-browser` 로 알림 영역에만 뜬다.
- `check-env.bat` [15]: localhost 리스닝·접속 시험 (DLP·보안 프로그램 차단 확인).

## 결과

- 치르는 값: 사본 30개 × DB 크기(현재 수백 KB)만큼 디스크. 설치 스크립트는 **실행하지 않았다** (사용자 PC 설정 변경).

## 검증

`OpsTests` OPS-1~4, `WebTests` WEB-13 (가짜 공휴일 함수로 네트워크 없이 갱신 흐름 확인).
