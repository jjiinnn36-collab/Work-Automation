"use client"

import * as React from "react"

import { errorMessage, get, post } from "@/lib/api"
import type { LoanSummary } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { Spinner } from "@/components/ui/spinner"
import { toast } from "@/components/ui/toast"

/**
 * 차입건을 통째로 지우는 확인 창 (사용자 결정 2026-09-23, ㄷ안).
 * 기본은 다른 항목 삭제와 같게 금액·진행 기록을 남긴다 — 실수로 지웠다가 다시 가져오면 이어지도록.
 * '진행 기록도 함께 지우기' 를 켰을 때만 그것까지 지운다. 회차에 직접 붙인 증빙은 어느 쪽이든 남는다.
 * 무엇이 지워지는지 숫자로 먼저 보여 준다 — 되돌릴 수 없는 일이라 눈으로 확인하고 눌러야 한다.
 */
export function LoanDeleteDialog({
  group, open, onOpenChange, onDone,
}: {
  group: string | null
  open: boolean
  onOpenChange: (v: boolean) => void
  onDone?: () => void
}) {
  const app = useApp()
  const [sum, setSum] = React.useState<LoanSummary | null>(null)
  const [purge, setPurge] = React.useState(false)
  const [busy, setBusy] = React.useState(false)

  React.useEffect(() => {
    if (!open || !group) return
    setSum(null)
    setPurge(false)
    get<LoanSummary>("/api/loan/summary", { group })
      .then(setSum)
      .catch((e) => {
        toast.add({ title: "차입건을 읽지 못했습니다", description: errorMessage(e), type: "error" })
        onOpenChange(false)
      })
  }, [open, group, onOpenChange])

  async function run() {
    if (!group) return
    setBusy(true)
    try {
      const r = await post<{ items: number; docs: number; backup: string | null }>(
        "/api/loan/delete", { group, purge: purge ? "1" : "0" })
      toast.add({
        title: "차입건을 지웠습니다",
        description: `회차 ${r.items}건 · 원본 스케줄 ${r.docs}개` +
          (r.backup ? ` · 지우기 전 사본: ${r.backup}` : ""),
        type: "success",
      })
      onOpenChange(false)
      app.refresh()
      onDone?.()
    } catch (e) {
      toast.add({ title: "지우지 못했습니다", description: errorMessage(e), type: "error" })
    } finally {
      setBusy(false)
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>이 차입건을 지울까요?</AlertDialogTitle>
          <AlertDialogDescription>
            {sum ? `${sum.name} · ${sum.org} · ${sum.start} 차입` : "불러오는 중"}
          </AlertDialogDescription>
        </AlertDialogHeader>

        {!sum ? (
          <div className="flex justify-center py-6"><Spinner /></div>
        ) : (
          <div className="flex flex-col gap-3 text-sm">
            <p>되돌릴 수 없습니다. 아래 자료가 함께 지워집니다.</p>

            <div className="rounded-lg border bg-muted/50 p-3">
              <div className="mb-1.5 font-medium">함께 지워지는 것</div>
              <ul className="list-disc pl-5 text-muted-foreground">
                <li>이자 회차 <b className="text-foreground">{sum.items}건</b></li>
                <li>차입 원본 스케줄 <b className="text-foreground">{sum.docs}개</b> — 보관 사본까지 삭제</li>
                {purge && (
                  <>
                    <li>회차별 지급액 <b className="text-foreground">{sum.amounts}건</b></li>
                    <li>진행 상태 <b className="text-foreground">{sum.status}건</b> · 변경 기록 <b className="text-foreground">{sum.events}건</b></li>
                  </>
                )}
              </ul>
            </div>

            <div className="flex items-start gap-2 rounded-lg border p-3">
              <Checkbox id="purge" checked={purge} onCheckedChange={(v) => setPurge(v === true)} className="mt-0.5" />
              <Label htmlFor="purge" className="flex flex-col items-start gap-0.5 font-normal">
                <span className="font-medium">진행 기록도 함께 지우기</span>
                <span className="text-xs text-muted-foreground">
                  끄면 지급액·진행 기록이 남아, 같은 엑셀을 다시 가져올 때 진행 상태가 이어집니다.
                </span>
              </Label>
            </div>

            <p className="text-xs text-muted-foreground">
              회차에 직접 붙인 증빙 {sum.otherDocs}건은 그대로 남습니다. 지우기 직전에 자료 사본을 한 부 만듭니다.
            </p>
          </div>
        )}

        <AlertDialogFooter>
          <AlertDialogCancel disabled={busy}>그만두기</AlertDialogCancel>
          <AlertDialogAction variant="destructive" disabled={!sum || busy} onClick={run}>
            {busy ? <Spinner /> : null} 차입건 지우기
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
