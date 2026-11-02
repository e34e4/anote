#!/system/bin/sh
# Keeps aNote's accessibility service enabled when Android disables it after a crash.
# Installed to /data/adb/service.d/ (KernelSU / Magisk) — runs as root after boot.
#
# Component id is discovered dynamically (CRC class names change between builds).
# Prefer the hint file written by the app; fall back to dumpsys package.

TAG="anote_a11y"
INTERVAL_SEC=25
# Avoid hammering if the service is crash-looping.
MIN_REENABLE_GAP_SEC=60
LAST_REENABLE=0
COMPONENT=""
HINT_FILE="/data/local/tmp/anote_a11y_component"
PACKAGE="app.anote"

until [ "$(getprop sys.boot_completed)" = "1" ]; do
  sleep 2
done
# Short settle after cold boot; skip long wait if already up for a while.
UPTIME_SEC="$(cut -d. -f1 /proc/uptime 2>/dev/null || echo 999)"
if [ "$UPTIME_SEC" -lt 120 ]; then
  sleep 15
fi

resolve_component() {
  # 1) Hint written by AccessibilityServiceWatchdog / OnServiceConnected
  if [ -f "$HINT_FILE" ]; then
    hint="$(tr -d '\r\n' < "$HINT_FILE" 2>/dev/null)"
    case "$hint" in
      ${PACKAGE}/*FloatingButtonAccessibilityService)
        COMPONENT="$hint"
        return 0
        ;;
    esac
  fi

  # 2) Discover from package dump (works even before first app launch after install)
  found="$(dumpsys package "$PACKAGE" 2>/dev/null | grep -oE "${PACKAGE}/crc[0-9a-f]+\.FloatingButtonAccessibilityService" | head -1)"
  if [ -n "$found" ]; then
    COMPONENT="$found"
    return 0
  fi

  # 3) Already-enabled list may still have the old or current component
  enabled="$(settings get secure enabled_accessibility_services 2>/dev/null)"
  case ":${enabled}:" in
    *":${PACKAGE}/"*"FloatingButtonAccessibilityService:"*)
      COMPONENT="$(printf '%s' "$enabled" | tr ':' '\n' | grep "FloatingButtonAccessibilityService" | head -1)"
      [ -n "$COMPONENT" ] && return 0
      ;;
  esac

  return 1
}

is_enabled() {
  [ -z "$COMPONENT" ] && return 1
  enabled="$(settings get secure enabled_accessibility_services 2>/dev/null)"
  [ -z "$enabled" ] && return 1
  [ "$enabled" = "null" ] && return 1
  case ":${enabled}:" in
    *":${COMPONENT}:"*) return 0 ;;
    *) return 1 ;;
  esac
}

reenable() {
  [ -z "$COMPONENT" ] && return 1
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
    # Drop stale CRC variants of the same service so only the current one remains.
    case "$part" in
      ${PACKAGE}/*FloatingButtonAccessibilityService) continue ;;
    esac
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
  resolve_component || true
  if [ -n "$COMPONENT" ] && ! is_enabled; then
    reenable
  fi
  sleep "$INTERVAL_SEC"
done
