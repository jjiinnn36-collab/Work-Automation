"use client"

import * as React from "react"
import {
  AlertTriangleIcon,
  FileSpreadsheetIcon,
  UploadIcon,
} from "lucide-react"

import { errorMessage, upload } from "@/lib/api"
import {
  loanDefaultKeys,
  loanKey,
  loanNameParams,
  loanOverrides,
  loanSaveSheets,
  md,
  won,
  type LoanName,
} from "@/lib/logic"
import type { LoanImportResult, LoanPreview } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { BackButton } from "@/components/app/parts"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Spinner } from "@/components/ui/spinner"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { toast } from "@/components/ui/toast"

type Loaded = {
  file: File
  result: LoanImportResult | null
  error: string | null
}
/** 차입건(loanKey) → 지급일 → 사용자가 고친 지급액 글자 */
type Typed = Record<string, Record<string, string>>
/** 차입건(loanKey) → 사용자가 넣은 차입명·약칭 */
type Names = Record<string, LoanName>

export type LoanExtendTarget = { group: string; org: string }

/**
 * ERP 차입관리 스케줄(xlsx·csv)을 올려 분기 이자 지급 건으로 등록한다 (ADR-0023).
 * extend 를 주면 그 차입건에 연장 스케줄을 이어 붙인다 (연장스케줄 업로드).
 * 파일을 고르면 미리보기만 받고, 버튼을 눌러야 저장한다. 새 회차의 지급액은 표에서 바로 고친다.
 * onBack 을 주면 (항목 추가 → 차입금 이자) 왼쪽 위에 '뒤로가기' 를 둔다.
 */
