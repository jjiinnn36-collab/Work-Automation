"use client"

import * as React from "react"
import { MoonIcon, SunIcon } from "lucide-react"
import { useTheme } from "next-themes"

import { ApiError, errorMessage, get, post } from "@/lib/api"
import { viewFromHash, type View } from "@/lib/logic"
import type { AlertsData, Item, Occurrence } from "@/lib/types"
import { AppContext, type AppActions, type StageAction } from "@/components/app/app-context"
import { AppSidebar } from "@/components/app/app-sidebar"
import { AmountDialog } from "@/components/app/dialogs/amount-dialog"
import { DocsDialog } from "@/components/app/dialogs/docs-dialog"
import { EventsSheet } from "@/components/app/dialogs/events-sheet"
import { GroupAmountDialog } from "@/components/app/dialogs/group-amount-dialog"
import { ItemDialog } from "@/components/app/dialogs/item-dialog"
import { AlertsPage } from "@/components/app/pages/alerts-page"
import { HistoryPage } from "@/components/app/pages/history-page"
import { ItemsPage } from "@/components/app/pages/items-page"
import { MonthPage } from "@/components/app/pages/month-page"
import { SettingsPage } from "@/components/app/pages/settings-page"
import { YearPage } from "@/components/app/pages/year-page"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar"
import { toast } from "@/components/ui/toast"

const TITLES: Record<View, string> = {
  alerts: "받은 알림",
  month: "이번 달",
  year: "연간",
  items: "항목 관리",
  history: "이력",
  settings: "설정",
}

type Confirm = { title: string; description: string; action: string; destructive?: boolean; resolve: (v: boolean) => void }

