"use client"

import * as React from "react"
import {
  ChevronRightIcon, DownloadIcon, ExternalLinkIcon, EyeIcon, FileIcon, FileSpreadsheetIcon,
  FileTextIcon, InfoIcon, Trash2Icon, UploadIcon,
} from "lucide-react"

import { cn } from "@/lib/utils"
import { errorMessage, fileUrl, get, post, upload } from "@/lib/api"
import { parseCsv, previewKind } from "@/lib/logic"
import type { AttachmentFile, LoanDocFile, Occurrence } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { Badge } from "@/components/ui/badge"
import { Button, buttonVariants } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { Spinner } from "@/components/ui/spinner"
import { toast } from "@/components/ui/toast"

type Kind = "받은문서" | "증빙"

/** 창 안에서 볼 파일 하나. 증빙은 (그 해, 회차 id), 차입 원본은 (차입일 해, 묶음) 으로 주소가 다르다. */
type Picked = { name: string; file: string; year: number; id: string; loan: boolean }

/** 이전 것이 이만큼 쌓이면 정리하라고 한 줄 알린다. 저절로 지우지는 않는다 (사용자 결정 2026-09-23). */
const 정리알림 = 10

/**
 * 증빙자료 목록 (AC-W101~W107). 이번 달·연간·이력이 같은 창을 쓴다.
 * 받은 문서(고지서·통보문)와 증빙(신고서·영수증)은 둘 다 직접 첨부하는 파일이라 한 목록으로 보인다 (사용자 요청 2026-09-18).
 * 차입 회차면 맨 위에 그 차입건의 원본 스케줄(CSV)이 함께 보인다 — 회차마다 복사하지 않고 차입건에 한 부만 둔다.
 * PDF·이미지·CSV 는 창 안에서 보고, 그 밖의 형식은 이 PC 의 기본 프로그램으로 연다.
 */
