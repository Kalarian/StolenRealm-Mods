#!/bin/bash
# Rebuild the one-file installer after the share zip changes, and drop it next to the zip.
set -e
cd "$(dirname "$0")/../mods/Installer"
dotnet build -c Release --no-incremental 2>&1 | grep -E "error|Build succeeded"
cp "bin/Release/Install Stolen Realm Mods.exe" "../share/Install Stolen Realm Mods.exe"
ls -la "../share/Install Stolen Realm Mods.exe" | awk '{print $5 " bytes -> mods/share/Install Stolen Realm Mods.exe"}'
