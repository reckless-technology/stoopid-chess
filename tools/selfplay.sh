#!/usr/bin/env bash
# Self-play match of the current engine against a saved baseline build.
#
# Snapshot a baseline first with:  dotnet publish src/StoopidChess.Uci -c Release -o ~/tools/stoopid-baseline
# Then after a search or evaluation change:  tools/selfplay.sh
# The result tells you whether the change gained strength; the unit tests cannot.
# The Linux twin of tools/selfplay.ps1; the defaults are the standard test setting,
# 100 games (50 paired rounds) at 12+0.12.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
baseline="$HOME/tools/stoopid-baseline/stoopid-chess"
tc="12+0.12"
rounds=50
concurrency=8
pgnout="selfplay.pgn"

while [ $# -gt 0 ]; do
    case "$1" in
        --baseline) baseline="$2"; shift 2 ;;
        --tc) tc="$2"; shift 2 ;;
        --rounds) rounds="$2"; shift 2 ;;
        --concurrency) concurrency="$2"; shift 2 ;;
        --pgn) pgnout="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2
           echo "usage: tools/selfplay.sh [--baseline <engine>] [--tc 12+0.12] [--rounds 50] [--concurrency 8] [--pgn selfplay.pgn]" >&2
           exit 2 ;;
    esac
done

exec "$repo/tools/match.sh" --opponent "$baseline" --tc "$tc" --rounds "$rounds" \
    --concurrency "$concurrency" --pgn "$pgnout"
