"use client"

import * as React from "react"
import { DownloadIcon, ExternalLinkIcon, EyeIcon, FileIcon, FileTextIcon, Trash2Icon, UploadIcon } from "lucide-react"

import { cn } from "@/lib/utils"
import { errorMessage, fileUrl, get, post, upload } from "@/lib/api"
import { previewKind } from "@/lib/logic"
import type { AttachmentFile, Occurrence } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { Badge } from "@/components/ui/badge"
import { Button, buttonVariants } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { Spinner } from "@/components/ui/spinner"
import { toast } from "@/components/ui/toast"

type Kind = "받은문서" | "증빙"

/**
 * 증빙자료 목록 (AC-W101~W107). 이번 달·연간·이력이 같은 창을 쓴다.
 * 받은 문서(고지서·통보문)와 증빙(신고서·영수증)은 둘 다 직접 첨부하는 파일이라 한 목록으로 보인다 (사용자 요청 2026-09-18).
 * 새로 첨부하는 파일은 '증빙' 으로 저장한다. 예전에 받은 문서로 붙인 파일도 같은 목록에 나온다.
 * PDF·이미지는 창 안에서 보고, 그 밖의 형식은 이 PC 의 기본 프로그램으로 연다.
 */
export function DocsDialog({
  target, open, onOpenChange,
}: { target: { o: Occurrence; kind: Kind } | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const [files, setFiles] = React.useState<AttachmentFile[] | null>(null)
  const [selected, setSelected] = React.useState<AttachmentFile | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [over, setOver] = React.useState(false)
  const inputRef = React.useRef<HTMLInputElement>(null)
  const autoOpened = React.useRef(false)

  const o = target?.o ?? null

  const load = React.useCallback(async () => {
    if (!o) return
    try {
      const r = await get<{ files: AttachmentFile[] }>("/api/attachments", { y: o.year, id: o.id })
      setFiles(r.files)
      return r.files
    } catch (e) {
      toast.add({ title: "목록을 불러오지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }, [o])

  React.useEffect(() => {
    if (!open || !target) return
    setSelected(null)
    setFiles(null)
    autoOpened.current = false
    load().then((list) => {
      // 파일이 하나뿐이면 고를 것이 없으니 바로 보여 준다 (AC-W107).
      if (!list || autoOpened.current) return
      const docs = list
      if (docs.length === 1 && previewKind(docs[0].name) !== "none") setSelected(docs[0])
      autoOpened.current = true
    })
  }, [open, target, load])

  if (!o) return null
  const list = files ?? []

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

  async function openWithOs(f: AttachmentFile) {
    if (!o) return
    try {
      await post("/api/open", { y: o.year, id: o.id, f: f.file })
      toast.add({ title: "기본 프로그램으로 엽니다", description: f.name, type: "success" })
    } catch (e) {
      toast.add({ title: "열지 못했습니다", description: errorMessage(e), type: "error" })
    }
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

        <div className="grid min-h-0 gap-4 md:grid-cols-[minmax(0,18rem)_1fr]">
          <div className="flex min-w-0 flex-col gap-3">
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
              <ul className="flex max-h-[45vh] flex-col gap-1 overflow-y-auto">
                {list.map((f) => {
                  const pk = previewKind(f.name)
                  const active = selected?.file === f.file
                  return (
                    <li key={f.file} className={cn("group flex items-center gap-2 rounded-md border px-2 py-1.5", active && "border-primary bg-muted")}>
                      <FileIcon className="size-4 shrink-0 text-muted-foreground" />
                      <button
                        type="button"
                        className="min-w-0 flex-1 text-left"
                        onClick={() => (pk === "none" ? openWithOs(f) : setSelected(f))}
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
                  <a href={fileUrl(o.year, o.id, selected.file)} target="_blank" rel="noopener noreferrer" className={buttonVariants({ variant: "ghost", size: "sm" })}>
                    <ExternalLinkIcon data-icon="inline-start" /> 새 창
                  </a>
                  <a href={fileUrl(o.year, o.id, selected.file)} download={selected.name} className={buttonVariants({ variant: "ghost", size: "sm" })}>
                    <DownloadIcon data-icon="inline-start" /> 내려받기
                  </a>
                </div>
                {previewKind(selected.name) === "pdf" ? (
                  <iframe title={selected.name} src={fileUrl(o.year, o.id, selected.file)} className="h-[60vh] w-full flex-1 bg-background" />
                ) : (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img alt={selected.name} src={fileUrl(o.year, o.id, selected.file)} className="max-h-[60vh] w-full object-contain" />
                )}
              </>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
