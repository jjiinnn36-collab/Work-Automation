// 서버(src/WebServer*.cs)가 돌려주는 자료 모양. 이름은 서버 JSON 키와 같다.

export type Severity = "overdue" | "soon" | "normal" | "done" | "before"
export type FlowName = "신고납부" | "납부만" | "제출만" | "사용자설정"

export interface Occurrence {
  year: number
  id: string
  org: string
  name: string
  flow: FlowName
  stages: string[]
  /** 시작점(0번)을 화면에 그리지 않는 흐름 (신고 후 납부) */
  hideStart?: boolean
  stage: number
  stageName: string
  nextStage: string | null
  nextAction: string
  paid: boolean
  siteName: string
  siteUrl: string
  /** 기관명 옆 아이콘 주소: 제 주소, 없으면 같은 기관의 주소 (사용자 요청 2026-09-18) */
  orgSiteUrl?: string
  orgSiteName?: string
  group: string
  done: boolean
  due: string
  dueDow: string
  payDue: string
  payDow: string
  shifted: boolean
  alertDate: string
  daysLeft: number
  statusText: string
  severity: Severity
  amount: number | null
  amountText: string
  amountEntered: boolean
  amountRule: string
  confirmedToday: boolean
  /** 오늘 '오늘은 대기' 를 눌렀고 그 뒤 다른 동작이 없음 — 대기 취소를 보인다 (ADR-0022). */
  deferredToday?: boolean
  changedAt: string | null
  attachments: number
  memo: string
  doneAt?: string
  beforeStart?: boolean
  /** 가져온 차입건의 이자 회차 — 금액을 칸에서 바로 고친다 (ADR-0023) */
  loan?: boolean
}

export interface Head {
  today: string
  todayText: string
  startDate: string | null
}

export interface AlertsData extends Head {
  rows: Occurrence[]
  pending: number
  overdue: Occurrence[]
  notYet: number
  warnings: string[]
}

export interface MonthData extends Head {
  year: number
  month: number
  rows: Occurrence[]
  inProgress: number
  upcoming: number
  done: number
  overdue: number
  beforeStart: number
  total: number
  amountUnknown: number
}

export interface YearData extends Head {
  year: number
  count: number
  beforeStart: number
  past: number
  pastDone: number
  inProgress: number
  upcoming: number
  overdue: number
  amountUnknown: number
  remaining: Occurrence[]
  finished: Occurrence[]
  orgs: string[]
}

export interface Item {
  id: string
  org: string
  name: string
  flow: FlowName
  month: number
  day: string
  lead: number
  rule: "고정" | "변동"
  fixed: number | null
  memo: string
  siteName: string
  siteUrl: string
  /** 기관명 옆 아이콘 주소: 제 주소, 없으면 같은 기관의 주소 (사용자 요청 2026-09-18) */
  orgSiteUrl?: string
  orgSiteName?: string
  group: string
  paid: boolean
  /** 지점 이름 (첫 칸 = 시작 지점). 사용자설정 흐름이면 사용자가 정한 것. */
  stages: string[]
  hideStart?: boolean
  /** 각 지점에 도달할 때 누르는 버튼 문구. 첫 칸은 빈 문자열. */
  actions: string[]
  /** 사용자설정 흐름에서 금액이 없는 건 */
  noAmount: boolean
  /** 가져온 차입건의 이자 회차 (ADR-0023) */
  loan: boolean
  /** 기한이 생기는 첫 해·마지막 해. null 이면 제한 없음 (ADR-0023) */
  startYear: number | null
  endYear: number | null
  thisYearAmount: number | null
  thisYearEntered: boolean
  thisYearUnknown: boolean
}

export interface ItemsData extends Head {
  items: Item[]
}

export interface HistoryData extends Head {
  from: string
  to: string
  rows: Occurrence[]
  total: number
}

export interface AttachmentFile {
  file: string
  name: string
  stage: string
  kind: "증빙" | "받은문서"
  at: string
}

