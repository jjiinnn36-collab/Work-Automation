"use client"

import * as React from "react"

import { errorMessage } from "@/lib/api"

/**
 * 자료를 불러온다. deps 가 바뀌면 다시 부르고, 다시 부르는 동안에도 이전 자료를 보여 준다
 * (1분마다 새로 고칠 때 화면이 깜빡이지 않게).
 */
export function useLoad<T>(load: () => Promise<T>, deps: React.DependencyList) {
  const [data, setData] = React.useState<T | null>(null)
  const [error, setError] = React.useState<string | null>(null)
  const [loading, setLoading] = React.useState(true)

  React.useEffect(() => {
    let alive = true
    setLoading(true)
    load()
      .then((d) => {
        if (!alive) return
        setData(d)
        setError(null)
      })
      .catch((e) => {
        if (alive) setError(errorMessage(e))
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  return { data, error, loading }
}
