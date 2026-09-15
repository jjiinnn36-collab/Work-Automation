"use client"

import { BellIcon, BellRingIcon, CalendarDaysIcon, CalendarRangeIcon, HistoryIcon, ListChecksIcon, SettingsIcon } from "lucide-react"

import type { View } from "@/lib/logic"
import {
  Sidebar, SidebarContent, SidebarFooter, SidebarGroup, SidebarGroupContent, SidebarGroupLabel, SidebarHeader,
  SidebarMenu, SidebarMenuBadge, SidebarMenuButton, SidebarMenuItem, SidebarRail,
} from "@/components/ui/sidebar"

const WORK = [
  { view: "alerts", label: "받은 알림", icon: BellIcon },
  { view: "month", label: "이번 달", icon: CalendarDaysIcon },
  { view: "year", label: "연간", icon: CalendarRangeIcon },
  { view: "history", label: "이력", icon: HistoryIcon },
] as const

const MANAGE = [
  { view: "items", label: "항목 관리", icon: ListChecksIcon },
  { view: "settings", label: "설정", icon: SettingsIcon },
] as const

export function AppSidebar({ view, pending, todayText, version }: { view: View; pending: number; todayText: string; version: string }) {
  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" render={<a href="#alerts" />} tooltip="납부 기한 알림">
              <div className="flex aspect-square size-8 items-center justify-center rounded-lg bg-sidebar-primary text-sidebar-primary-foreground">
                <BellRingIcon className="size-4" />
              </div>
              <div className="grid flex-1 text-left leading-tight">
                <span className="truncate font-semibold">납부 기한 알림</span>
                <span className="truncate text-xs text-muted-foreground">{version ? `v${version}` : "내 PC 전용"}</span>
              </div>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>업무</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {WORK.map((m) => (
                <SidebarMenuItem key={m.view}>
                  <SidebarMenuButton render={<a href={`#${m.view}`} />} isActive={view === m.view} tooltip={m.label}>
                    <m.icon />
                    <span>{m.label}</span>
                  </SidebarMenuButton>
                  {m.view === "alerts" && pending > 0 && (
                    <SidebarMenuBadge className="bg-destructive text-white">{pending}</SidebarMenuBadge>
                  )}
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
        <SidebarGroup>
          <SidebarGroupLabel>관리</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {MANAGE.map((m) => (
                <SidebarMenuItem key={m.view}>
                  <SidebarMenuButton render={<a href={`#${m.view}`} />} isActive={view === m.view} tooltip={m.label}>
                    <m.icon />
                    <span>{m.label}</span>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter>
        <p className="truncate px-2 text-xs text-muted-foreground group-data-[collapsible=icon]:hidden">{todayText}</p>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
