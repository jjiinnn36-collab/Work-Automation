#!/bin/bash
# 매일 아침 9시에 알림 팝업이 뜨도록 자동 실행을 등록한다 (macOS launchd).
# 로그인 상태에서만 돈다. 자료는 이 PC 밖으로 나가지 않는다.
set -e
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
NODE="$(command -v node || true)"
if [ -z "$NODE" ]; then
  echo "Node.js 를 찾지 못했습니다. 먼저 https://nodejs.org 에서 Node 를 설치하세요."
  echo "설치 후 이 파일을 다시 실행하면 됩니다."
  read -n 1 -s -r -p "아무 키나 누르면 닫힙니다."
  exit 1
fi

LABEL="com.paymentalert.popup"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
HOUR="${1:-9}"     # 첫 번째 인자로 시각(시)을 바꿀 수 있다. 기본 9시.

mkdir -p "$HOME/Library/LaunchAgents"
cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>$NODE</string>
    <string>$ROOT/bin/popup-open.js</string>
  </array>
  <key>WorkingDirectory</key><string>$ROOT</string>
  <key>StartCalendarInterval</key>
  <dict><key>Hour</key><integer>$HOUR</integer><key>Minute</key><integer>0</integer></dict>
  <key>StandardOutPath</key><string>$ROOT/popup.out.log</string>
  <key>StandardErrorPath</key><string>$ROOT/popup.out.log</string>
</dict>
</plist>
PLISTEOF

# 다시 등록 (이미 있으면 지우고 새로).
launchctl unload "$PLIST" 2>/dev/null || true
launchctl load "$PLIST"

echo "매일 아침 ${HOUR}시에 알림 팝업이 뜨도록 등록했습니다."
echo "끄려면 '자동알림 끄기.command' 를 실행하세요."
read -n 1 -s -r -p "아무 키나 누르면 닫힙니다."
