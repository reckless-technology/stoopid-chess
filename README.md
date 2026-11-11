# Stoopid Chess

[![CI](https://github.com/reckless-technology/stoopid-chess/actions/workflows/ci.yml/badge.svg)](https://github.com/reckless-technology/stoopid-chess/actions/workflows/ci.yml)

A chess engine and a cross-platform chess GUI in C#. The engine speaks
[UCI](https://www.chessprogramming.org/UCI), so it can be used from any chess GUI and tested against other engines;
the GUI can play against any UCI engine, with the built-in one as the default. Originally written in 2009 as a Windows
Forms program, ported to .NET 10 and restructured in 2026. The source is meant to be read: it aims to be as simple, clean
and canonical as it can be, so that it is useful to someone learning chess programming, and strength comes second.

## Start here

Three commands, from a clone, with only the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed:

```bash
dotnet test StoopidChess.slnx -c Release                           # the test suite, a few seconds
dotnet run --project src/StoopidChess.Uci -c Release -- bench      # the engine searches a fixed set of positions
dotnet run --project src/StoopidChess.Desktop                      # play it
```

Then read the code in this order, each file standing on the one before ([docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) is the
guided tour, with the life of a move from click to reply):

1. `Core/Square.cs`, `Bitboard.cs`, `Move.cs`: the value types everything is written in.
2. `Core/Position.cs` with `Attacks.cs`: the board, make and unmake; then `Core/Position.MoveGeneration.cs`: legal move generation
   ([docs/BITBOARDS.md](docs/BITBOARDS.md)); then `Game.cs`.
3. `Engine/Evaluator.cs` ([docs/EVALUATION.md](docs/EVALUATION.md)) and `Engine/Searcher.cs`.
4. `Engine/Uci/UciSession.cs`: the engine's side of UCI.
5. `Play/UciEngineClient.cs`: the GUI's side of UCI, and `Play/GameController.cs`: how a GUI holds an engine.
6. `Desktop/`: the window. It is GUI plumbing, not chess: skip it on a first read.

Nothing in `tools/` is needed to build, test or play; it holds the setup, match, signing and icon scripts.

## Layout

| Project | Purpose |
|---|---|
| `src/StoopidChess.Core` | The rules of chess: the `Square`, `Bitboard`, `Move` and `MoveList` value types (a colored piece is a `(Color, Piece)` pair), `Position` (bitboards plus a square array, with incremental Zobrist keys), `Attacks` (attack tables and magic bitboards), FEN, legal move generation, `Game` (move history and result detection), perft. SAN, PGN and board setup are in Play. Cross-platform, no dependencies. |
| `src/StoopidChess.Engine` | The engine: `Evaluator`, `TranspositionTable`, `Searcher` (iterative deepening alpha-beta with quiescence), `SearchLimits`, and `UciSession` (the engine side of the UCI protocol). Cross-platform. |
| `src/StoopidChess.Play` | `GameController` and `UciEngineClient` (the GUI side of UCI: drives any engine, in-process or external): the state machine behind a chess GUI (game, the one engine it plays against, thinking, editing, chess clocks, save/load) with no UI, so it is tested on Linux against the in-process engine. Also `Level`, `SavedGame`, `BoardSetup` (a board being edited), and SAN and PGN. |
| `src/StoopidChess.Uci` | Console program `stoopid-chess`: the engine as a UCI executable, plus `bench` and `perft` subcommands. |
| `src/StoopidChess.Desktop` | The GUI for Linux, macOS and Windows, drawn with SkiaSharp in an SDL3 window: drawing, mouse, keyboard and menus over `GameController`, talking to the engine only through UCI. It plays the built-in engine, or any UCI engine opened by its path; running matches between engines is what Amphitheater is for. |
| `tests/StoopidChess.Tests` | xunit tests of the chess itself: Core, Engine and Uci. It builds and runs without the graphics stack. |
| `tests/StoopidChess.App.Tests` | xunit tests of the application half: Play (`GameController`, the UCI client) and the GUI, which are drawn to bitmaps and driven by synthesized events. |

Everything builds and runs on Linux, macOS and Windows.

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

**Setting up a machine.** `tools/setup-dev.ps1` (Windows) and `tools/setup-dev.sh` (Linux, macOS) install what
building, testing and debugging need - the .NET SDK of `Directory.Build.props`' target framework (per-user, through
Microsoft's dotnet-install script, when no such SDK is on the PATH), git, on Linux the libraries .NET and the GUI load -
plus the git hooks and the recommended VS Code extensions, checking each first, so they are safe to run again;
`-Check` / `--check` only reports. `-Tuning` / `--tuning` adds what `tools/match.cs` plays with, under
`~/tools`: fastchess and the reference pawnstar, the release binaries `tools/tuning-tools.txt` pins by version and
SHA-256 (pawnstar has none for Intel Macs, Linux on arm64 or Windows on arm64), and a first self-play baseline. In VS
Code they are the `setup environment`, `setup environment + tuning tools` and `check environment` tasks.

```bash
dotnet build StoopidChess.slnx -c Release          # everything (any OS)
dotnet build src/StoopidChess.Uci -c Release       # engine only (any OS)
dotnet test StoopidChess.slnx -c Release                        # the test suite, both projects
dotnet test tests/StoopidChess.Tests -c Release    # only the chess tests (Core, Engine, Uci): no SkiaSharp or SDL3 needed
dotnet test StoopidChess.slnx -c Release --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults/coverage
dotnet run tools/coverage-gate.cs -- TestResults/coverage --uncovered   # line coverage per assembly, the 90% gate
dotnet run --project src/StoopidChess.Desktop      # play (any OS)
dotnet run --project src/StoopidChess.Uci -c Release -- bench      # search signature and speed
dotnet run --project src/StoopidChess.Uci -c Release -- perft      # move generation check
```

Warnings are errors and nullable reference types are on throughout. `.vscode/` has tasks for all of
the above and launch configurations for debugging the GUI and the engine.

## The engine

- Bitboard board representation (a set of squares per piece type and per color, plus a square
  array), magic bitboards for the sliding pieces, their magic numbers found by a seeded search at
  start-up, and a fully legal move generator that resolves pins, checks, en passant and castling
  during generation rather than by make/test/unmake. Modelled on pawnstar; the design and
  measurements are in `docs/BITBOARDS.md`.
- Squares, bitboards and move lists are small structs (`Square`, `Bitboard`, `MoveList`) rather
  than raw integers and arrays, at no run-time cost: the bench signature and speed are unchanged
  by them.
- Zobrist keys for the position and for the pawn structure, generated from fixed seeds so every run
  is reproducible; 16-byte transposition entries verified by the full 64-bit key.
- Search: negamax alpha-beta (principal variation search) with iterative deepening, aspiration
  windows, and time management that gives a move half as much time again when its score drops or
  its best move changes; a two-tier transposition table (depth-preferred and always-replace), probed
  in the quiescence search too; mate distance pruning, internal iterative reduction, reverse
  futility pruning, razoring, null-move pruning, late move pruning and late move reductions; moves
  ordered by the table move, MVV/LVA for captures, two killer moves, a countermove, and history and
  one-ply continuation history for quiet moves; a check extension at the horizon; and a quiescence
  search with stand-pat that skips captures losing material by static exchange evaluation.
  `Engine/Searcher.cs` opens with a numbered map of these in the order it applies them, and
  `docs/SEARCH.md` records what each one measured in self-play, with the ones that were tried and
  dropped.
- Evaluation: the Simplified Evaluation Function (material and piece-square tables, with a separate
  king table in the endgame) as a baseline, plus whatever terms self-play has since justified;
  `docs/EVALUATION.md` is the log of every term tried, kept or not.
- UCI: `uci`, `isready`, `setoption` (see UCI options below), `ucinewgame`, `position`, `go` (depth, nodes,
  movetime, wtime/btime/winc/binc/movestogo, infinite), `stop`, `quit`; plus `d`, `eval`,
  `perft N` and `bench [depth]`.

The search is deterministic, so the `bench` node count is a signature of the search and evaluation.
Current values: depth 5 = 42,533 nodes, depth 6 = 79,688 nodes. A test asserts the depth-5
figure; update it (and CLAUDE.md) whenever a change to search or evaluation is intended.

## UCI options

The engine advertises one option in answer to `uci`. A value outside its range is clamped to it, and
an unknown option is ignored.

| Option | Default | Min | Max | What it does |
|---|---|---|---|---|
| `Hash` | 64 | 1 | 1024 | Transposition table size in megabytes |

The evaluation's weights are constants in `Evaluator.cs`, not options; `docs/EVALUATION.md` describes every
term and how its value was found.

## Testing

Modelled on the sibling engines pawnstar and trebuchet:

- **Perft** against the 128 standard positions (Ethereal's `standard.epd`, embedded in Core) is the
  move generation gate. The unit tests run each position to the deepest depth under 300k nodes;
  `stoopid-chess perft [max million nodes]` runs the full set (about 120 million nodes per second: 122 million with `perft 3` on an i9-13900HX, Windows, 2026-11-09).
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
  reply, an engine dying mid-game, an engine that will not start), the clocks and time controls, and saved games.
- **GUI**, with no window: every view is drawn to a bitmap and driven by synthesized pointer, wheel, key and text
  events - the widgets (plates, menus, lists, the dialog frame), the board drawn in every state and its drags
  and right clicks turned into squares, the capture bin, each dialog's OK and Cancel, and the main view's
  wiring (its menus, shortcuts, files, clipboard and engines) against a fake window system and scripted
  engines - and the window's text (`GameText`: the move list, the status bar, the analysis lines, the PGN names and
  result).
- **Bench signature**.

**Coverage.** CI fails the Linux job when the line coverage of the product code falls under 90%
(`tools/coverage-gate.cs` holds the threshold, the one place it lives; currently about 97%). The gated code is
Core, Engine, Play, the Uci console and the Desktop GUI. Left out (`tests/coverage.runsettings`): the test
assembly; the GUI's SDL host (`src/StoopidChess.Desktop/Sdl/`: the windows, the event loop, presenting, the native
file dialogs, the clipboard), which only calls SDL and needs a display, while every view and the main view's wiring
are gated; and the code generated into `obj/`. The two commands under "Build and test" measure it locally; `--uncovered`
lists every line no test runs.

CI (`.github/workflows/ci.yml`) runs on GitHub-hosted runners, each image pinned by version (`ubuntu-24.04`,
`macos-15` on Apple silicon, `windows-2025`). It runs the full test suite on Linux, macOS (arm64) and Windows — with
perft and bench gates on Linux and macOS and the coverage gate on Linux — and keeps self-contained engine and GUI binaries for all
three platforms as a day's artifacts.

## Versions and releases

The version is `MAJOR.MINOR.BUILD`, from git alone, the rule all of reckless-technology's projects follow:

- `MAJOR.MINOR` is the nearest annotated tag `vMAJOR.MINOR` reachable from the commit built (a *version mark*, made by
  hand on `main`);
- `BUILD` is the number of commits since that tag, the tagged commit itself being 0.

`Directory.Build.targets` works it out at build time (`git describe --tags --match 'v*.*' --exclude 'v*.*.*'`) and
stamps every assembly with it. The engine reports it (`id name Stoopid Chess 5.0.0`), and so does the GUI: in its
Help box, its `--version`, the Windows .exe's version resource and the macOS app's Info.plist. Nothing
in the tree holds a version to edit. A tree with no mark reachable (a source tarball, a shallow clone, a fork without
the tags) builds as `0.0.0`, with a warning.

`.github/workflows/release.yml` makes a release:

- **Pushing a mark** (`git tag -a v3.1 -m "Stoopid Chess 3.1" && git push origin v3.1`) releases `3.1.0`.
- **Running it by hand** on `main` (`gh workflow run release.yml --ref main`) releases `main`'s head as
  `MAJOR.MINOR.BUILD`. `-f dry_run=true`, from any branch, builds, tests and signs the macOS files but publishes
  nothing.

It refuses a mark that isn't `vMAJOR.MINOR`, a commit not on `main`, and a version already released. Each platform is
built from nothing on a fresh GitHub-hosted runner (`ubuntu-24.04`, `windows-2025`, `macos-15`), with the suite, perft
and the bench signature, as CI builds every push and pull request on the same images. Each file
shipped must report the version, and each GUI is started before it ships. The workflow creates the release
`vMAJOR.MINOR.BUILD` and its tag itself. It never runs for a pull request.

| File | What it is |
|---|---|
| `stoopid-chess-<version>-windows-x86_64.exe`, `stoopid-chess-gui-<version>-windows-x86_64.exe` | the engine and the GUI (each one self-contained file), Authenticode-signed through SSL.com eSigner (Jonathan Reckless); up to 3.0.0 the Windows GUI was the Windows Forms one |
| `stoopid-chess-<version>-macos-arm64.zip`, `stoopid-chess-gui-<version>-macos-arm64.zip` | the engine and `Stoopid Chess.app` (the GUI) for Apple silicon, signed with a Developer ID (hardened runtime) and notarised by Apple; the app's ticket is stapled |
| `stoopid-chess-<version>-linux-x86_64`, `stoopid-chess-gui-<version>-linux-x86_64` | the engine and the GUI for Linux, each one self-contained file |

**No source archive is published**, as in the sibling engines pawnstar and trebuchet. The source is this repository:
clone or fork it. (GitHub attaches its own automatic "Source code" archives to every release, which are its doing and
not the workflow's.)

The macOS GUI is a proper app bundle, so it opens from Finder; its native libraries (`libSkiaSharp.dylib`,
`libSDL3.dylib`) sit beside its executable, each signed with it, and
`packaging/macos/` holds its Info.plist, its icon and the one entitlement .NET's JIT needs under the hardened runtime.
The Linux and Windows GUIs carry their native libraries inside their single files. No one watches the Windows
runner's screen, so the Windows GUI is started there by its own `--smoke`: it opens its main window as usual, says
so, and quits itself ten seconds later with exit code 0. Before 3.0 the macOS and Linux GUIs shipped without those
libraries and did not start. A bare engine binary can't carry a stapled notarisation ticket, so Gatekeeper looks the
macOS engine's up online. The signing runs from `tools/sign-macos.sh` and `tools/sign-windows.ps1`, with the
organisation's signing secrets (the same ones as its other apps). The Windows files are signed only in a run that
publishes, since every signing spends the eSigner account's allowance. A publishing run that would ship an unsigned
file fails.

## The GUI

The board with the captured pieces under it, the move list (SAN) with *Move now!* under it at the right, the drawn
menu bar across the top and the status bar along the foot; the Analysis window (*View > Analysis window*) is a window
of its own. It is drawn in the Reckless series' look, as Reckless Amphitheater wears it: charcoal, cream ink and an
amber accent, the light and dark squares of its board, the toolkit's legend plates for buttons and choices, Noto
Serif for the interface and Monaspace Xenon for the moves, the engine's lines and the clocks, and every dialog drawn
in the window over a veil.

You play White or Black against the engine (*Game > New Game*), can take a move back or switch sides, set up a
position by right-clicking a square, copy and paste FEN, save and load games, and import and export PGN. Every game
is played on countdown clocks, shown in the status bar, and a side whose clock reaches zero loses on time. *Game >
Time control* offers eight: 40/5 and 40/40 (40 moves in 5 or 40 minutes, the period repeating), 1+0, 3+2, 5+3, 10+0,
15+10 and 30+0, in minutes and seconds of increment; a game starts at 10+0. *Game > Set level* handicaps the engine's
thinking, not the clocks: both clocks run in real time, but the engine plans each move as though it had only part of
its clock - a hundredth at Easy, a tenth at Medium, a third at Hard and all of it at Best. Easy, where a game starts,
also searches no more than three plies deep.

- **Views** (`Views/`, `Look/`, and `MainView`, `BoardView`, `Dialogs/` beside them) draw on any `SKCanvas` and take
  the app's own pointer, wheel, key and text events, so the suite runs them with no window. The look is a C#
  translation of reckless-toolkit's drawing (`look/LookDrawing.cpp`, `app/MenuBarView.h`, `view/Menu.h`,
  `view/Dialog.h`; GPLv3, as this repository is), in Amphitheater's colours (`src/app/Theme.h`). The menu bar is drawn
  on every OS; its shortcuts use the platform's command key, Cmd on macOS and Ctrl elsewhere.
- **The SDL host** (`Sdl/`) opens the windows, pumps SDL's events into the views, and presents each frame: Skia draws
  into a raster bitmap at the window's physical pixels, which an SDL streaming texture shows - no GL context of the
  app's own, so the GUI runs on any display, a CI runner's included. It also does the native file dialogs (on Linux
  through the desktop portal, or zenity), the clipboard and the UI thread's synchronization context, which engine
  replies continue on.

## Playing against other engines

The engine executable works in any UCI GUI (Arena, Cute Chess, Banksia), and
[Reckless Amphitheater](https://github.com/reckless-technology/amphitheater-chess) carries it as one of its bundled
engines. It also works with tournament tools such as `cutechess-cli` or `fastchess`, which is how it can be measured
against pawnstar or trebuchet.

In the Stoopid Chess GUI, *Engine > Open UCI engine...* plays an external engine in place of the built-in one, and
*Engine > Use built-in engine* goes back. The GUI plays one engine at a time: matches between engines, a list of
engines and their options are Amphitheater's job, and were taken out of this GUI in 5.0.

## Saved games

`*.chess` files are JSON: the starting position as FEN, the moves in UCI notation, the side you play,
the level and whether the board is flipped; the time control is not saved. A file saved at the Beginner level,
which 5.0 removed, loads at Easy. Files from before September 2026 used .NET binary serialisation and cannot be
loaded.

## History

This project lived in the `StoopidChess` directory of the `reckless-technology/software` repository
until September 2026, when it was split out with its history. `docs/history/` keeps the review that preceded the
restructuring and the proposals that followed it; the names in them are out of date.

## License

Stoopid Chess is free software, licensed under the **GNU General Public License, version 3** (GPLv3). You may
redistribute and/or modify it under the terms of that license; it comes with **no warranty**. See the
[LICENSE](LICENSE) file for the full text, or <https://www.gnu.org/licenses/gpl-3.0.html>.

The GUI's fonts are embedded in it with their licences (`src/StoopidChess.Desktop/Assets/Fonts/`): Noto Serif,
Copyright 2022 The Noto Project Authors, and Monaspace Xenon, Copyright 2023 GitHub, both under the SIL Open Font
License, version 1.1, as reckless-toolkit carries them. It is built on SkiaSharp 4 (MIT; Skia itself under the BSD
licence) and on SDL3 (the zlib licence) through the ppy.SDL3-CS binding (MIT), whose native libraries ship inside it.

The chess piece images, in the GUI and in the application icon, are the "cburnett" set by
[Colin M.L. Burnett](https://en.wikipedia.org/wiki/User:Cburnett), used under the GNU General Public
License, version 2 or later. They were rendered to 64 px PNGs from the SVGs that
[Lichess](https://github.com/lichess-org/lila/tree/7370914942f30f1a77321ff6e5bc0ad3e4dd5841/public/piece/cburnett)
ships. The application icon is the black queen, `packaging/icon/BlackQueen.svg` (that SVG, unchanged), on a rounded
square of the Lichess board's light square colour, laid out on Apple's icon grid; `tools/make-icons.py` renders it to
the Windows .ico (16 to 256 px), the window's PNG and the macOS app's .icns (16 to 1024 px).
