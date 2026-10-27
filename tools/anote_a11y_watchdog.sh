#!/system/bin/sh
# Keeps aNote's accessibility service enabled when Android disables it after a crash.
# Installed to /data/adb/service.d/ (KernelSU / Magisk) — runs as root after boot.

COMPONENT="app.anote/crc6416b736807aca2ef0.FloatingButtonAccessibilityService"
TAG="anote_a11y"
INTERVAL_SEC=25
# Avoid hammering if the service is crash-looping.
MIN_REENABLE_GAP_SEC=60
LAST_REENABLE=0

until [ "$(getprop sys.boot_completed)" = "1" ]; do
  sleep 2
done
# Short settle after cold boot; skip long wait if already up for a while.
UPTIME_SEC="$(cut -d. -f1 /proc/uptime 2>/dev/null || echo 999)"
if [ "$UPTIME_SEC" -lt 120 ]; then
  sleep 15
fi

is_enabled() {
  enabled="$(settings get secure enabled_accessibility_services 2>/dev/null)"
  [ -z "$enabled" ] && return 1
  [ "$enabled" = "null" ] && return 1
  case ":${enabled}:" in
    *":${COMPONENT}:"*) return 0 ;;
    *) return 1 ;;
  esac
}

reenable() {
  now="$(date +%s)"
  if [ "$LAST_REENABLE" -gt 0 ]; then
    delta=$((now - LAST_REENABLE))
    if [ "$delta" -lt "$MIN_REENABLE_GAP_SEC" ]; then
      log -t "$TAG" "skip re-enable (gap ${delta}s < ${MIN_REENABLE_GAP_SEC}s)"
      return 0
    fi
  fi

  enabled="$(settings get secure enabled_accessibility_services 2>/dev/null)"
  # Rebuild list without duplicates, then ensure our component is present once.
  new=""
  OLD_IFS="$IFS"
  IFS=':'
  # shellcheck disable=SC2086
  for part in $enabled; do
    [ -z "$part" ] && continue
    [ "$part" = "null" ] && continue
    [ "$part" = "$COMPONENT" ] && continue
    if [ -z "$new" ]; then
      new="$part"
    else
      new="${new}:${part}"
    fi
  done
  IFS="$OLD_IFS"
  if [ -z "$new" ]; then
    new="$COMPONENT"
  else
    new="${new}:${COMPONENT}"
  fi

  settings put secure enabled_accessibility_services "$new"
  settings put secure accessibility_enabled 1
  LAST_REENABLE="$now"
  log -t "$TAG" "re-enabled ${COMPONENT}"
}

while true; do
  if ! is_enabled; then
    reenable
  fi
  sleep "$INTERVAL_SEC"
done
