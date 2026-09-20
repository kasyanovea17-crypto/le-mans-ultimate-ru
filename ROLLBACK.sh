#!/bin/sh
set -eu
case "$0" in */*) home=${0%/*} ;; *) home=. ;; esac
if [ "$#" -ne 1 ]; then
  printf '%s\n' 'Usage: sh ROLLBACK.sh "C:/.../Le Mans Ultimate"'
  exit 2
fi
if [ -f "$home/LMU-RU-Launcher.exe" ]; then exe="$home/LMU-RU-Launcher.exe"; else exe="$home/build/LMU-RU-Launcher.exe"; fi
exec "$exe" --cli restore "$1" "$home/rollback-result.json"
