#!/usr/bin/env bash
# Install / refresh the aNote accessibility root watchdog on a USB device.
# Usage: ./tools/install_a11y_watchdog.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/anote_a11y_watchdog.sh"
adb push "$SRC" /data/local/tmp/anote_a11y_watchdog.sh >/dev/null
adb shell chmod 755 /data/local/tmp/anote_a11y_watchdog.sh

adb shell 'su -c "
pkill -f anote_a11y_watchdog.sh 2>/dev/null || true
sleep 1
# Prefer Magisk/KSU service.d when writable; otherwise run from /data/local/tmp until reboot.
if touch /data/adb/service.d/.anote_w 2>/dev/null; then
  rm -f /data/adb/service.d/.anote_w
  cp /data/local/tmp/anote_a11y_watchdog.sh /data/adb/service.d/anote_a11y_watchdog.sh
  chmod 755 /data/adb/service.d/anote_a11y_watchdog.sh
  echo installed_service_d
else
  echo service_d_not_writable_using_tmp
fi
setsid sh /data/local/tmp/anote_a11y_watchdog.sh </dev/null >/data/local/tmp/anote_a11y.log 2>&1 &
sleep 1
ps -A -o PID,ARGS | grep anote_a11y_watchdog | grep -v grep || true
"'
