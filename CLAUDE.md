# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Stoopid Chess: a C# chess engine (`StoopidChess.Core` rules + `StoopidChess.Engine` search and UCI
protocol, wrapped by the `StoopidChess.Uci` console) and a Windows Forms GUI (`StoopidChess.Gui`) that
plays against any UCI engine through the cross-platform `StoopidChess.Play` controller. .NET 10;
`Directory.Build.props` applies nullable, warnings-as-errors and the recommended analyzers to every
project. Read [README.md](README.md) for the architecture and
[docs/CODE_REVIEW.md](docs/CODE_REVIEW.md) for why the code is shaped the way it is, and
[docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) for the prioritised list of proposed improvements.

## Build / test / run

```bash
dotnet build StoopidChess.slnx -c Release                         # everything (Windows; the GUI targets net10.0-windows)
dotnet build src/StoopidChess.Uci -c Release                      # engine only, works on Linux/macOS
dotnet test tests/StoopidChess.Tests -c Release                   # ~312 xunit tests, a few seconds
dotnet test tests/StoopidChess.Tests -c Release --filter "FullyQualifiedName~SearchTests"          # one class
dotnet test tests/StoopidChess.Tests -c Release --filter "Name=KeysAreMaintainedIncrementallyThroughRandomPlay"  # one test
dotnet test tests/StoopidChess.Tests -c Debug                     # same tests with Debug.Assert enabled (slower)
dotnet run --project src/StoopidChess.Uci -c Release -- bench     # fixed-depth node signature + nps (depth 6 default)
dotnet run --project src/StoopidChess.Uci -c Release -- bench 5   # the depth the test asserts
dotnet run --project src/StoopidChess.Uci -c Release -- perft 3   # all 128 standard perft positions, depths up to 3M nodes
dotnet run --project src/StoopidChess.Gui                         # the GUI
```

Tests live in `tests/StoopidChess.Tests/{Core,Engine,Play}`, mirroring the projects. Release is the
configuration that matters for search work (bench timings, matches); Debug turns on the
`Debug.Assert` checks in `Board` and `Searcher`. `.vscode/tasks.json` wraps the same commands (build,
build release, test, test debug, match vs pawnstar, run gui, run uci, bench, perft, publish, clean);
`.vscode/launch.json` debugs the GUI and the engine. CI (`.github/workflows/ci.yml`) runs engine
build + tests + perft 3 + bench on Linux and the full solution + single-file publishes on Windows.

`config.json` at the repository root is fastchess's autosaved tournament config from the last
self-play run, not project configuration; leave it alone.

## The testing discipline

Same as the sibling engines pawnstar and zenith. Every change must keep the tests green, and:

1. **Move generation is guarded by perft.** `tests/.../PerftTests.cs` runs every one of the 128
   standard positions (embedded as `src/StoopidChess.Core/Data/perft.epd`). Never weaken it. If a
   perft count changes, the move generator is wrong, not the reference.
2. **The search is deterministic and has a signature.** `Bench.Run(5)` must equal
   `BenchTests.Depth5Signature` (241,437 nodes; depth 6 is 579,880). Any change to search
   ordering, pruning, evaluation, table size or Zobrist keys changes it. When the change is
   intended, run `stoopid-chess bench 5`, update the constant in `tests/.../BenchTests.cs` and the
   numbers in README.md, and say so in the commit message. Nondeterminism (e.g. time-based
   decisions inside a fixed-depth search) is a bug.
3. **Make/unmake must be exact.** `BoardTests.KeysAreMaintainedIncrementallyThroughRandomPlay`
   recomputes the keys after every random move. Anything that touches `Board`'s state must keep the
   incremental keys, piece lists and history consistent; `Board.ComputeKeys()` is the oracle.
4. Strength changes should be checked by self-play or a match against another engine with
   `fastchess`: `tools/match.ps1 -Opponent <uci engine exe>` builds the engine and runs paired
   games from an openings file (defaults point at the copies under `C:\Users\jonny\tools`; both
   engines are deterministic, so openings are essential). `tools/selfplay.ps1` plays the current
   build against a snapshot in `C:\Users\jonny\tools\stoopid-baseline` (refresh it with
   `dotnet publish src/StoopidChess.Uci -c Release -o <that dir>` after each accepted change).
   Results so far, all 40-60 games at 10+0.1, single-threaded, no book: pawnstar 0.13.593 beat
   the original search 40-0 and the improved search 40-0; self-play gains were null move
   +199 ± 90, history heuristic +41 ± 67, LMR +101 ± 68, quiescence TT +41 ± 61, aspiration
   windows -35 ± 76 alone (not kept), PVS +58 ± 78, aspiration windows on top of PVS +64 ± 75 (kept).

