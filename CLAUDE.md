# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

Stoopid Chess: a C# chess engine (`StoopidChess.Core` rules + `StoopidChess.Engine` search, speaking
UCI through the `StoopidChess.Uci` console) and a Windows Forms GUI (`StoopidChess.Gui`) that plays
against any UCI engine. .NET 10, `Directory.Build.props` applies nullable, warnings-as-errors and the
recommended analyzers to every project. Read [README.md](README.md) for the architecture and
[docs/CODE_REVIEW.md](docs/CODE_REVIEW.md) for why the code is shaped the way it is, and
[docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) for the prioritised list of proposed improvements.

## Build / test / run

```bash
dotnet build StoopidChess.slnx -c Release                         # everything (Windows; the GUI targets net10.0-windows)
dotnet build src/StoopidChess.Uci -c Release                      # engine only, works on Linux/macOS
dotnet test tests/StoopidChess.Tests -c Release                   # ~290 xunit tests, a few seconds
dotnet run --project src/StoopidChess.Uci -c Release -- bench     # fixed-depth node signature + nps (depth 6 default)
dotnet run --project src/StoopidChess.Uci -c Release -- perft 3   # all 128 standard perft positions, depths up to 3M nodes
dotnet run --project src/StoopidChess.Gui                         # the GUI
```

`.vscode/tasks.json` wraps the same commands (build, build release, test, run gui, run uci, bench,
perft, publish, clean); `.vscode/launch.json` debugs the GUI and the engine.

## The testing discipline

Same as the sibling engines pawnstar and zenith. Every change must keep the tests green, and:

1. **Move generation is guarded by perft.** `tests/.../PerftTests.cs` runs every one of the 128
   standard positions (embedded as `src/StoopidChess.Core/Data/perft.epd`). Never weaken it. If a
   perft count changes, the move generator is wrong, not the reference.
2. **The search is deterministic and has a signature.** `Bench.Run(5)` must equal
   `BenchTests.Depth5Signature` (845,984 nodes; depth 6 is 3,056,834). Any change to search
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
   engines are deterministic, so openings are essential). First result, 2026-09-17, 40 games at
   10+0.1 against pawnstar 0.13.593 single-threaded without book: 0-40 (Stoopid Chess searches
   to depth 5-7 per move, pawnstar to 11-15).

## Architecture notes

- **Core has no engine knowledge.** `Board` is a mutable 0x88 position with `MakeMove`/`UnmakeMove`
  (strict LIFO), piece square lists, primary/secondary/pawn Zobrist keys, side to move, castling
  rights (lost by any move from or to a king or rook home square, see
  `CastlingRightsExtensions.LostBySquare`), en passant square and clocks. `Move` and `Piece` are
  readonly record structs. `MoveGenerator` is static and deterministic; legal move generation is
  pseudo-legal generation plus make/test/unmake. `Game` layers a legal-moves-only API, move history
  and `GameStatus` on top and is what the GUI uses. `Fen` and `MoveNotation` do text.
- **Engine.** `Searcher` owns per-search state (killers, per-ply move lists, statistics) and searches
  a clone of the caller's board. Mate scores are `±(MateScore - ply)` and are converted to
  node-relative form across the transposition table boundary. Draw score is 0. `SearchLimits`
  mirrors UCI `go`; `SearchLimits.TimeBudget` is the time manager. `TranspositionTable` is two
  tier and verifies entries with both keys. `Evaluator` is an instance (it owns the pawn hash).
- **UCI.** `UciSession` handles the protocol over any `TextReader`/`TextWriter`, running searches on
  a `Task` so `stop`/`isready` work while thinking; `UciEngineClient` is the GUI side, with
  `StartInProcess()` (anonymous pipes to a `UciSession` on a background thread) and
  `StartProcess(path)` for external engines. The GUI only ever uses the client.
- **GUI.** `MainForm.Designer.cs` is Windows Forms designer output (analyzers are off for
  `*.Designer.cs` via `.editorconfig`); event handler names there must stay in sync with
  `MainForm.cs`. Non-designer UI (engine menu, promotion dialog) is built in code. Save games are
  JSON with FEN + UCI moves (`SavedGame`).

## Conventions

- .NET naming and idioms: PascalCase members, file-scoped namespaces, records for data, `ArgumentNullException.ThrowIfNull`
  on public entry points, `CancellationToken` last. No static mutable state; counters live in
  `SearchStatistics`.
- Keep `Core` and `Engine` free of Windows-only APIs so the Linux CI job keeps passing.
- Commit messages describe the change and, for search/eval changes, the new bench signature.
