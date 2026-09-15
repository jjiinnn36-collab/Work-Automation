"use client"

import * as React from "react"

/**
 * 대화상자를 머리 부분으로 끌어 옮긴다.
 * 가운데 자리(left/top 50%)에서 얼마나 옮겼는지만 들고, 창을 다시 열면 가운데로 돌아온다.
 * 입력칸·버튼 위에서 누른 것은 끌기로 보지 않는다.
 */
export function useDraggable(open: boolean) {
  const [offset, setOffset] = React.useState({ x: 0, y: 0 })
  const start = React.useRef<{ px: number; py: number; x: number; y: number } | null>(null)

  React.useEffect(() => {
    if (open) setOffset({ x: 0, y: 0 })
  }, [open])

  const onPointerDown = (e: React.PointerEvent<HTMLElement>) => {
    if (e.button !== 0) return
    if ((e.target as HTMLElement).closest("button, input, select, textarea, a")) return
    start.current = { px: e.clientX, py: e.clientY, x: offset.x, y: offset.y }
    e.currentTarget.setPointerCapture(e.pointerId)
    e.preventDefault()
  }

  const onPointerMove = (e: React.PointerEvent<HTMLElement>) => {
    const s = start.current
    if (!s) return
    // 머리가 화면 밖으로 사라지지 않게 가둔다.
    const maxX = window.innerWidth / 2 - 40
    const maxY = window.innerHeight / 2 - 24
    const clamp = (v: number, m: number) => Math.max(-m, Math.min(m, v))
    setOffset({ x: clamp(s.x + e.clientX - s.px, maxX), y: clamp(s.y + e.clientY - s.py, maxY) })
  }

  const onPointerUp = (e: React.PointerEvent<HTMLElement>) => {
    start.current = null
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId)
  }

  return {
    style: { left: `calc(50% + ${offset.x}px)`, top: `calc(50% + ${offset.y}px)` } as React.CSSProperties,
    handleProps: {
      onPointerDown,
      onPointerMove,
      onPointerUp,
      onPointerCancel: onPointerUp,
      className: "cursor-move touch-none select-none",
    },
  }
}