export function DocsDialog({
  target, open, onOpenChange,
}: { target: { o: Occurrence; kind: Kind } | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const [files, setFiles] = React.useState<AttachmentFile[] | null>(null)
  const [loanDocs, setLoanDocs] = React.useState<LoanDocFile[]>([])
  const [loanGroup, setLoanGroup] = React.useState<string | null>(null)
  const [oldOpen, setOldOpen] = React.useState(false)
  const [selected, setSelected] = React.useState<Picked | null>(null)
  const [csv, setCsv] = React.useState<string[][] | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [over, setOver] = React.useState(false)
  const inputRef = React.useRef<HTMLInputElement>(null)
  const autoOpened = React.useRef(false)

  const o = target?.o ?? null

  const load = React.useCallback(async () => {
    if (!o) return
    try {
      const r = await get<{ files: AttachmentFile[]; loanGroup: string | null; loanDocs: LoanDocFile[] }>(
        "/api/attachments", { y: o.year, id: o.id })
      setFiles(r.files)
      setLoanDocs(r.loanDocs ?? [])
      setLoanGroup(r.loanGroup ?? null)
      return r
    } catch (e) {
      toast.add({ title: "목록을 불러오지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }, [o])

  React.useEffect(() => {
    if (!open || !target) return
    setSelected(null)
    setFiles(null)
    setLoanDocs([])
    setOldOpen(false)
    autoOpened.current = false
    load().then((r) => {
      if (!r || autoOpened.current) return
      autoOpened.current = true
      // 차입 회차면 가장 최근 원본 스케줄을 바로 펼쳐 준다 (다시 가져오기·연장한 것이 맨 위).
      const newest = (r.loanDocs ?? [])[0]
      if (newest && r.loanGroup) {
        setSelected({ name: newest.name, file: newest.file, year: newest.year, id: r.loanGroup, loan: true })
        return
      }
      // 파일이 하나뿐이면 고를 것이 없으니 바로 보여 준다 (AC-W107).
      if (r.files.length === 1 && previewKind(r.files[0].name) !== "none")
        setSelected({ name: r.files[0].name, file: r.files[0].file, year: o!.year, id: o!.id, loan: false })
    })
  }, [open, target, load, o])

  // 고른 것이 CSV 면 글자를 받아 표로 그린다. 다른 형식은 iframe·img 가 알아서 한다.
  React.useEffect(() => {
    if (!selected || previewKind(selected.name) !== "csv") { setCsv(null); return }
    let alive = true
    setCsv(null)
    fetch(fileUrl(selected.year, selected.id, selected.file))
      .then((r) => r.text())
      .then((t) => { if (alive) setCsv(parseCsv(t)) })
      .catch(() => { if (alive) setCsv([]) })
    return () => { alive = false }
  }, [selected])

  if (!o) return null
  const list = files ?? []
  const newest = loanDocs[0]
  const olds = loanDocs.slice(1)

  async function add(picked: File[]) {
    if (!o || picked.length === 0) return
    setBusy(true)
    let ok = 0
    for (const f of picked) {
      try {
        await upload("/api/attach", { y: o.year, id: o.id, kind: "증빙" }, f)
        ok++
      } catch (e) {
        toast.add({ title: `${f.name} 을(를) 첨부하지 못했습니다`, description: errorMessage(e), type: "error" })
      }
    }
    setBusy(false)
    if (ok > 0) {
      toast.add({ title: `${ok}개 첨부했습니다`, type: "success" })
      await load()
      app.refresh()
    }
  }

  async function remove(f: AttachmentFile) {
    if (!o) return
    const yes = await app.confirm({
      title: "이 파일을 지울까요?",
      description: `${f.name} — 목록에서 빠지고 보관한 사본도 지워집니다.`,
      action: "지우기",
      destructive: true,
    })
    if (!yes) return
    try {
      await post("/api/attach/delete", { y: o.year, id: o.id, f: f.file })
      if (selected?.file === f.file) setSelected(null)
      await load()
      app.refresh()
    } catch (e) {
      toast.add({ title: "지우지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }

  /** 이전에 가져온 원본 스케줄 한 부를 지운다. 가장 최근 것은 지울 수 없다 (근거가 사라진다). */
  async function removeDoc(d: LoanDocFile) {
    if (!loanGroup) return
    const yes = await app.confirm({
      title: "이 원본 스케줄을 지울까요?",
      description: `${d.name} (${d.at}) — 그때 올린 스케줄 근거가 사라집니다. 가장 최근 것은 그대로 남습니다.`,
      action: "지우기",
      destructive: true,
    })
    if (!yes) return
    try {
      await post("/api/attach/delete", { y: d.year, id: loanGroup, f: d.file })
      if (selected?.file === d.file) setSelected(null)
      await load()
      app.refresh()
    } catch (e) {
      toast.add({ title: "지우지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }

  async function openWithOs(p: Picked) {
    try {
      await post("/api/open", { y: p.year, id: p.id, f: p.file })
      toast.add({ title: "기본 프로그램으로 엽니다", description: p.name, type: "success" })
    } catch (e) {
      toast.add({ title: "열지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }

  function pickDoc(d: LoanDocFile) {
    if (!loanGroup) return
    setSelected({ name: d.name, file: d.file, year: d.year, id: loanGroup, loan: true })
  }

  function docRow(d: LoanDocFile, 최신: boolean) {
    const active = selected?.file === d.file
    return (
      <li key={d.file} className={cn("group flex items-center gap-2 border-t px-2 py-1.5 first:border-t-0", active && "bg-muted")}>
        <FileSpreadsheetIcon className={cn("size-4 shrink-0", 최신 ? "text-action" : "text-muted-foreground")} />
        <button type="button" className="min-w-0 flex-1 text-left" onClick={() => pickDoc(d)} title="창 안에서 표로 보기">
          <span className={cn("block truncate text-sm", 최신 ? "font-semibold" : "font-medium")}>{d.name}</span>
          <span className="block truncate text-xs text-muted-foreground">
            {최신 && <Badge className="mr-1 h-4 px-1 text-[10px]">최신</Badge>}
            {d.at}
          </span>
        </button>
        {!최신 && (
          <Button variant="ghost" size="icon-xs" aria-label="이 원본 지우기" onClick={() => removeDoc(d)}>
            <Trash2Icon />
          </Button>
        )}
      </li>
    )
  }

  return (
    <Dialog open={open} disablePointerDismissal onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>증빙자료</DialogTitle>
          <DialogDescription>
            {o.name} · {o.org} · {o.year}년
          </DialogDescription>
        </DialogHeader>

        <div className="grid min-h-0 gap-4 md:grid-cols-[minmax(0,19rem)_1fr]">
          <div className="flex min-w-0 flex-col gap-4 overflow-y-auto md:max-h-[60vh]">
            {newest && (
              <section>
                <h3 className="mb-1.5 text-xs font-semibold text-muted-foreground">
                  차입 원본 스케줄 <span className="font-normal">· 이 차입건 공통</span>
                </h3>
                <ul className="overflow-hidden rounded-lg border border-l-2 border-l-action">
                  {docRow(newest, true)}
                  {olds.length > 0 && (
                    <li className="border-t">
                      <button
                        type="button"
                        onClick={() => setOldOpen((v) => !v)}
                        aria-expanded={oldOpen}
                        className="flex w-full items-center gap-1.5 px-2 py-1.5 text-xs text-muted-foreground hover:bg-muted hover:text-foreground"
                      >
                        <ChevronRightIcon className={cn("size-3.5 transition-transform", oldOpen && "rotate-90")} />
                        이전에 가져온 파일 {olds.length}건
                      </button>
                    </li>
                  )}
                  {oldOpen && olds.map((d) => docRow(d, false))}
                </ul>
                <p className="mt-1.5 flex gap-1.5 text-xs text-muted-foreground">
                  <InfoIcon className="mt-0.5 size-3.5 shrink-0" />
                  <span>
                    올린 엑셀에서 이 사업건 시트만 떼어 보관합니다. 회차마다 복사하지 않고 차입건에 한 부만 있습니다.
                    {olds.length + 1 >= 정리알림 && ` 이전 파일이 ${olds.length}건 쌓였습니다 — 필요 없는 것은 지워도 됩니다.`}
                  </span>
                </p>
              </section>
            )}

            <section className="flex flex-col gap-3">
              {newest && (
                <h3 className="text-xs font-semibold text-muted-foreground">
                  이 회차 증빙 <span className="font-normal">· {o.name}</span>
                </h3>
              )}
              <div
                onDragOver={(e) => { e.preventDefault(); setOver(true) }}
                onDragLeave={() => setOver(false)}
                onDrop={(e) => { e.preventDefault(); setOver(false); add(Array.from(e.dataTransfer.files)) }}
                className={cn(
                  "flex flex-col items-center gap-2 rounded-lg border border-dashed p-4 text-center text-sm text-muted-foreground transition-colors",
                  over && "border-primary bg-muted"
                )}
              >
                <span>파일을 끌어 놓거나</span>
                <Button variant="outline" size="sm" disabled={busy} onClick={() => inputRef.current?.click()}>
                  {busy ? <Spinner /> : <UploadIcon data-icon="inline-start" />} 파일 첨부
                </Button>
                <input
                  ref={inputRef}
                  type="file"
                  multiple
                  hidden
                  onChange={(e) => {
                    add(Array.from(e.target.files ?? []))
                    e.target.value = ""
                  }}
                />
              </div>

              {files === null ? (
                <div className="flex justify-center p-4"><Spinner /></div>
              ) : list.length === 0 ? (
                <Empty className="border">
                  <EmptyHeader>
                    <EmptyMedia variant="icon"><FileTextIcon /></EmptyMedia>
                    <EmptyTitle>증빙자료가 없습니다</EmptyTitle>
                    <EmptyDescription>
                      고지서·신고서·영수증 등을 첨부해 두면 여기서 바로 열립니다.
                    </EmptyDescription>
                  </EmptyHeader>
                </Empty>
              ) : (
                <ul className="flex flex-col gap-1">
                  {list.map((f) => {
                    const pk = previewKind(f.name)
                    const picked: Picked = { name: f.name, file: f.file, year: o.year, id: o.id, loan: false }
                    const active = selected?.file === f.file
                    return (
                      <li key={f.file} className={cn("group flex items-center gap-2 rounded-md border px-2 py-1.5", active && "border-primary bg-muted")}>
                        <FileIcon className="size-4 shrink-0 text-muted-foreground" />
                        <button
                          type="button"
                          className="min-w-0 flex-1 text-left"
                          onClick={() => (pk === "none" ? openWithOs(picked) : setSelected(picked))}
                          title={pk === "none" ? "기본 프로그램으로 열기" : "창 안에서 보기"}
                        >
                          <span className="block truncate text-sm font-medium">{f.name}</span>
                          <span className="block truncate text-xs text-muted-foreground">
                            {f.stage && <Badge variant="outline" className="mr-1 h-4 px-1 text-[10px]">{f.stage}</Badge>}
                            {f.at}
                          </span>
                        </button>
                        <Button variant="ghost" size="icon-xs" aria-label="지우기" onClick={() => remove(f)}>
                          <Trash2Icon />
                        </Button>
                      </li>
                    )
                  })}
                </ul>
              )}
            </section>
          </div>

          <div className="flex min-h-[40vh] min-w-0 flex-col overflow-hidden rounded-lg border bg-muted/30">
            {!selected ? (
              <div className="flex flex-1 items-center justify-center p-6 text-sm text-muted-foreground">
                <EyeIcon className="mr-2 size-4" /> 목록에서 파일을 누르면 여기에서 봅니다
              </div>
            ) : (
              <>
                <div className="flex items-center gap-2 border-b bg-background px-3 py-2">
                  <span className="min-w-0 flex-1 truncate text-sm font-medium">{selected.name}</span>
                  {previewKind(selected.name) === "csv" && (
                    <Button variant="ghost" size="sm" onClick={() => openWithOs(selected)}>
                      <ExternalLinkIcon data-icon="inline-start" /> 엑셀로 열기
                    </Button>
                  )}
                  {previewKind(selected.name) !== "csv" && (
                    <a href={fileUrl(selected.year, selected.id, selected.file)} target="_blank" rel="noopener noreferrer" className={buttonVariants({ variant: "ghost", size: "sm" })}>
                      <ExternalLinkIcon data-icon="inline-start" /> 새 창
                    </a>
                  )}
                  <a href={fileUrl(selected.year, selected.id, selected.file)} download={selected.name} className={buttonVariants({ variant: "ghost", size: "sm" })}>
                    <DownloadIcon data-icon="inline-start" /> 내려받기
                  </a>
                </div>

                {previewKind(selected.name) === "pdf" ? (
                  <iframe title={selected.name} src={fileUrl(selected.year, selected.id, selected.file)} className="h-[60vh] w-full flex-1 bg-background" />
                ) : previewKind(selected.name) === "image" ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img alt={selected.name} src={fileUrl(selected.year, selected.id, selected.file)} className="max-h-[60vh] w-full object-contain" />
                ) : csv === null ? (
                  <div className="flex flex-1 items-center justify-center p-6"><Spinner /></div>
                ) : csv.length === 0 ? (
                  <div className="flex flex-1 items-center justify-center p-6 text-sm text-muted-foreground">내용을 읽지 못했습니다</div>
                ) : (
                  <div className="max-h-[60vh] flex-1 overflow-auto bg-background">
                    <table className="w-full border-collapse text-xs">
                      <thead>
                        <tr>
                          {csv[0].map((h, i) => (
                            <th key={i} className="sticky top-0 whitespace-nowrap border-b bg-muted px-2.5 py-1.5 text-left font-semibold text-muted-foreground">
                              {h}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {csv.slice(1).map((row, r) => (
                          <tr key={r}>
                            {row.map((v, i) => (
                              <td key={i} className={cn("whitespace-nowrap border-b px-2.5 py-1.5", /^-?[\d,]+(\.\d+)?$/.test(v) && v !== "" && "text-right tabular-nums")}>
                                {v}
                              </td>
                            ))}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
