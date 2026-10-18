# Stoopid Chess

[![CI](https://github.com/reckless-technology/stoopid-chess/actions/workflows/ci.yml/badge.svg)](https://github.com/reckless-technology/stoopid-chess/actions/workflows/ci.yml)

A chess engine and a cross-platform chess GUI (Avalonia) in C#. The engine speaks
[UCI](https://www.chessprogramming.org/UCI), so it can be used from any chess GUI and tested against other engines;
the GUI can play against any UCI engine, with the built-in one as the default. Originally written in 2009 as a Windows
Forms program, ported to .NET 10 and restructured in 2026; the Avalonia GUI replaced the Windows Forms one after
3.0.0.

## Layout

| Project | Purpose |
|---|---|
| `src/StoopidChess.Core` | The rules of chess: the `Square`, `Bitboard`, `Move` and `MoveList` value types (a colored piece is a `(Color, Piece)` pair), `Position` (bitboards plus a square array, with incremental Zobrist keys), `Attacks` (attack tables and magic bitboards), FEN, legal move generation, `Game` (move history and result detection), perft. Cross-platform, no dependencies. |
| `src/StoopidChess.Engine` | The engine: `Evaluator`, `TranspositionTable`, `Searcher` (iterative deepening alpha-beta with quiescence), `SearchLimits`, `UciSession` (the protocol handler) and `UciEngineClient` (drives any UCI engine, in-process or external). Cross-platform. |
| `src/StoopidChess.Play` | `GameController`: the state machine behind a chess GUI (game, engine lifecycle, thinking, editing, clocks, save/load) with no UI, so it is tested on Linux against the in-process engine. Also `Level` and `SavedGame`. |
| `src/StoopidChess.Uci` | Console program `stoopid-chess`: the engine as a UCI executable, plus `bench` and `perft` subcommands. |
| `src/StoopidChess.Desktop` | Avalonia GUI for Linux, macOS and Windows: drawing, mouse and menus over `GameController`, talking to the engine only through UCI, and a small UCI host — registered external engines and Fischer time controls with flagging. |
| `tests/StoopidChess.Tests` | xunit tests for everything above. |

Everything builds and runs on Linux, macOS and Windows.

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

**Setting up a machine.** `tools/setup-dev.ps1` (Windows) and `tools/setup-dev.sh` (Linux, macOS) install what
building, testing and debugging need - the .NET SDK of `Directory.Build.props`' target framework (per-user, through
Microsoft's dotnet-install script, when no such SDK is on the PATH), git, on Linux the libraries .NET and the GUI load -
plus the git hooks and the recommended VS Code extensions, checking each first, so they are safe to run again;
`-Check` / `--check` only reports. `-Tuning` / `--tuning` adds what the match and self-play scripts play with, under
`~/tools`: fastchess and the reference pawnstar, the release binaries `tools/tuning-tools.txt` pins by version and
SHA-256 (pawnstar has none for Intel Macs, Linux on arm64 or Windows on arm64), and a first self-play baseline. In VS
Code they are the `setup environment`, `setup environment + tuning tools` and `check environment` tasks.

```bash
dotnet build StoopidChess.slnx -c Release          # everything (any OS)
dotnet build src/StoopidChess.Uci -c Release       # engine only (any OS)
dotnet test tests/StoopidChess.Tests -c Release    # the test suite
dotnet test tests/StoopidChess.Tests -c Release --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults/coverage
dotnet run tools/coverage-gate.cs -- TestResults/coverage --uncovered   # line coverage per assembly, the 90% gate
dotnet run --project src/StoopidChess.Desktop      # play (any OS, Avalonia)
dotnet run --project src/StoopidChess.Uci -c Release -- bench      # search signature and speed
dotnet run --project src/StoopidChess.Uci -c Release -- perft      # move generation check
```

Warnings are errors and nullable reference types are on throughout. `.vscode/` has tasks for all of
the above and launch configurations for debugging the GUI and the engine.

## The engine

- Bitboard board representation (a set of squares per piece type and per color, plus a square
  array), magic bitboards for the sliding pieces, and a fully legal move generator that resolves
  pins, checks, en passant and castling during generation rather than by make/test/unmake. Modelled
  on pawnstar; the design and measurements are in `docs/BITBOARDS.md`.
- Squares, bitboards and move lists are small structs (`Square`, `Bitboard`, `MoveList`) rather
  than raw integers and arrays, at no run-time cost: the bench signature and speed are unchanged
  by them.
- Zobrist keys for the position and for the pawn structure, generated from fixed seeds so every run
  is reproducible; 16-byte transposition entries verified by the full 64-bit key.
- Negamax alpha-beta (principal variation search) with iterative deepening and aspiration windows, a two-tier transposition table (depth-preferred and
  always-replace), null-move pruning, late move reductions, killer moves, a countermove, history and one-ply continuation history heuristics, MVV/LVA capture ordering, a quiescence search that skips captures losing material by static exchange evaluation,
  with stand-pat, a check extension at the horizon, and mate distance pruning.
- Evaluation: the Simplified Evaluation Function (material and piece-square tables, with a separate
  king table in the endgame) as a baseline, plus whatever terms self-play has since justified;
  `docs/EVALUATION.md` is the log of every term tried, kept or not.
- UCI: `uci`, `isready`, `setoption name Hash`, `ucinewgame`, `position`, `go` (depth, nodes,
  movetime, wtime/btime/winc/binc/movestogo, infinite), `stop`, `quit`; plus `d`, `eval`,
  `perft N` and `bench [depth]`.

The search is deterministic, so the `bench` node count is a signature of the search and evaluation.
Current values: depth 5 = 42,531 nodes, depth 6 = 79,698 nodes. A test asserts the depth-5
figure; update it (and CLAUDE.md) whenever a change to search or evaluation is intended.

## Testing

Modelled on the sibling engines pawnstar and zenith:

- **Perft** against the 128 standard positions (Ethereal's `standard.epd`, embedded in Core) is the
  move generation gate. The unit tests run each position to the deepest depth under 300k nodes;
  `stoopid-chess perft [max million nodes]` runs the full set (about 80 million nodes per second).
- **Move generation** is also cross-checked against a deliberately naive reference generator on the
  standard positions, their children and random games, with the awkward cases (en passant that
  exposes the king along the rank, pinned pawns, double check, castling through attacked squares)
  asserted directly; the magic bitboard tables are checked against ray walking on random occupancies.
- **Make/unmake invariants**: random games check that the incremental keys match a recomputation
  after every move, that the bitboards and square array agree, and that unwinding restores the
  start position exactly.
- **Evaluation regression**: the scores of eighteen positions, re-frozen whenever an evaluation
  term is accepted, so that refactoring can prove it changed nothing.
- **Rules**: checkmate, stalemate, repetition, the fifty-move rule, insufficient material,
  castling rights, en passant, promotions, FEN round trips.
- **Search**: mates in one and two, being mated, draw recognition, determinism, limits and
  cancellation, principal variation legality.
- **UCI**: the session driven through in-memory streams, and the client talking to the engine over
  pipes exactly as the GUI does; the `stoopid-chess` console's command line, UCI loop, bench and perft.
- **Play**: `GameController` against the in-process engine and against scripted fake engines (an illegal
  reply, an engine dying mid-game, an engine match whose engines will not start or stop answering), the
  clocks and time controls, saved games and the engine roster.
- **GUI**, under Avalonia's headless platform: the dialogs' choices, the board drawn in every state and its
  drags and right clicks turned into squares, the capture bin, the message box, and the window's text
  (`GameText`: the move list, the status bar, the analysis lines, the PGN names and result).
- **Bench signature**.

**Coverage.** CI fails the Linux job when the line coverage of the product code falls under 90%
(`tools/coverage-gate.cs` holds the threshold, the one place it lives; currently about 97%). The gated code is
Core, Engine, Play, the Uci console and the Desktop GUI. Left out (`tests/coverage.runsettings`): the test
assembly; the GUI's XAML code-behind (`*.axaml.cs`: `MainWindow`'s menus, dialogs, file pickers and event wiring,
`App`'s start-up) and the code generated into `obj/` - layout and wiring, which the headless tests construct but
cannot judge, while the logic the window shows lives in `GameText` and `EngineRosterFile` and is gated; and
`WindowsConsole.cs`, Win32 console calls that run only on Windows when the GUI is asked for its version or its
smoke check from a command prompt. The two commands under "Build and test" measure it locally; `--uncovered`
lists every line no test runs.

CI (`.github/workflows/ci.yml`) runs the full test suite on Linux, macOS (arm64) and Windows — with
perft and bench gates on Linux and macOS and the coverage gate on Linux — and keeps self-contained engine and GUI binaries for all
three platforms as a day's artifacts.

## Versions and releases

The version is `MAJOR.MINOR.BUILD`, from git alone, the rule all of reckless-technology's projects follow:

- `MAJOR.MINOR` is the nearest annotated tag `vMAJOR.MINOR` reachable from the commit built (a *version mark*, made by
  hand on `main`);
- `BUILD` is the number of commits since that tag, the tagged commit itself being 0.

`Directory.Build.targets` works it out at build time (`git describe --tags --match 'v*.*' --exclude 'v*.*.*'`) and
stamps every assembly with it. The engine reports it (`id name Stoopid Chess 3.0.0`), and so does the GUI: in its
Help box, its `--version`, the Windows .exe's version resource and the macOS app's Info.plist. Nothing
in the tree holds a version to edit. A tree with no mark reachable (a source tarball, a shallow clone, a fork without
the tags) builds as `0.0.0`, with a warning.

`.github/workflows/release.yml` makes a release:

- **Pushing a mark** (`git tag -a v3.1 -m "Stoopid Chess 3.1" && git push origin v3.1`) releases `3.1.0`.
- **Running it by hand** on `main` (`gh workflow run release.yml --ref main`) releases `main`'s head as
  `MAJOR.MINOR.BUILD`. `-f dry_run=true`, from any branch, builds, tests and signs the macOS files but publishes
  nothing.

It refuses a mark that isn't `vMAJOR.MINOR`, a commit not on `main`, and a version already released. Each platform is
built from nothing on the organisation's self-hosted runners, with the suite, perft and the bench signature. Each file
shipped must report the version, and each GUI is started before it ships. The workflow creates the release
`vMAJOR.MINOR.BUILD` and its tag itself. It never runs for a pull request.

| File | What it is |
|---|---|
| `stoopid-chess-<version>-windows-x86_64.exe`, `stoopid-chess-gui-<version>-windows-x86_64.exe` | the engine and the Avalonia GUI (each one self-contained file), Authenticode-signed through SSL.com eSigner (Jonathan Reckless); up to 3.0.0 the Windows GUI was the Windows Forms one |
| `stoopid-chess-<version>-macos-arm64.zip`, `stoopid-chess-gui-<version>-macos-arm64.zip` | the engine and `Stoopid Chess.app` (the Avalonia GUI) for Apple silicon, signed with a Developer ID (hardened runtime) and notarised by Apple; the app's ticket is stapled |
| `stoopid-chess-<version>-linux-x86_64`, `stoopid-chess-gui-<version>-linux-x86_64` | the engine and the Avalonia GUI for Linux, each one self-contained file |
| `stoopid-chess-<version>-source.tar.gz` | the source at the commit |

The macOS GUI is a proper app bundle, so it opens from Finder; its native libraries sit beside its executable, and
`packaging/macos/` holds its Info.plist, its icon and the one entitlement .NET's JIT needs under the hardened runtime.
The Linux and Windows GUIs carry their native libraries inside their single files. The Windows runner has no
interactive desktop, so the Windows GUI is started there by its own `--smoke`: it opens its main window as usual, says
so, and quits itself ten seconds later with exit code 0. Before 3.0 the macOS and Linux GUIs shipped without those
libraries and did not start. A bare engine binary can't carry a stapled notarisation ticket, so Gatekeeper looks the
macOS engine's up online. The signing runs from `tools/sign-macos.sh` and `tools/sign-windows.ps1`, with the
organisation's signing secrets (the same ones as its other apps). The Windows files are signed only in a run that
publishes, since every signing spends the eSigner account's allowance. A publishing run that would ship an unsigned
file fails.

## Playing against other engines

The engine executable works in any UCI GUI (Arena, Cute Chess, Banksia) and with tournament tools
such as `cutechess-cli` or `fastchess`, which is how it can be measured against pawnstar or zenith.
In the Stoopid Chess GUI, *Engine > Choose UCI engine...* swaps the built-in engine for an external one.

## Saved games

`*.chess` files are JSON: the starting position as FEN, the moves in UCI notation, and the game
settings. Files from before September 2026 used .NET binary serialisation and cannot be loaded.

## History

This project lived in the `StoopidChess` directory of the `reckless-technology/software` repository
until September 2026, when it was split out with its history. `docs/CODE_REVIEW.md` records the
review that preceded the restructuring and `docs/ARCHITECTURE_REVIEW.md` the proposals that followed it.

## License

Stoopid Chess is free software, licensed under the **GNU General Public License, version 3** (GPLv3). You may
redistribute and/or modify it under the terms of that license; it comes with **no warranty**. See the
[LICENSE](LICENSE) file for the full text, or <https://www.gnu.org/licenses/gpl-3.0.html>.

The chess piece images, in the GUI and in the application icon, are the "cburnett" set by
[Colin M.L. Burnett](https://en.wikipedia.org/wiki/User:Cburnett), used under the GNU General Public
License, version 2 or later. They were rendered to 64 px PNGs from the SVGs that
[Lichess](https://github.com/lichess-org/lila/tree/7370914942f30f1a77321ff6e5bc0ad3e4dd5841/public/piece/cburnett)
ships. The application icon is the black queen, `packaging/icon/BlackQueen.svg` (that SVG, unchanged), on a rounded
square of the Lichess board's light square colour, laid out on Apple's icon grid; `tools/make-icons.py` renders it to
the Windows .ico (16 to 256 px), the window's PNG and the macOS app's .icns (16 to 1024 px).
