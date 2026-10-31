#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
export ANOTE_WEB_HOST="${ANOTE_WEB_HOST:-0.0.0.0}"
export ANOTE_WEB_PORT="${ANOTE_WEB_PORT:-8787}"
dotnet build ./decrypt/AnoteDecrypt.csproj -c Release -v q
exec python3 ./server.py
