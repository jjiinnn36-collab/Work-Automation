#!/bin/bash
# 알림 팝업을 지금 바로 앱 창으로 띄운다 (처리할 건이 없어도 --always 로 보여 준다).
cd "$(dirname "$0")/.."
node bin/popup-open.js --always
