"use client"

import { cn } from "@/lib/utils"
import { progressText, stepStates, stepTooltip, visibleSteps } from "@/lib/logic"
import type { Occurrence } from "@/lib/types"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"

/**
 * 진행 지점 도형 (AC-W45~W48, W58, W70~W72).
 * 끝낸 지점 = 꽉 찬 회색, 지금 할 지점 = 꽉 찬 파랑, 아직 아닌 지점 = 테두리만. 선은 지나온 구간만 채운다.
 */
export function Steps({ o, showLabels = false, className }: { o: Occurrence; showLabels?: boolean; className?: string }) {
  // 신고 후 납부는 시작점을 그리지 않는다 (hideStart).
  const { names, stage } = visibleSteps(o)
  const states = stepStates(stage, names.length, o.done)
  const muted = o.beforeStart === true

  return (
    <div className={cn("flex w-full items-start", className)} aria-label={progressText(o)}>
      {names.map((name, i) => {
        const s = states[i]
        return (
          <div key={name} className="relative flex min-w-0 flex-1 flex-col items-center gap-1.5">
            {i > 0 && (
              <span
                aria-hidden
                className={cn(
                  "absolute top-[5px] right-1/2 left-[-50%] h-0.5",
                  s === "past" ? "bg-muted-foreground/60" : "bg-border"
                )}
              />
            )}
            <Tooltip>
              <TooltipTrigger
                render={
                  <button
                    type="button"
                    className={cn(
                      "relative z-[1] size-3 rounded-full border-2 outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
                      s === "past" && "border-muted-foreground/60 bg-muted-foreground/60",
                      s === "current" && !muted && "border-action bg-action ring-3 ring-action/20",
                      s === "current" && muted && "border-muted-foreground/40 bg-background",
                      s === "future" && "border-border bg-background"
                    )}
                    aria-label={stepTooltip(names, i, s)}
                  />
                }
              />
              <TooltipContent>{stepTooltip(names, i, s)}</TooltipContent>
            </Tooltip>
            {showLabels && (
              <span
                className={cn(
                  "max-w-full truncate text-[11px] leading-none",
                  s === "current" && !muted ? "font-semibold text-action" : "text-muted-foreground"
                )}
              >
                {name}
              </span>
            )}
          </div>
        )
      })}
    </div>
  )
}

/** 도형 밑 한 줄: 지금 해야 할 행동 하나만 (AC-W49~W52). 시작일 이전 건에는 지시를 적지 않는다. */
export function NextAction({ o, className }: { o: Occurrence; className?: string }) {
  if (o.beforeStart) return <span className={cn("text-xs text-muted-foreground", className)}>{o.stageName}</span>
  return (
    <span className={cn("text-xs font-medium", o.done ? "text-muted-foreground" : "text-foreground", className)}>
      {o.nextAction}
    </span>
  )
}