export function LoanImportDialog({
  open,
  onOpenChange,
  extend,
  onBack,
}: {
  open: boolean
  onOpenChange: (v: boolean) => void
  extend?: LoanExtendTarget | null
  onBack?: () => void
}) {
  const app = useApp()
  const [files, setFiles] = React.useState<Loaded[]>([])
  const [selected, setSelected] = React.useState<string[]>([])
  const [typed, setTyped] = React.useState<Typed>({})
  const [names, setNames] = React.useState<Names>({})
  const [showMissing, setShowMissing] = React.useState(false)
  const [reading, setReading] = React.useState(false)
  const [saving, setSaving] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)
  const inputRef = React.useRef<HTMLInputElement>(null)

  React.useEffect(() => {
    if (open) {
      setFiles([])
      setSelected([])
      setTyped({})
      setNames({})
      setShowMissing(false)
      setError(null)
    }
  }, [open])

  /** 고른 파일이 있으면 버릴지 먼저 묻는다 (ADR-0022). */
  async function back() {
    if (
      files.length > 0 &&
      !(await app.confirm({
        title: "올린 파일을 버리고 돌아갈까요?",
        description: "아직 등록하지 않은 차입 스케줄이 사라집니다.",
        action: "뒤로가기",
        destructive: true,
      }))
    )
      return
    onBack?.()
  }

  const extendParam = extend ? { extend: extend.group } : {}

  async function pick(list: FileList | null) {
    if (!list || list.length === 0) return
    setReading(true)
    setError(null)
    const loaded: Loaded[] = []
    for (const f of Array.from(list)) {
      try {
        loaded.push({
          file: f,
          result: await upload<LoanImportResult>(
            "/api/import/loans",
            { mode: "preview", ...extendParam },
            f
          ),
          error: null,
        })
      } catch (e) {
        loaded.push({ file: f, result: null, error: errorMessage(e) })
      }
    }
    // 연장은 파일 한 개만. 처음 등록은 같은 이름의 파일을 다시 고르면 새 것으로 바꾼다.
    const next = extend
      ? loaded.slice(0, 1)
      : [
          ...files.filter(
            (x) => !loaded.some((l) => l.file.name === x.file.name)
          ),
          ...loaded,
        ]
    setFiles(next)
    // 알던 차입건은 저장된 이름으로 채운다. 이미 입력한 칸은 그대로.
    setNames((prev) => {
      const out = { ...prev }
      for (const x of loaded)
        for (const l of x.result?.loans ?? []) {
          const key = loanKey(x.file.name, l.sheet)
          // 차입명은 시트명으로 미리 채운다 (사용자 요청). 이미 저장된 이름이 있으면 그것을 쓴다.
          if (l.ok && !out[key]) out[key] = { name: l.name || l.sheet, short: l.short ?? "" }
        }
      return out
    })
    setSelected(
      loanDefaultKeys(
        next
          .filter((x) => x.result)
          .map((x) => ({ file: x.file.name, loans: x.result!.loans }))
      )
    )
    setReading(false)
    if (inputRef.current) inputRef.current.value = ""
  }

  function toggle(key: string, on: boolean) {
    setSelected((s) => (on ? [...s, key] : s.filter((k) => k !== key)))
  }

  function rename(key: string, patch: Partial<LoanName>) {
    setNames((n) => ({ ...n, [key]: { ...(n[key] ?? { name: "", short: "" }), ...patch } }))
    setError(null)
  }

  function type(key: string, date: string, value: string) {
    setTyped((t) => ({ ...t, [key]: { ...(t[key] ?? {}), [date]: value } }))
    setError(null)
  }

  const plan = files
    .filter((x) => x.result)
    .map((x) => {
      const sheets = loanSaveSheets(x.file.name, x.result!.loans, selected)
      const ov: string[] = []
      const invalid: string[] = []
      for (const l of x.result!.loans) {
        if (!sheets.includes(l.sheet)) continue
        const r = loanOverrides(
          l.sheet,
          l.payments ?? [],
          typed[loanKey(x.file.name, l.sheet)] ?? {}
        )
        ov.push(...r.ov)
        invalid.push(...r.invalid)
      }
      const bySheet: Record<string, LoanName | undefined> = {}
      for (const sh of sheets) bySheet[sh] = names[loanKey(x.file.name, sh)]
      // 연장은 차입건 이름이 이미 있어 보내지 않는다.
      const { nm, missing } = extend
        ? { nm: [] as string[], missing: [] as string[] }
        : loanNameParams(sheets, bySheet)
      return { file: x.file, sheets, ov, invalid, nm, missing }
    })
    .filter((p) => p.sheets.length > 0)
  const newTotal = files.reduce(
    (n, x) =>
      n +
      (x.result?.loans ?? [])
        .filter((l) => selected.includes(loanKey(x.file.name, l.sheet)))
        .reduce((m, l) => m + (l.newCount ?? 0), 0),
    0
  )

  async function save() {
    if (plan.length === 0) return
    if (plan.some((p) => p.missing.length > 0)) {
      setShowMissing(true)
      setError("차입명을 넣어 주세요.")
      return
    }
    if (plan.some((p) => p.invalid.length > 0)) {
      setError("지급액은 원 단위 숫자로 넣어 주세요.")
      return
    }
    setSaving(true)
    setError(null)
    let added = 0
    let popup = false
    let 보관 = 0     // 증빙에 새로 보관한 원본 스케줄 수
    try {
      for (const p of plan) {
        const r = await upload<LoanImportResult>(
          "/api/import/loans",
          { mode: "save", only: p.sheets.join(","), ov: p.ov, nm: p.nm, ...extendParam },
          p.file
        )
        added += r.added ?? 0
        popup = popup || r.popup === true
        보관 += (r.loans ?? []).filter((l) => l.doc === "새로").length
      }
      // 새 회차가 없어도 원본 스케줄은 보관된다 — 같은 파일을 다시 올려 증빙만 채우는 경우.
      const 보관말 = 보관 > 0 ? `원본 스케줄 ${보관}건을 증빙에 보관했습니다.` : undefined
      toast.add({
        title: extend
          ? `${extend.org} 연장 이자 ${added}건을 붙였습니다`
          : added > 0
            ? `차입금 이자 ${added}건을 등록했습니다`
            : `원본 스케줄 ${보관}건을 증빙에 보관했습니다`,
        description: popup
          ? "오늘 알릴 건이 있어 알림 팝업을 띄웠습니다."
          : added > 0
            ? 보관말
            : undefined,
        type: "success",
      })
      onOpenChange(false)
      app.refresh()
    } catch (e) {
      setError(errorMessage(e))
      app.refresh()
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} disablePointerDismissal onOpenChange={(v) => !saving && onOpenChange(v)}>
      <DialogContent className="sm:max-w-3xl">
        <DialogHeader>
          {onBack && !extend && <BackButton onClick={back} />}
          <DialogTitle>
            {extend ? `연장스케줄 업로드 · ${extend.org}` : "차입금 이자"}
          </DialogTitle>
          <DialogDescription render={<div />}>
            {extend ? (
              "ERP 연장 스케줄 파일을 올리면 이 차입건에 이자 일정을 이어 붙입니다."
            ) : (
              <ul className="flex list-disc flex-col gap-0.5 pl-4">
                <li>ERP 차입관리 스케줄 파일 (.xlsx · .csv)</li>
                <li>엑셀은 시트 1개 = 차입건 1건</li>
              </ul>
            )}
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-3 rounded-lg border border-dashed p-3">
          <FileSpreadsheetIcon className="size-5 text-muted-foreground" />
          <div className="min-w-0 flex-1 text-sm">
            {files.length === 0 ? (
              <span className="text-muted-foreground">파일을 고르세요.</span>
            ) : (
              <span className="font-medium">
                {files.map((x) => x.file.name).join(" · ")}
              </span>
            )}
          </div>
          <input
            ref={inputRef}
            type="file"
            accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            multiple={!extend}
            className="hidden"
            onChange={(e) => pick(e.target.files)}
          />
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={reading || saving}
            onClick={() => inputRef.current?.click()}
          >
            {reading ? <Spinner /> : <UploadIcon data-icon="inline-start" />}{" "}
            {files.length === 0
              ? "파일 고르기"
              : extend
                ? "다른 파일"
                : "파일 추가"}
          </Button>
        </div>

        <div className="flex max-h-[55vh] flex-col gap-4 overflow-y-auto pr-1">
          {files.map((x) => (
            <FileResult
              key={x.file.name}
              loaded={x}
              selected={selected}
              onToggle={toggle}
              typed={typed}
              onType={type}
              names={names}
              onRename={rename}
              showMissing={showMissing}
              extend={!!extend}
            />
          ))}
        </div>

        {error && <FieldError>{error}</FieldError>}
        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            disabled={saving}
            onClick={() => onOpenChange(false)}
          >
            취소
          </Button>
          <Button
            type="button"
            disabled={saving || reading || plan.length === 0}
            onClick={save}
          >
            {saving && <Spinner />}{" "}
            {extend ? "연장 등록" : newTotal === 0 ? "원본 보관" : "등록"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function FileResult({
  loaded,
  selected,
  onToggle,
  typed,
  onType,
  names,
  onRename,
  showMissing,
  extend,
}: {
  loaded: Loaded
  selected: string[]
  onToggle: (key: string, on: boolean) => void
  typed: Typed
  onType: (key: string, date: string, value: string) => void
  names: Names
  onRename: (key: string, patch: Partial<LoanName>) => void
  showMissing: boolean
  extend: boolean
}) {
  const { file, result, error } = loaded
  if (error) {
    return (
      <Alert variant="destructive">
        <AlertTriangleIcon />
        <AlertTitle>{file.name} — 읽지 못했습니다</AlertTitle>
        <AlertDescription>{error}</AlertDescription>
      </Alert>
    )
  }
  if (!result) return null
  return (
    <div className="flex flex-col gap-3">
      {result.loans.map((l) => {
        const key = loanKey(file.name, l.sheet)
        return (
          <LoanCard
            key={l.sheet}
            file={file.name}
            loan={l}
            checked={selected.includes(key)}
            onToggle={(on) => onToggle(key, on)}
            typed={typed[key] ?? {}}
            onType={(date, v) => onType(key, date, v)}
            names={names[key] ?? { name: l.name || l.sheet, short: l.short ?? "" }}
            onRename={(patch) => onRename(key, patch)}
            showMissing={showMissing && selected.includes(key)}
            extend={extend}
            showCheckbox={!extend && result.loans.length > 1}
          />
        )
      })}
      {result.skipped.length > 0 && (
        <p className="text-xs text-muted-foreground">
          건너뛴 시트:{" "}
          {result.skipped.map((s) => `${s.sheet} (${s.reason})`).join(" · ")}
        </p>
      )}
      {result.loans.length === 0 && (
        <Alert>
          <AlertTriangleIcon />
          <AlertTitle>
            {file.name} —{" "}
            {extend
              ? "이 차입건의 스케줄이 없습니다"
              : "차입 스케줄이 없습니다"}
          </AlertTitle>
          <AlertDescription>
            {extend
              ? "거래처가 같은 시트를 찾지 못했습니다."
              : "기준일자 · 현금흐름구분 · 액면이자금액 열이 있는 시트를 찾지 못했습니다."}
          </AlertDescription>
        </Alert>
      )}
    </div>
  )
}

function LoanCard({
  file,
  loan,
  checked,
  onToggle,
  typed,
  onType,
  names,
  onRename,
  showMissing,
  extend,
  showCheckbox,
}: {
  file: string
  loan: LoanPreview
  checked: boolean
  onToggle: (on: boolean) => void
  typed: Record<string, string>
  onType: (date: string, value: string) => void
  names: LoanName
  onRename: (patch: Partial<LoanName>) => void
  showMissing: boolean
  extend: boolean
  showCheckbox: boolean
}) {
  if (!loan.ok) {
    return (
      <Alert variant="destructive">
        <AlertTriangleIcon />
        <AlertTitle>{loan.sheet} — 등록할 수 없습니다</AlertTitle>
        <AlertDescription>{loan.error}</AlertDescription>
      </Alert>
    )
  }
  const payments = loan.payments ?? []
  const canAdd = (loan.newCount ?? 0) > 0
  const id = `loan-${file}-${loan.sheet}`
  const missing = showMissing && canAdd && names.name.trim() === ""
  return (
    <div className="rounded-lg border">
      {!extend && (
        <div className="flex items-start gap-3 border-b px-4 py-3">
          {showCheckbox && (
            <Checkbox
              id={id}
              className="mt-8"
              checked={checked}
              onCheckedChange={(v) => onToggle(v === true)}
              aria-label={`${names.name || loan.org} 등록`}
            />
          )}
          <div className="grid flex-1 grid-cols-[1.5fr_1fr] gap-3">
            <Field data-invalid={missing || undefined}>
              <FieldLabel htmlFor={`${id}-name`}>
                차입명<span className="text-destructive">*</span>
              </FieldLabel>
              <Input
                id={`${id}-name`}
                maxLength={60}
                placeholder="예: ○○지구 도시개발사업"
                aria-invalid={missing || undefined}
                aria-required
                value={names.name}
                onChange={(e) => onRename({ name: e.target.value })}
              />
              {missing && <FieldError>차입명을 넣어 주세요.</FieldError>}
            </Field>
            <Field>
              <FieldLabel htmlFor={`${id}-short`}>약칭</FieldLabel>
              <Input
                id={`${id}-short`}
                maxLength={20}
                placeholder="비우면 차입명으로 표시"
                value={names.short}
                onChange={(e) => onRename({ short: e.target.value })}
              />
            </Field>
          </div>
          {!canAdd && (
            <Badge variant="outline" className="mt-8">
              회차 모두 등록됨 · 원본만 보관
            </Badge>
          )}
        </div>
      )}
      <dl className="flex flex-wrap gap-x-6 gap-y-1.5 border-b bg-muted/30 px-4 py-2.5">
        <Meta label="차입명" value={names.name.trim() || "—"} />
        <Meta label="차입일" value={loan.start ?? "—"} />
        <Meta label="액면" value={`${won(loan.face)}원`} />
        {loan.rate !== null && loan.rate !== undefined && (
          <Meta label="이율" value={`${loan.rate}%`} />
        )}
      </dl>

      {loan.checks && loan.checks.length > 0 && (
        <dl className="grid grid-cols-1 gap-x-6 gap-y-1 border-b px-4 py-2.5 text-sm sm:grid-cols-2">
          {loan.checks.map((c) => (
            <div key={c.label} className="flex justify-between gap-3">
              <dt className="text-muted-foreground">{c.label}</dt>
              <dd
                className={
                  c.level === "warn"
                    ? "font-medium text-amber-700 dark:text-amber-400"
                    : "font-medium"
                }
              >
                {c.value}
              </dd>
            </div>
          ))}
        </dl>
      )}

      {(loan.warnings ?? []).length > 0 && (
        <ul className="mx-4 mt-2.5 flex list-disc flex-col gap-0.5 rounded-md bg-amber-50 py-2 pr-3 pl-7 text-xs text-amber-900 dark:bg-amber-950/40 dark:text-amber-200">
          {loan.warnings!.map((w) => (
            <li key={w}>{w}</li>
          ))}
        </ul>
      )}

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="pl-4">기간</TableHead>
            <TableHead>지급일</TableHead>
            <TableHead className="pr-4 text-right">지급액</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {payments.map((p) => (
            <TableRow
              key={p.scheduled}
              className={
                p.state === "new" ? undefined : "text-muted-foreground"
              }
            >
              <TableCell className="pl-4 text-muted-foreground tabular-nums">
                {p.from} ~ {p.scheduled}
                {p.state !== "new" && (
                  <span className="ml-1 text-xs">· 등록됨</span>
                )}
              </TableCell>
              <TableCell className="tabular-nums">
                {p.scheduled}
                {p.shifted && (
                  <span className="ml-1 text-xs text-muted-foreground">
                    → {md(p.payDue)}
                  </span>
                )}
              </TableCell>
              <TableCell className="pr-4 text-right">
                {p.state === "new" ? (
                  <Input
                    inputMode="numeric"
                    aria-label={`${p.scheduled} 지급액`}
                    className="ml-auto h-7 w-36 text-right tabular-nums"
                    value={typed[p.scheduled] ?? won(p.amount)}
                    onChange={(e) => onType(p.scheduled, e.target.value)}
                  />
                ) : (
                  <span className="tabular-nums">
                    {won(p.existingAmount ?? p.amount)}
                  </span>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

/** 차입처 카드의 작은 이름 + 값 한 칸. */
function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm tabular-nums">{value}</dd>
    </div>
  )
}
