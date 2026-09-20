"""SPSA tuning of the evaluation parameters with fastchess.

Each iteration perturbs every parameter by +c_k or -c_k (a random sign per parameter), plays a
short paired match between the two perturbed engines and moves the parameters towards the winner.
The schedule follows fishtest: c_k = c / k^gamma, a_k = a / (A + k)^alpha, with c and a derived from
the final perturbation size (c_end) and the final learning rate (r_end) of each parameter.

    python tools/spsa.py --iterations 600 --games 8 --tc 4+0.04

The parameters, their defaults and ranges are read from the engine's own `uci` output, so the
script needs no editing when a parameter is added. State is written to tools/spsa-state.json after
every iteration (so a run can be resumed with --resume) and a CSV log to tools/spsa-log.csv. At the
end the tuned values are printed as UCI setoption lines and as the constants to change.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import random
import re
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
WINDOWS = os.name == "nt"
TOOLS = Path(r"C:\Users\jonny\tools") if WINDOWS else Path.home() / "tools"
DEFAULT_ENGINE = (REPO / "src" / "StoopidChess.Uci" / "bin" / "Release" / "net10.0"
                  / ("stoopid-chess.exe" if WINDOWS else "stoopid-chess"))
DEFAULT_FASTCHESS = TOOLS / "fastchess" / ("fastchess-windows-x86-64/fastchess.exe" if WINDOWS
                                           else "fastchess-linux-x86-64/fastchess")
DEFAULT_OPENINGS = TOOLS / "openings-6ply.epd"
STATE = REPO / "tools" / "spsa-state.json"
LOG = REPO / "tools" / "spsa-log.csv"

ALPHA = 0.602
GAMMA = 0.101


def read_parameters(engine: Path) -> dict[str, dict]:
    """The tunable spin options the engine announces, except Hash."""
    out = subprocess.run([str(engine)], input="uci\nquit\n", capture_output=True, text=True, check=True).stdout
    parameters = {}
    for line in out.splitlines():
        m = re.match(r"option name (\S+) type spin default (-?\d+) min (-?\d+) max (-?\d+)", line)
        if m and m.group(1) != "Hash":
            name, default, lo, hi = m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4))
            parameters[name] = {"value": float(default), "default": default, "min": lo, "max": hi}
    if not parameters:
        sys.exit("the engine announced no tunable options")
    return parameters


def play(fastchess: Path, engine: Path, openings: Path, tc: str, games: int, concurrency: int,
         plus: dict[str, int], minus: dict[str, int]) -> tuple[int, int, int]:
    """Play a paired match between the two parameter sets; (wins, losses, draws) for plus."""
    command = [str(fastchess),
               "-engine", f"cmd={engine}", "name=plus", *[f"option.{k}={v}" for k, v in plus.items()],
               "-engine", f"cmd={engine}", "name=minus", *[f"option.{k}={v}" for k, v in minus.items()],
               "-each", f"tc={tc}", "-rounds", str(max(1, games // 2)), "-games", "2", "-repeat",
               "-concurrency", str(concurrency),
               "-openings", f"file={openings}", "format=epd", "order=random",
               "-report", "penta=false", "-ratinginterval", "1"]
    result = subprocess.run(command, capture_output=True, text=True)
    scores = re.findall(r"Wins: (\d+), Losses: (\d+), Draws: (\d+)", result.stdout)   # Counted for the first engine, plus
    if not scores:
        sys.stderr.write(result.stdout[-2000:] + result.stderr[-2000:])
        sys.exit("could not read the match result from fastchess")
    wins, losses, draws = map(int, scores[-1])
    return wins, losses, draws


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--engine", type=Path, default=DEFAULT_ENGINE)
    parser.add_argument("--fastchess", type=Path, default=DEFAULT_FASTCHESS)
    parser.add_argument("--openings", type=Path, default=DEFAULT_OPENINGS)
    parser.add_argument("--iterations", type=int, default=600)
    parser.add_argument("--games", type=int, default=8, help="games per iteration (paired, so even)")
    parser.add_argument("--tc", default="12+0.12")
    parser.add_argument("--concurrency", type=int, default=8)
    parser.add_argument("--r-end", type=float, default=0.002, help="final learning rate (fishtest r_end)")
    parser.add_argument("--c-fraction", type=float, default=0.05, help="final perturbation as a fraction of the range")
    parser.add_argument("--seed", type=int, default=2026)
    parser.add_argument("--resume", action="store_true", help="continue from tools/spsa-state.json")
    args = parser.parse_args()

    random.seed(args.seed)
    parameters = read_parameters(args.engine)
    start = 1
    if args.resume and STATE.exists():
        state = json.loads(STATE.read_text())
        start = state["iteration"] + 1
        for name, value in state["values"].items():
            if name in parameters:
                parameters[name]["value"] = value
        print(f"resuming at iteration {start}")

    N = args.iterations
    A = 0.1 * N
    for p in parameters.values():
        c_end = max(0.5, args.c_fraction * (p["max"] - p["min"]))
        p["c"] = c_end * N ** GAMMA
        p["a"] = args.r_end * p["c"] ** 2 * (A + N) ** ALPHA

    new_log = not LOG.exists() or not args.resume
    with LOG.open("a" if args.resume else "w", newline="") as log_file:
        log = csv.writer(log_file)
        if new_log:
            log.writerow(["iteration", "wins", "losses", "draws", "seconds", *parameters])
        for k in range(start, N + 1):
            began = time.time()
            plus, minus, deltas = {}, {}, {}
            for name, p in parameters.items():
                c_k = p["c"] / k ** GAMMA
                delta = random.choice((-1, 1))
                deltas[name] = (delta, c_k)
                plus[name] = int(round(min(p["max"], max(p["min"], p["value"] + c_k * delta))))
                minus[name] = int(round(min(p["max"], max(p["min"], p["value"] - c_k * delta))))
            wins, losses, draws = play(args.fastchess, args.engine, args.openings, args.tc, args.games, args.concurrency, plus, minus)
            games = wins + losses + draws
            result = (wins - losses) / max(1, games)   # -1 .. 1, positive when plus did better
            for name, p in parameters.items():
                delta, c_k = deltas[name]
                a_k = p["a"] / (A + k) ** ALPHA
                p["value"] = min(p["max"], max(p["min"], p["value"] + (a_k / c_k) * delta * result))
            seconds = time.time() - began
            log.writerow([k, wins, losses, draws, f"{seconds:.0f}", *[f"{p['value']:.2f}" for p in parameters.values()]])
            log_file.flush()
            STATE.write_text(json.dumps({"iteration": k, "values": {n: p["value"] for n, p in parameters.items()}}, indent=1))
            changed = ", ".join(f"{n}={p['value']:.1f}" for n, p in parameters.items() if round(p["value"]) != p["default"])
            print(f"[{k}/{N}] +{wins} -{losses} ={draws} in {seconds:.0f}s; changed: {changed or 'nothing yet'}", flush=True)

    print("\nTuned values (rounded), as UCI options and against their defaults:")
    for name, p in parameters.items():
        value = int(round(p["value"]))
        marker = "" if value == p["default"] else f"   (was {p['default']})"
        print(f"setoption name {name} value {value}{marker}")


if __name__ == "__main__":
    main()
