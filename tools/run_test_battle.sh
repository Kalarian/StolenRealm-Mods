#!/bin/bash
# Smoke test: back up the saves, launch the game with -srtest (TestDriver plays a debug battle with the AI), restore the saves, report.
# Usage: bash tools/run_test_battle.sh [headless|windowed] [partysize] [Name1,Name2,...] [battle-name-substring, no spaces, e.g. Statue]
set -u
GAME="C:/Program Files (x86)/Steam/steamapps/common/Stolen Realm"
SAVES="$USERPROFILE/AppData/LocalLow/Burst2Flame Entertainment/Stolen Realm"
MODE="${1:-headless}"; PARTY="${2:-3}"; CHARS="${3:-}"; BATTLE="${4:-}"
EXTRA=""; [ -n "$CHARS" ] && EXTRA="-srchars $CHARS"; [ -n "$BATTLE" ] && EXTRA="$EXTRA -srbattle $BATTLE"
STAMP=$(date +%Y%m%d-%H%M%S)
BK="$SAVES/../Stolen Realm.testbackup-$STAMP"
mkdir -p "$BK" && cp "$SAVES"/*.json "$BK"/ 2>/dev/null && cp "$SAVES"/*.backup "$BK"/ 2>/dev/null; echo "saves backed up to $BK"
rm -f "$GAME/BepInEx/TestDriver/result.txt"
cd "$GAME"
if [ "$MODE" = "windowed" ]; then
  timeout 720 "./Stolen Realm.exe" -srtest -srparty "$PARTY" $EXTRA -screen-width 1280 -screen-height 720 -screen-fullscreen 0 >/dev/null 2>&1
else
  timeout 720 "./Stolen Realm.exe" -srtest -srparty "$PARTY" $EXTRA -batchmode -nographics >/dev/null 2>&1
fi
echo "game exited (code $?)"
# restore every save file the run may have touched
cp "$BK"/*.json "$SAVES"/ && cp "$BK"/*.backup "$SAVES"/ 2>/dev/null; echo "saves restored"
echo "=== result"; cat "$GAME/BepInEx/TestDriver/result.txt" 2>/dev/null || echo "(no result file: the driver never finished)"
echo "=== driver + errors from the log"
n=$(grep -n "SESSION START" "$GAME/BepInEx/LogOutput.log" | tail -1 | cut -d: -f1)
tail -n +"$n" "$GAME/BepInEx/LogOutput.log" | grep -n "TESTDRIVER\|Error\|Exception\|BATTLE SUMMARY\|Threat overlay:" | grep -v "Unity Log" | head -60 | cut -c1-200
