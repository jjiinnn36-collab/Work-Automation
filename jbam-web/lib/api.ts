"use client"

// 내 PC 서버(PaymentAlert.exe --web)와 주고받는 함수. 변경 요청에는 X-PaymentAlert 머리글을 붙인다 (ADR-0003).

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function read<T>(res: Response): Promise<T> {
  let body: unknown = null
  try {
    body = await res.json()
  } catch {
    body = null
  }
  if (!res.ok) {
    const msg = (body as { error?: string } | null)?.error ?? `요청을 처리하지 못했습니다 (${res.status})`
    throw new ApiError(res.status, msg)
  }
  return body as T
}

type Params = Record<string, string | number | boolean | null | undefined>

function query(params?: Params): string {
  if (!params) return ""
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== null && v !== undefined && v !== "") q.set(k, String(v))
  const s = q.toString()
  return s ? `?${s}` : ""
}

export async function get<T>(path: string, params?: Params): Promise<T> {
  return read<T>(await fetch(path + query(params), { cache: "no-store" }))
}

export async function post<T>(path: string, data?: Params): Promise<T> {
  const body = new URLSearchParams()
  for (const [k, v] of Object.entries(data ?? {})) if (v !== null && v !== undefined) body.set(k, String(v))
  return read<T>(
    await fetch(path, {
      method: "POST",
      headers: { "X-PaymentAlert": "1", "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" },
      body,
    })
  )
}

/** 파일 본문을 그대로 보낸다 (증빙·받은 문서 첨부). */
export async function upload<T>(path: string, params: Params, file: File): Promise<T> {
  return read<T>(
    await fetch(path + query(params), {
      method: "POST",
      headers: {
        "X-PaymentAlert": "1",
        "X-File-Name": encodeURIComponent(file.name),
        "Content-Type": "application/octet-stream",
      },
      body: file,
    })
  )
}

export function fileUrl(year: number, id: string, f: string): string {
  return "/api/file" + query({ y: year, id, f })
}

export function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

/** 브라우저에서 CSV 를 내려받는다. */
export function downloadText(name: string, text: string) {
  const a = document.createElement("a")
  a.href = URL.createObjectURL(new Blob([text], { type: "text/csv;charset=utf-8" }))
  a.download = name
  document.body.appendChild(a)
  a.click()
  setTimeout(() => {
    URL.revokeObjectURL(a.href)
    a.remove()
  }, 0)
}
