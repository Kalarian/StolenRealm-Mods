#!/bin/bash
# Publish a new mod pack release to GitHub (public repo Kalarian/StolenRealm-Mods).
# Usage: bash tools/publish.sh <version> "<one-line release notes>"     e.g. bash tools/publish.sh 1.4.1 "Poison credits, mod window"
# Steps: write VERSION, pack the zip (adds version.txt), rebuild the installer (embeds the zip), write SHA256SUMS.txt,
# commit + tag, create the GitHub release with the zip, the installer, the checksums and the PDF as assets.
# Friends' installers ask GitHub for the latest release and download the zip when it is newer than the one they carry.
set -e
cd "$(dirname "$0")/.."
GH="/c/Program Files/GitHub CLI/gh.exe"
[ -x "$GH" ] || GH=gh
VER="$1"; NOTES="${2:-Mod pack v$1}"
[ -n "$VER" ] || { echo "usage: bash tools/publish.sh <version> \"<notes>\""; exit 1; }
if tasklist | grep -qi "stolen realm"; then echo "Close the game first (its configs are packed as-is)."; exit 1; fi
echo "$VER" > VERSION
python tools/pack_zip.py
bash tools/build_installer.sh
cd mods/share
sha256sum "../StolenRealm-Mods-install.zip" "Install Stolen Realm Mods.exe" | sed 's#\.\./##' > SHA256SUMS.txt
cat SHA256SUMS.txt
cd ../..
git add -A
git commit -q -m "Release v$VER: $NOTES" || echo "(nothing new to commit)"
git tag -f "v$VER"
git push -q origin main
git push -q -f origin "v$VER"   # -f: a tag that already exists on GitHub (re-release) is moved, not rejected
"$GH" release delete "v$VER" --yes --cleanup-tag 2>/dev/null || true   # re-publishing the same version replaces the release
git push -q -f origin "v$VER"
"$GH" release create "v$VER" "mods/StolenRealm-Mods-install.zip" "mods/share/Install Stolen Realm Mods.exe" "mods/share/SHA256SUMS.txt" "mods/share/Stolen Realm Mods - Read Me.pdf" --title "Stolen Realm Mods v$VER" --notes "$NOTES"
echo "Published v$VER: https://github.com/Kalarian/StolenRealm-Mods/releases/tag/v$VER"
