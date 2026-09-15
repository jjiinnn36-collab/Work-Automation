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
  stage: number
  stageName: string
  nextStage: string | null
  nextAction: string
  paid: boolean
  siteName: string
  siteUrl: string
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
  changedAt: string | null
  attachments: number
  memo: string
  doneAt?: string
  beforeStart?: boolean
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
  group: string
  paid: boolean
  /** 지점 이름 (첫 칸 = 시작 지점). 사용자설정 흐름이면 사용자가 정한 것. */
  stages: string[]
  /** 각 지점에 도달할 때 누르는 버튼 문구. 첫 칸은 빈 문자열. */
  actions: string[]
  /** 사용자설정 흐름에서 금액이 없는 건 */
  noAmount: boolean
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
  backups: { name: string; size: number; at: string }[]
  apiKeySet: boolean
  holidayYears: number[]
  holidayCount: number
  holidayUpdated: string | null
  holidayJob: { running: boolean; message: string | null }
  warnings: string[]
}
