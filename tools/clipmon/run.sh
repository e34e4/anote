#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
export CLIPMON_HOST="${CLIPMON_HOST:-0.0.0.0}"
export CLIPMON_PORT="${CLIPMON_PORT:-8787}"
exec python3 ./server.py
