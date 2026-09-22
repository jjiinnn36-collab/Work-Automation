#!/bin/bash
# 정리 화면(웹)을 켜고 기본 브라우저로 연다. 서버가 이미 떠 있으면 그대로 쓴다.
cd "$(dirname "$0")/.."
PORT="${PA_PORT:-8317}"

if ! curl -s "http://localhost:$PORT/api/health" >/dev/null 2>&1; then
  # 서버를 백그라운드로 띄운다.
  nohup node server.js >> server.out.log 2>&1 &
  # 뜰 때까지 잠깐 기다린다.
  for i in $(seq 1 20); do
    sleep 0.3
    curl -s "http://localhost:$PORT/api/health" >/dev/null 2>&1 && break
  done
fi

open "http://localhost:$PORT/"
