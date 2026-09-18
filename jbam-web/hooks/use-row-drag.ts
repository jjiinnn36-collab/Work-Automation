"use client"

import * as React from "react"

type Drag = { from: number; over: number; dy: number }
type Box = { top: number; bottom: number; height: number }

/**
 * 표의 줄을 손잡이(☰)로 끌어 순서를 바꾼다. 끄는 줄은 포인터를 따라가고, 나머지 줄은 비켜 준다.
 * 놓으면 onDrop(원래 위치, 새 위치). 손잡이에서 ↑·↓ 키로도 한 칸씩 옮긴다.
 */
export function useRowDrag(count: number, onDrop: (from: number, to: number) => void) {
  const rows = React.useRef<(HTMLElement | null)[]>([])
  const boxes = React.useRef<Box[]>([])
  const startY = React.useRef(0)
  const [drag, setDragState] = React.useState<Drag | null>(null)
  // 놓는 순간 마지막 움직임이 아직 화면에 반영되지 않았을 수 있어, 최신 값은 ref 로 따로 든다.
  const latest = React.useRef<Drag | null>(null)
  const setDrag = (d: Drag | null) => {
    latest.current = d
    setDragState(d)
  }
  // 놓은 직후 한 번은 비켜 가는 움직임을 끈다 — 줄이 제자리로 되돌아갔다 다시 오는 것처럼 보이지 않게.
  const [settling, setSettling] = React.useState(false)
  const reduce = React.useMemo(
    () => typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches,
    []
  )

  React.useEffect(() => {
    if (!settling) return
    const id = requestAnimationFrame(() => setSettling(false))
    return () => cancelAnimationFrame(id)
  }, [settling])

  function finish(to: number | null) {
    const d = latest.current
    setDrag(null)
    setSettling(true)
    if (d && to !== null && to !== d.from) onDrop(d.from, to)
  }

  const handleProps = (i: number) => ({
    onPointerDown: (e: React.PointerEvent<HTMLElement>) => {
      if (e.button !== 0 || count < 2) return
      e.preventDefault()
      try {
        e.currentTarget.setPointerCapture(e.pointerId)
      } catch {
        // 포인터를 잡지 못해도 끌기는 된다 (손잡이 밖으로 빠르게 나가면 멈출 수 있음).
      }
      boxes.current = rows.current.slice(0, count).map((el) => {
        const r = el?.getBoundingClientRect()
        return { top: r?.top ?? 0, bottom: r?.bottom ?? 0, height: r?.height ?? 0 }
      })
      startY.current = e.clientY
      setDrag({ from: i, over: i, dy: 0 })
    },
    onPointerMove: (e: React.PointerEvent<HTMLElement>) => {
      const cur = latest.current
      if (!cur) return
      const b = boxes.current
      const me = b[cur.from]
      // 목록 밖으로는 끌려 나가지 않게 가둔다.
      const min = b[0].top - me.top
      const max = b[b.length - 1].bottom - me.bottom
      const dy = Math.max(min, Math.min(max, e.clientY - startY.current))
      // 끄는 줄의 앞쪽 가장자리가 옆 줄의 가운데를 넘으면 그 줄과 자리를 바꾼다. 줄 높이가 조금씩 달라도 끝까지 닿는다.
      let over = cur.from
      b.forEach((x, j) => {
        const mid = x.top + x.height / 2
        if (j > cur.from && me.bottom + dy >= mid) over++
        if (j < cur.from && me.top + dy <= mid) over--
      })
      setDrag({ from: cur.from, over, dy })
    },
    onPointerUp: () => finish(latest.current ? latest.current.over : null),
    onPointerCancel: () => finish(null),
    onKeyDown: (e: React.KeyboardEvent<HTMLElement>) => {
      if (e.key === "ArrowUp" && i > 0) {
        e.preventDefault()
        onDrop(i, i - 1)
      } else if (e.key === "ArrowDown" && i < count - 1) {
        e.preventDefault()
        onDrop(i, i + 1)
      }
    },
  })

  const rowProps = (i: number) => {
    const ref = (el: HTMLElement | null) => {
      rows.current[i] = el
    }
    if (!drag) return { ref, style: settling ? { transition: "none" } : undefined }
    const h = boxes.current[drag.from]?.height ?? 0
    if (i === drag.from)
      return {
        ref,
        "data-dragging": true,
        style: {
          transform: `translateY(${drag.dy}px)`,
          position: "relative" as const,
          zIndex: 10,
          transition: "none",
        },
      }
    let shift = 0
    if (drag.from < drag.over && i > drag.from && i <= drag.over) shift = -h
    if (drag.over < drag.from && i >= drag.over && i < drag.from) shift = h
    return {
      ref,
      style: {
        transform: shift ? `translateY(${shift}px)` : undefined,
        transition: reduce ? "none" : "transform 160ms ease",
      },
    }
  }

  return { rowProps, handleProps, dragging: drag !== null }
}