## Architecture notes

- **Dependencies point one way:** Core ← Engine ← Play ← Gui, and Uci ← Engine. Core has no engine
  knowledge; Core, Engine, Play and Uci have no Windows-only APIs so the Linux CI job keeps passing.
- **Core.** `Board` is a mutable 0x88 position with `MakeMove`/`UnmakeMove` (strict LIFO), piece
  square lists, Zobrist and pawn keys, side to move, castling rights (lost by any move from or to a
  king or rook home square, see `CastlingRightsExtensions.LostBySquare`), en passant square and
  clocks. `Move` and `Piece` are readonly record structs (`Move` packs into 32 bits). `MoveGenerator`
  is static and deterministic; legal move generation is pseudo-legal generation plus
  make/test/unmake. `Game` layers a legal-moves-only API, move history and `GameStatus` on top and is
  what the controller uses. `Fen` and `MoveNotation` do text; `PositionBuilder` edits positions.
- **Engine.** `Searcher` owns per-search state (killers, history, per-ply move lists, statistics) and
  searches a clone of the caller's board. Mate scores are `±(MateScore - ply)` and are converted to
  node-relative form across the transposition table boundary. Draw score is 0. `SearchLimits`
  mirrors UCI `go`; `SearchLimits.TimeBudget` is the time manager. `TranspositionTable` is two tier
  with 16-byte entries verified by the full 64-bit key. `Evaluator` is an instance (it owns the pawn
  hash). `Bench` is the fixed position set behind the signature.
- **UCI lives in Engine, not in the Uci project.** `src/StoopidChess.Engine/Uci/UciSession` handles
  the protocol over any `TextReader`/`TextWriter`, running searches on a `Task` so `stop`/`isready`
  work while thinking; `UciEngineClient` is the GUI side, with `StartInProcess()` (anonymous pipes to
  a `UciSession` on a background thread) and `StartProcess(path)` for external engines. The GUI only
  ever uses the client, so the built-in and external engines share one code path.
  `src/StoopidChess.Uci/Program.cs` is an 80-line entry point that dispatches to the session, `bench`
  or `perft`.
- **Play.** `GameController` (cross-platform, `StoopidChess.Play`) owns the game, the engine client,
  whose turn it is, the thinking state and generation counter (stale engine replies are discarded),
  position editing via `PositionBuilder`, clocks and save/load, and raises `Changed`,
  `EngineFailed` and `EngineIllegalMove`. It is single-threaded: engine replies continue on the
  caller's `SynchronizationContext`; tests `await WaitUntilIdleAsync()` instead. `Level` maps
  difficulty to `SearchLimits`; `SavedGame` is JSON with FEN + UCI moves.
- **GUI.** `MainForm` is drawing, mouse and menus over the controller and redraws on `Changed`.
  `MainForm.Designer.cs` is Windows Forms designer output (analyzers are off for `*.Designer.cs`
  via `.editorconfig`); event handler names (`OnXxx`) there must stay in sync with `MainForm.cs`.
  The engine menu and promotion dialog are built in code.

## Conventions

- .NET naming and idioms: PascalCase members, file-scoped namespaces, records for data,
  `ArgumentNullException.ThrowIfNull` on public entry points, `CancellationToken` last, nullable
  returns rather than `Try`/`out` pairs. No static mutable state; counters live in `SearchStatistics`.
- `.editorconfig` style: Allman braces, braces on every block, `var` only when the type is apparent,
  no `this.` qualification, `System` usings first, 4-space indent (2 for json/yml/csproj/props), LF
  line endings.
- Keep `Core`, `Engine`, `Play` and `Uci` free of Windows-only APIs so the Linux CI job keeps passing.
- Commit messages describe the change and, for search/eval changes, the new bench signature.
