"use client"

import * as React from "react"

import type { Item, Occurrence } from "@/lib/types"

export type StageAction = "advance" | "defer" | "revert"

export interface AppActions {
  /** 서버 기준 오늘 (YYYY-MM-DD). 아직 모르면 빈 문자열. */
  today: string
  /** 올리면 모든 화면이 자료를 다시 부른다. */
  version: number
  refresh: () => void
  act: (kind: StageAction, o: Occurrence) => Promise<void>
  confirm: (opts: { title: string; description: string; action: string; destructive?: boolean }) => Promise<boolean>
  openAmount: (o: Occurrence) => void
  openGroup: (year: number, group: string) => void
  openDocs: (o: Occurrence, kind?: "받은문서" | "증빙") => void
  openEvents: (target: { year: number; id: string; name: string }) => void
  openItem: (item: Item | null) => void
}

export const AppContext = React.createContext<AppActions | null>(null)

export function useApp(): AppActions {
  const ctx = React.useContext(AppContext)
  if (!ctx) throw new Error("AppContext 가 없습니다.")
  return ctx
}
