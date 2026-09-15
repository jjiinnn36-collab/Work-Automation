# ADR-0007 웹 화면을 shadcn/ui 로, Next.js 정적 내보내기로 배포

- 상태: 채택
- 날짜: 2026-09-16
- 관련: 사용자 지시(2026-09-16) "shadcn/ui 로 전체 UI, 기본 컴포넌트 적극 활용",
  AC-W11(외부 의존 0건), AC-W115, 명세의 `design_mb.md` 모노톤 지시(대체됨)

## 맥락

2026-09-15 웹 화면은 손으로 쓴 HTML·CSS·JS 3개 파일이었다. 사용자는 전체 UI 를 shadcn/ui 로 다시 만들라고 했고,
직접 `jbam-web/`(Next.js 16 + shadcn `base-nova` 스타일, Base UI 기반 컴포넌트 60여 개)을 만들어 두었다.
제약:
- 회사 PC 는 **추가 설치 0건** — Node 를 깔 수 없다. 서버는 여전히 `PaymentAlert.exe`(HttpListener).
- **외부 의존 0건** — CDN·웹폰트를 부르면 사내에서 화면이 깨질 수 있다.

## 결정

- `jbam-web` 을 **정적 내보내기**(`output: "export"`)로 빌드하고 결과(`out/`)를 `web/` 에 복사한다 (`build-web.bat`).
  `web/` 은 **저장소에 올린다** — 회사 PC 는 빌드 없이 exe 와 web 폴더만 있으면 된다.
- 화면은 한 페이지 + 해시 경로(`#alerts #month #year #items #history #settings`). 서버는 index.html 하나와 `_next/` 자산만 내주면 된다.
- `next/font/google` 을 걷어 내고 시스템 글꼴(Pretendard 가 있으면 먼저, 없으면 Segoe UI·맑은 고딕)을 쓴다.
- shadcn 기본 컴포넌트를 그대로 쓴다: Sidebar(아이콘 접힘), Card, Table, Badge, Button/ButtonGroup, Dialog, AlertDialog,
  Sheet, Tabs, DropdownMenu, NativeSelect, Input/InputGroup, Field, Checkbox, Tooltip, Toast, Empty, Skeleton, Alert, Spinner.
  색은 shadcn neutral 테마를 그대로 두고 **토큰 두 개만 더했다**:
  - `--action`(파랑): "여기 손대야 한다" — 지금 할 지점 도형과 금액 미확인에만 (AC-W46).
  - 기한 지남은 shadcn `destructive`.
  밝은/어두운 화면 모두 지원(next-themes).
- 명세의 `design_mb.md` 모노톤·필 기하 지시는 이 결정으로 대체한다. 기능 규칙(AC-W45 도형 세 가지, W49 행동 문구,
  W53~W57 묶음·정렬, W63 내려받기 위치, W66 버튼 이름, W89 빈 값 저장 등)은 그대로 지킨다.

### 서버 쪽 (WebServer.Static)
- web 폴더 안의 정해 둔 형식(html·js·css·txt·json·ico·svg·png·woff/woff2)만. 점으로 시작하는 경로·역슬래시·콜론·상위 폴더는 404.
  확장자가 없으면 `.html`.
- **CSP 는 해시 방식**: 정적 내보내기가 넣는 인라인 스크립트의 SHA-256 을 파일마다 계산해(수정 시각으로 캐시)
  `script-src 'self' 'sha256-…'` 로 허용한다. `'unsafe-inline'` 스크립트는 쓰지 않는다.
  스타일은 컴포넌트가 style 속성을 쓰므로 `style-src 'self' 'unsafe-inline'`.
- `_next/static/` 은 이름에 내용 해시가 있으므로 `immutable` 1년 캐시.

### 시험
- 화면 규칙(도형 상태·필터·정렬·분할 합계·CSV·링크 안전성)은 `jbam-web/lib/logic.ts` 에 모으고
  `node --test lib/logic.test.ts` 로 시험한다 (Node 타입 제거 기능, 추가 패키지 없음).
- 서버 시험 WEB-1 은 실제 빌드 결과로: CSP 해시가 인라인 스크립트와 전부 맞는지, index.html 이 부르는 `_next` 파일이 모두 있는지 본다.

## 결과

- 좋은 점: 일관된 컴포넌트, 접근성(키보드·스크린리더) 기본 제공, 어두운 화면.
- 치르는 값: 화면을 고치려면 Node 가 있는 PC 에서 `build-web.bat` 을 돌려 `web/` 을 다시 올려야 한다.
  `web/` 에 빌드 산출물(약 1MB)이 커밋된다.
- `jbam-web` 의 원래 `.git` 은 `.git-template-backup` 으로 이름만 바꿨다 (`decisions-needed.md` Q9).

## 검증

`jbam-web/lib/logic.test.ts` 9개 묶음, `WebTests` WEB-1.