export default function Page() {
  const [view, setView] = React.useState<View>("alerts")
  const [version, setVersion] = React.useState(0)
  const [head, setHead] = React.useState<{ today: string; todayText: string; pending: number }>({ today: "", todayText: "", pending: 0 })
  const [appVersion, setAppVersion] = React.useState("")

  const [amountFor, setAmountFor] = React.useState<Occurrence | null>(null)
  const [groupFor, setGroupFor] = React.useState<{ year: number; group: string } | null>(null)
  const [docsFor, setDocsFor] = React.useState<{ o: Occurrence; kind: "받은문서" | "증빙" } | null>(null)
  const [eventsFor, setEventsFor] = React.useState<{ year: number; id: string; name: string } | null>(null)
  const [itemFor, setItemFor] = React.useState<{ item: Item | null } | null>(null)
  const [confirmState, setConfirmState] = React.useState<Confirm | null>(null)

  const refresh = React.useCallback(() => setVersion((v) => v + 1), [])

  // 해시 경로로 화면을 고른다. 서버는 index.html 하나만 내주면 된다.
  React.useEffect(() => {
    const sync = () => {
      setView(viewFromHash(window.location.hash))
      window.scrollTo(0, 0)
    }
    sync()
    window.addEventListener("hashchange", sync)
    return () => window.removeEventListener("hashchange", sync)
  }, [])

  // 팝업에서 바꾼 것이 늦게 보이지 않게: 돌아왔을 때와 1분마다 다시 읽는다.
  React.useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === "visible") refresh()
    }
    document.addEventListener("visibilitychange", onVisible)
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") refresh()
    }, 60_000)
    return () => {
      document.removeEventListener("visibilitychange", onVisible)
      clearInterval(timer)
    }
  }, [refresh])

  React.useEffect(() => {
    get<AlertsData>("/api/alerts")
      .then((d) => setHead({ today: d.today, todayText: d.todayText, pending: d.pending }))
      .catch(() => {})
  }, [version])

  React.useEffect(() => {
    get<{ version: string }>("/api/health").then((h) => setAppVersion(h.version)).catch(() => {})
  }, [])

  React.useEffect(() => {
    document.title = `${TITLES[view]} · 납부 기한 알림`
  }, [view])

  const confirm = React.useCallback<AppActions["confirm"]>(
    (opts) => new Promise<boolean>((resolve) => setConfirmState({ ...opts, resolve })),
    []
  )

  const act = React.useCallback(
    async (kind: StageAction, o: Occurrence) => {
      if (kind === "revert") {
        const yes = await confirm({
          title: "한 단계 되돌릴까요?",
          description: `${o.name} (${o.year}년) — '${o.stages[o.stage]}' 를 취소하고 '${o.stages[o.stage - 1]}' 까지 끝난 것으로 돌립니다. 오늘 확인 표시도 지워져 알림 팝업이 다시 묻습니다.`,
          action: "되돌리기",
          destructive: true,
        })
        if (!yes) return
      }
      const path = kind === "advance" ? "/api/advance" : kind === "defer" ? "/api/defer" : "/api/revert"
      try {
        await post(path, { y: o.year, id: o.id, stage: o.stage })
        const title = kind === "advance" ? `${o.name} · ${o.nextStage} 끝남` : kind === "defer" ? `${o.name} · 오늘은 대기` : `${o.name} · 되돌렸습니다`
        toast.add({ title, type: "success" })
      } catch (e) {
        const conflict = e instanceof ApiError && e.status === 409
        toast.add({ title: conflict ? "다른 곳에서 먼저 바뀌었습니다" : "처리하지 못했습니다", description: errorMessage(e), type: "error" })
      } finally {
        refresh()
      }
    },
    [confirm, refresh]
  )

  const actions: AppActions = React.useMemo(
    () => ({
      today: head.today,
      version,
      refresh,
      act,
      confirm,
      openAmount: (o) => setAmountFor(o),
      openGroup: (year, group) => setGroupFor({ year, group }),
      openDocs: (o, kind = "증빙") => setDocsFor({ o, kind }),
      openEvents: (t) => setEventsFor(t),
      openItem: (item) => setItemFor({ item }),
    }),
    [head.today, version, refresh, act, confirm]
  )

  return (
    <AppContext.Provider value={actions}>
      <SidebarProvider>
        <AppSidebar view={view} pending={head.pending} todayText={head.todayText} version={appVersion} />
        <SidebarInset className="min-w-0">
          <header className="sticky top-0 z-20 flex h-14 shrink-0 items-center gap-2 border-b bg-background/90 px-4 backdrop-blur supports-backdrop-filter:bg-background/70">
            <SidebarTrigger className="-ml-1" />
            <Separator orientation="vertical" className="mr-1 data-vertical:h-4" />
            <h1 className="text-sm font-medium">{TITLES[view]}</h1>
            <span className="ml-auto hidden text-sm text-muted-foreground sm:inline">{head.todayText}</span>
            <ThemeToggle />
          </header>
          <main className="mx-auto flex w-full min-w-0 max-w-7xl flex-1 flex-col gap-6 p-4 md:p-6">
            {view === "alerts" && <AlertsPage />}
            {view === "month" && <MonthPage />}
            {view === "year" && <YearPage />}
            {view === "items" && <ItemsPage />}
            {view === "history" && <HistoryPage />}
            {view === "settings" && <SettingsPage />}
          </main>
        </SidebarInset>
      </SidebarProvider>

      <AmountDialog o={amountFor} open={amountFor !== null} onOpenChange={(v) => !v && setAmountFor(null)} />
      <GroupAmountDialog target={groupFor} open={groupFor !== null} onOpenChange={(v) => !v && setGroupFor(null)} />
      <DocsDialog target={docsFor} open={docsFor !== null} onOpenChange={(v) => !v && setDocsFor(null)} />
      <EventsSheet target={eventsFor} open={eventsFor !== null} onOpenChange={(v) => !v && setEventsFor(null)} />
      <ItemDialog item={itemFor?.item ?? null} open={itemFor !== null} onOpenChange={(v) => !v && setItemFor(null)} />

      <AlertDialog
        open={confirmState !== null}
        onOpenChange={(v) => {
          if (!v && confirmState) {
            confirmState.resolve(false)
            setConfirmState(null)
          }
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{confirmState?.title}</AlertDialogTitle>
            <AlertDialogDescription>{confirmState?.description}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>취소</AlertDialogCancel>
            <AlertDialogAction
              variant={confirmState?.destructive ? "destructive" : "default"}
              onClick={() => {
                confirmState?.resolve(true)
                setConfirmState(null)
              }}
            >
              {confirmState?.action}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </AppContext.Provider>
  )
}

function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme()
  const dark = resolvedTheme === "dark"
  return (
    <Button variant="ghost" size="icon-sm" aria-label={dark ? "밝은 화면" : "어두운 화면"} onClick={() => setTheme(dark ? "light" : "dark")}>
      {dark ? <SunIcon /> : <MoonIcon />}
    </Button>
  )
}