/** 차입건(묶음)에 한 부씩 붙는 원본 스케줄 CSV. 그 차입의 어느 회차에서 열어도 같은 목록이 보인다. */
export interface LoanDocFile {
  file: string
  name: string
  year: number
  at: string
}

/** 차입건을 지우기 전에 무엇이 함께 지워지는지. */
export interface LoanSummary {
  group: string
  org: string
  name: string
  short: string
  start: string
  items: number
  amounts: number
  status: number
  events: number
  docs: number
  otherDocs: number
}

// ── 차입 스케줄 가져오기 (ADR-0023) ──

export interface LoanRowPart {
  date: string
  amount: number
}

export interface LoanPayment {
  no: number
  /** 저장된(또는 저장될) 회차 id. 처음 보는 차입건이면 null */
  id: string | null
  /** 이자 기간 시작 = 직전 지급일(첫 회차는 차입일) */
  from: string
  /** ERP 스케줄의 지급일 */
  scheduled: string
  /** 휴일이면 다음 영업일 */
  payDue: string
  shifted: boolean
  amount: number
  /** 액면×이율÷4 와 다름 */
  unexpected: boolean
  rows: LoanRowPart[]
  state: "new" | "exists" | "diff"
  existingAmount: number | null
}

export interface LoanPreview {
  sheet: string
  ok: boolean
  error?: string
  /** ERP 거래처명 */
  org?: string
  /** 사용자가 넣은 차입명·약칭 (알던 차입건이면 저장된 값) */
  name?: string
  short?: string
  start?: string
  face?: number
  rate?: number | null
  maturity?: string | null
  interestTotal?: number
  quarterExpected?: number | null
  /** 이미 가져온 적 있는 차입건 */
  known?: boolean
  payments?: LoanPayment[]
  newCount?: number
  existingCount?: number
  diffCount?: number
  warnings?: string[]
  selected?: boolean
  /** 저장할 때 원본 스케줄을 보관한 결과: "새로" · "같음" · "실패" */
  doc?: string
  group?: string
  added?: number
  /** 연장스케줄 업로드일 때 연장이 맞는지 본 항목 */
  checks?: { label: string; value: string; level: "ok" | "warn" }[]
}

export interface LoanImportResult {
  file: string
  mode: "preview" | "save"
  loans: LoanPreview[]
  skipped: { sheet: string; reason: string }[]
  added?: number
  existing?: number
  popup?: boolean
  extend?: { group: string; org: string; maturity: string | null; count: number }
}

export interface LoanSummary {
  group: string
  /** 화면의 차입처 이름: 약칭, 없으면 차입명, 없으면 ERP 거래처 */
  org: string
  name: string
  short: string
  start: string
  face: number
  rate: number | null
  maturity: string | null
  count: number
  file: string
}

export interface LoansData extends Head {
  loans: LoanSummary[]
}

export interface GroupData {
  year: number
  group: string
  org: string
  name: string
  rows: Occurrence[]
  total: number
  entered: number
  count: number
}

export interface EventRow {
  at: string
  year: number
  id: string
  name: string
  org: string
  /** 기관명 옆 아이콘 주소: 제 주소, 없으면 같은 기관의 주소 (사용자 요청 2026-09-18) */
  orgSiteUrl?: string
  orgSiteName?: string
  action: string
  source: string
  detail: string
  from: string | null
  to: string | null
}

export interface SettingsData extends Head {
  version: string
  schema: number
  integrity: string
  dataDir: string
  dbPath: string
  logPath: string
  backupDir: string
  /** 기본 위치 (자료 폴더 안 backups) */
  backupDirDefault: string
  /** 설정 화면에서 위치를 따로 정했는가 */
  backupDirCustom: boolean
  backups: { name: string; size: number; at: string }[]
  apiKeySet: boolean
  holidayYears: number[]
  holidayCount: number
  holidayUpdated: string | null
  holidayJob: { running: boolean; message: string | null }
  warnings: string[]
}
