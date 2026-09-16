"use client"

import * as React from "react"

import { discardConfirm } from "@/lib/logic"
import { useApp } from "@/components/app/app-context"

/** 확인 창이 닫힌 직후 이만큼은 닫기 요청을 받지 않는다 — 확인 창을 누른 동작이 뒤 창에 '바깥 누르기' 로 이어지지 않게. */
const 여운ms = 400

/**
 * 입력하던 창을 ✕·Esc·바깥 누르기·취소로 닫으려 하면, 바뀐 내용이 있을 때 한 번 묻는다 (ADR-0022).
 * 저장이 끝나 닫을 때는 원래 onOpenChange 를 그대로 부른다.
 * - 확인 창이 떠 있는 동안, 그리고 닫힌 직후 잠깐 들어온 닫기 요청은 무시한다(같은 질문이 겹쳐 뜨지 않게).
 * - 확인 창 안을 누른 것이 뒤 창에 바깥 누르기로 전해진 경우도 무시한다.
 */
export function useCloseGuard(dirty: boolean, onOpenChange: (v: boolean) => void, what = "내용") {
  const app = useApp()
  const asking = React.useRef(false)
  const quietUntil = React.useRef(0)
  return React.useCallback(
    async (v: boolean, details?: { event?: Event }) => {
      if (v || !dirty) {
        onOpenChange(v)
        return
      }
      const target = details?.event?.target
      if (target instanceof Element && target.closest('[role="alertdialog"]')) return
      if (asking.current || Date.now() < quietUntil.current) return
      asking.current = true
      try {
        if (await app.confirm(discardConfirm(what))) onOpenChange(false)
      } finally {
        asking.current = false
        quietUntil.current = Date.now() + 여운ms
      }
    },
    [dirty, onOpenChange, what, app]
  )
}
