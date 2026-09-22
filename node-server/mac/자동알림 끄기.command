#!/bin/bash
# 매일 아침 알림 자동 실행을 끈다.
LABEL="com.paymentalert.popup"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
launchctl unload "$PLIST" 2>/dev/null || true
rm -f "$PLIST"
echo "자동 알림을 껐습니다."
read -n 1 -s -r -p "아무 키나 누르면 닫힙니다."
