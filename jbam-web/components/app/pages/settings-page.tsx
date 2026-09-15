"use client"

import * as React from "react"
import { CheckCircle2Icon, DatabaseBackupIcon, RefreshCwIcon, TriangleAlertIcon } from "lucide-react"

import { errorMessage, get, post } from "@/lib/api"
import type { SettingsData } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useApp } from "@/components/app/app-context"
import { LoadError, PageHeader } from "@/components/app/parts"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Field, FieldDescription, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { toast } from "@/components/ui/toast"

function size(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

/** 설정 — 추적 시작일·공휴일·백업·프로그램 상태 (ADR-0008). */
export function SettingsPage() {
  const app = useApp()
  const [tick, setTick] = React.useState(0)
  const { data, error } = useLoad(() => get<SettingsData>("/api/settings"), [app.version, tick])
  const [startDate, setStartDate] = React.useState("")
  const [apiKey, setApiKey] = React.useState("")
  const [busy, setBusy] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (data) setStartDate(data.startDate ?? "")
  }, [data?.startDate]) // eslint-disable-line react-hooks/exhaustive-deps

  // 공휴일을 받아 오는 동안에는 결과가 나올 때까지 자주 묻는다.
  React.useEffect(() => {
    if (!data?.holidayJob.running) return
    const t = setTimeout(() => setTick((n) => n + 1), 1500)
    return () => clearTimeout(t)
  }, [data])

  if (error && !data) return <LoadError message={error} />
  if (!data) return <Skeleton className="h-96 rounded-xl" />

  async function run(key: string, fn: () => Promise<unknown>, ok: string) {
    setBusy(key)
    try {
      await fn()
      toast.add({ title: ok, type: "success" })
      setTick((n) => n + 1)
      app.refresh()
    } catch (e) {
      toast.add({ title: "처리하지 못했습니다", description: errorMessage(e), type: "error" })
    } finally {
      setBusy(null)
    }
  }

  const integrityOk = data.integrity === "ok"

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title="설정" description="자료 상태와 운영 설정" />

      {data.warnings.map((w) => (
        <Alert key={w}>
          <TriangleAlertIcon />
          <AlertTitle>자료 확인이 필요합니다</AlertTitle>
          <AlertDescription>{w}</AlertDescription>
        </Alert>
      ))}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>추적 시작일</CardTitle>
            <CardDescription>이 날짜보다 기한이 이른 건은 다루지 않습니다. 프로그램을 쓰기 전의 건이 &lsquo;기한 지남&rsquo; 으로 잡히지 않게 합니다.</CardDescription>
          </CardHeader>
          <CardContent>
            <Field>
              <FieldLabel htmlFor="set-start">날짜</FieldLabel>
              <Input id="set-start" type="date" className="w-48" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
              <FieldDescription>지금: {data.startDate ?? "제한 없음"}</FieldDescription>
            </Field>
          </CardContent>
          <CardFooter className="gap-2">
            <Button disabled={busy !== null || startDate === (data.startDate ?? "")} onClick={() => run("start", () => post("/api/settings/start-date", { date: startDate }), "추적 시작일을 저장했습니다")}>
              {busy === "start" && <Spinner />} 저장
            </Button>
            {data.startDate && (
              <Button variant="outline" disabled={busy !== null} onClick={() => run("start", () => post("/api/settings/start-date", { date: "" }), "추적 시작일을 비웠습니다")}>
                제한 없애기
              </Button>
            )}
          </CardFooter>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>공휴일</CardTitle>
            <CardDescription>영업일 계산에 씁니다. 자료가 없는 해는 주말만 반영하고 알림을 이틀 앞당깁니다.</CardDescription>
            <CardAction>
              <Badge variant="outline">{data.holidayCount}건</Badge>
            </CardAction>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-wrap items-center gap-1.5 text-sm">
              <span className="text-muted-foreground">자료가 있는 해</span>
              {data.holidayYears.map((y) => <Badge key={y} variant="secondary">{y}</Badge>)}
              <span className="ml-auto text-muted-foreground">갱신 {data.holidayUpdated ?? "기록 없음"}</span>
            </div>
            <Field>
              <FieldLabel htmlFor="set-key">공공데이터포털 인증키</FieldLabel>
              <div className="flex gap-2">
                <Input id="set-key" type="password" autoComplete="off" placeholder={data.apiKeySet ? "저장되어 있음 — 바꾸려면 새 키 입력" : "인증키 붙여 넣기"} value={apiKey} onChange={(e) => setApiKey(e.target.value)} />
                <Button variant="outline" disabled={!apiKey.trim() || busy !== null} onClick={() => run("key", async () => { await post("/api/settings/apikey", { key: apiKey.trim() }); setApiKey("") }, "인증키를 저장했습니다")}>
                  저장
                </Button>
              </div>
              <FieldDescription>
                &lsquo;한국천문연구원_특일 정보&rsquo; 활용신청을 한 키여야 합니다. 키는 이 PC 의 자료 폴더에만 저장되고 화면에 다시 보이지 않습니다.
              </FieldDescription>
            </Field>
            {data.holidayJob.message && (
              <p className="rounded-md bg-muted px-3 py-2 text-sm">{data.holidayJob.message}</p>
            )}
          </CardContent>
          <CardFooter className="gap-2">
            <Button disabled={!data.apiKeySet || data.holidayJob.running || busy !== null} onClick={() => run("holiday", () => post("/api/holidays/refresh"), "공휴일을 받아 오기 시작했습니다")}>
              {data.holidayJob.running ? <Spinner /> : <RefreshCwIcon data-icon="inline-start" />} 지금 공휴일 받기
            </Button>
            {data.apiKeySet && (
              <Button variant="ghost" disabled={busy !== null} onClick={() => run("key", () => post("/api/settings/apikey", { key: "" }), "인증키를 지웠습니다")}>
                인증키 지우기
              </Button>
            )}
          </CardFooter>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>백업</CardTitle>
            <CardDescription>하루 첫 실행 때 자동으로 사본을 만들고 최근 30개를 남깁니다. 다른 디스크에 두려면 실행 폴더에 backup-folder.txt 를 두세요.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <code className="rounded-md bg-muted px-2 py-1 text-xs break-all">{data.backupDir}</code>
            {data.backups.length === 0 ? (
              <p className="text-sm text-muted-foreground">아직 사본이 없습니다.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>파일</TableHead>
                    <TableHead>만든 시각</TableHead>
                    <TableHead className="text-right">크기</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.backups.map((b) => (
                    <TableRow key={b.name}>
                      <TableCell className="font-mono text-xs">{b.name}</TableCell>
                      <TableCell className="tabular-nums text-muted-foreground">{b.at}</TableCell>
                      <TableCell className="text-right tabular-nums">{size(b.size)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
          <CardFooter>
            <Button variant="outline" disabled={busy !== null} onClick={() => run("backup", () => post("/api/backup"), "사본을 만들었습니다")}>
              {busy === "backup" ? <Spinner /> : <DatabaseBackupIcon data-icon="inline-start" />} 지금 백업
            </Button>
          </CardFooter>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>프로그램 정보</CardTitle>
            <CardDescription>문제가 생기면 이 내용과 실행 기록 파일을 함께 확인하세요.</CardDescription>
            <CardAction>
              {integrityOk ? (
                <Badge variant="secondary"><CheckCircle2Icon /> 자료 파일 정상</Badge>
              ) : (
                <Badge variant="destructive">자료 파일 점검 필요</Badge>
              )}
            </CardAction>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-[7rem_1fr] gap-x-3 gap-y-2 text-sm">
              <dt className="text-muted-foreground">판</dt>
              <dd>v{data.version} · 자료 {data.schema}판</dd>
              {!integrityOk && (
                <>
                  <dt className="text-muted-foreground">점검 결과</dt>
                  <dd className="text-destructive">{data.integrity}</dd>
                </>
              )}
              <dt className="text-muted-foreground">자료 폴더</dt>
              <dd className="font-mono text-xs break-all">{data.dataDir}</dd>
              <dt className="text-muted-foreground">DB 파일</dt>
              <dd className="font-mono text-xs break-all">{data.dbPath}</dd>
              <dt className="text-muted-foreground">실행 기록</dt>
              <dd className="font-mono text-xs break-all">{data.logPath}</dd>
            </dl>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
