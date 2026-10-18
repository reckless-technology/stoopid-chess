#!/usr/bin/env bash
# Play a fastchess match between the Stoopid Chess engine and another UCI engine.
#
# Builds the engine in Release, then runs paired games (each opening played with both colors)
# from an openings file. Both engines are deterministic, so an openings file is essential.
# The Linux and macOS twin of tools/match.ps1. fastchess defaults to the build tools/setup-dev.sh --tuning installs
# under ~/tools for this platform (tools/tuning-tools.txt), else the one on the PATH; the openings to the repo's own.
# The opponent below is the reference pawnstar on Linux; on an Apple-silicon Mac it is
# ~/tools/pawnstar/pawnstar-0.13.593-macos-arm64.
#
#   tools/match.sh --opponent ~/tools/pawnstar/pawnstar-0.13.593-linux-x86_64 \
#       --opponent-option option.Threads=1 --opponent-option option.OwnBook=false \
#       --rounds 20 --tc 12+0.12
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
opponent=""
opponent_options=()
case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) platform=macos-arm64 ;;
    Darwin-*) platform=macos-x64 ;;
    *-aarch64 | *-arm64) platform=linux-arm64 ;;
    *) platform=linux-x64 ;;
esac
fastchess="$HOME/tools/$(awk -v p="$platform" '$1 == "fastchess" && $3 == p { print $4; exit }' "$repo/tools/tuning-tools.txt")"
if [ ! -x "$fastchess" ] && command -v fastchess >/dev/null 2>&1; then fastchess=fastchess; fi
openings="$repo/tools/openings-6ply.epd"
tc="12+0.12"
rounds=20
concurrency=8
pgnout="match.pgn"

usage() {
    echo "usage: tools/match.sh --opponent <uci engine> [--opponent-option option.X=Y]..." >&2
    echo "       [--fastchess <path>] [--openings <epd>] [--tc 12+0.12] [--rounds 20]" >&2
    echo "       [--concurrency 8] [--pgn match.pgn]" >&2
    exit 2
}

while [ $# -gt 0 ]; do
    case "$1" in
        --opponent) opponent="$2"; shift 2 ;;
        --opponent-option) opponent_options+=("$2"); shift 2 ;;
        --fastchess) fastchess="$2"; shift 2 ;;
        --openings) openings="$2"; shift 2 ;;
        --tc) tc="$2"; shift 2 ;;
        --rounds) rounds="$2"; shift 2 ;;
        --concurrency) concurrency="$2"; shift 2 ;;
        --pgn) pgnout="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; usage ;;
    esac
done
[ -n "$opponent" ] || usage

dotnet build "$repo/src/StoopidChess.Uci/StoopidChess.Uci.csproj" -c Release > /dev/null
engine="$repo/src/StoopidChess.Uci/bin/Release/net10.0/stoopid-chess"
opponent_name="$(basename "$opponent")"
opponent_name="${opponent_name%.exe}"

"$fastchess" \
    -engine "cmd=$engine" name=StoopidChess \
    -engine "cmd=$opponent" "name=$opponent_name" ${opponent_options[@]+"${opponent_options[@]}"} \
    -each "tc=$tc" -rounds "$rounds" -games 2 -repeat -concurrency "$concurrency" \
    -openings "file=$openings" format=epd order=random \
    -pgnout "file=$pgnout" -ratinginterval 10
