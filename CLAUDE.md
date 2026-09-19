# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Stoopid Chess: a C# chess engine (`StoopidChess.Core` rules + `StoopidChess.Engine` search and UCI
protocol, wrapped by the `StoopidChess.Uci` console) and a Windows Forms GUI (`StoopidChess.Gui` over
the `StoopidChess.Play` controller) that plays against any UCI engine. .NET 10;
`Directory.Build.props` applies nullable, warnings-as-errors and the recommended analyzers to every
project. Read [README.md](README.md) for the architecture and
[docs/CODE_REVIEW.md](docs/CODE_REVIEW.md) for why the code is shaped the way it is,
[docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) for the prioritised list of proposed improvements,
and [docs/BITBOARDS.md](docs/BITBOARDS.md) for the bitboard board and legal move generator.

## Build / test / run

```bash
dotnet build StoopidChess.slnx -c Release                         # everything (Windows; the GUI targets net10.0-windows)
dotnet build src/StoopidChess.Uci -c Release                      # engine only, works on Linux/macOS
dotnet test tests/StoopidChess.Tests -c Release                   # the xunit suite, a few seconds
dotnet test tests/StoopidChess.Tests -c Release --filter "FullyQualifiedName~SearchTests"          # one class
dotnet test tests/StoopidChess.Tests -c Release --filter "Name=KeysAreMaintainedIncrementallyThroughRandomPlay"  # one test
dotnet test tests/StoopidChess.Tests -c Debug                     # with the Debug.Assert checks in Board and Searcher on
dotnet run --project src/StoopidChess.Uci -c Release -- bench 5   # deterministic node signature + nps (the test asserts depth 5; CI runs the default, 6)
dotnet run --project src/StoopidChess.Uci -c Release -- perft 3   # all 128 standard perft positions, depths up to 3M nodes
dotnet run --project src/StoopidChess.Gui                         # the GUI
```

`.vscode/tasks.json` wraps the same commands and `.vscode/launch.json` debugs the GUI and the engine.
CI (`.github/workflows/ci.yml`) runs engine build + tests + perft 3 + bench on Linux and the full
solution + single-file publishes on Windows.

## The testing discipline

Same as the sibling engines pawnstar and zenith. Every change must keep the tests green, and:

1. **Move generation is guarded by perft.** `tests/.../PerftTests.cs` runs every one of the 128
   standard positions (embedded as `src/StoopidChess.Core/Data/perft.epd`). Never weaken it. If a
   perft count changes, the move generator is wrong, not the reference. `MoveGeneratorTests` also
   cross-checks the legal generator against a naive reference generator and pins the en passant,
   pin and double-check special cases; `AttacksTests` checks the magic tables against ray walking
   and the `Bitboard` operations; `MoveListTests` covers the inline move list.
2. **The search is deterministic and has a signature.** `Bench.Run(5)` must equal
   `BenchTests.Depth5Signature` (86,351 nodes; depth 6 is 217,470). Any change to search
   ordering, pruning, evaluation, table size or Zobrist keys changes it. When the change is
   intended, run `stoopid-chess bench 5`, update the constant in `tests/.../BenchTests.cs` and the
   numbers in README.md, and say so in the commit message. Nondeterminism (e.g. time-based
   decisions inside a fixed-depth search) is a bug. `EvaluatorRegressionTests` pins the static
   evaluation of eighteen positions, so a refactoring of the evaluator can prove it changed nothing.
3. **Make/unmake must be exact.** `BoardTests.KeysAreMaintainedIncrementallyThroughRandomPlay`
   recomputes the keys after every random move. Anything that touches `Board`'s state must keep the
   incremental keys, the bitboards, the square array and the history consistent;
   `Board.ComputeKeys()` and `Board.IsConsistent()` are the oracles.
4. Strength changes should be checked by self-play or a match against another engine with
   `fastchess`: `tools/match.ps1 -Opponent <uci engine exe>` builds the engine and runs paired
   games from an openings file (defaults point at the copies under `C:\Users\jonny\tools`; both
   engines are deterministic, so openings are essential). `tools/selfplay.ps1` plays the current
   build against a snapshot in `C:\Users\jonny\tools\stoopid-baseline` (refresh it with
   `dotnet publish src/StoopidChess.Uci -c Release -o <that dir>` after each accepted change).
   Results so far, all 40-60 games at 10+0.1, single-threaded, no book: pawnstar 0.13.593 beat
   the original search 40-0 and the improved search 40-0; self-play gains were null move
   +199 ± 90, history heuristic +41 ± 67, LMR +101 ± 68, quiescence TT +41 ± 61, aspiration
   windows -35 ± 76 alone (not kept), PVS +58 ± 78, aspiration windows on top of PVS +64 ± 75 (kept),
   bitboards with a legal move generator (a pure speed change) +95 ± 70, reverse futility pruning
   +122 ± 50 (80 games), late move pruning at depth <= 4 after the (5 + depth^2)th quiet move +30 ± 61
   (80 games; pawnstar's 3 + depth^2 to depth 8 measured -22 ± 59 and was not kept), a logarithmic LMR
   table (0.75 + ln(depth) ln(move) / 2.25, one less in PV nodes) -22 ± 57 (not kept), futility pruning
   of quiet moves at depth <= 3 with a 100 cp per ply margin +4 ± 58 (not kept), delta pruning in
   quiescence with a 200 cp margin -4 ± 54 (not kept).

## Architecture notes

- **Dependencies point one way:** Core ← Engine ← Play ← Gui (Gui also references Core directly),
  and Uci ← Engine.
- **Core value types** (zero-cost wrappers, like pawnstar's C++ classes; the bench signature and
  nps prove it): `Square` is a one-byte readonly record struct, a1 = 0 .. h8 = 63 so a square is its
  bit number, with `Square.None` (index 64, `IsValid` false), `File`/`Rank`/`Name`, `Square.At`,
  `Square.Parse`, the `A1`..`H8` constants, `square + 8` arithmetic, an implicit conversion to `int`
  for table lookups and only an explicit one back (`new Square(i)`), so an `int` can never be
  mistaken for a square; `Square.Unchecked` is the internal no-validation constructor for hot
  paths. `Bitboard` is a readonly record struct over `ulong` with `& | ^ ~`, `Count`, `IsEmpty`,
  `IsSingle`, `First`, `Contains`, the shifts (`North()`, `NorthEast()`, `Forward(colour)`), the
  file/rank/light-square constants, `Bitboard.Of(square)` and `foreach (Square s in set)` (a struct
  enumerator the JIT inlines; `Bitboard.PopFirst(ref set)` is the explicit form). `MoveList` is a
  fixed-capacity (256) struct holding its moves inline (an `[InlineArray]`), so generation
  allocates nothing: pass it by `ref`, keep one per ply (`Searcher`) or per depth (`Perft`), order
  it through `AsSpan()`; it also implements `IEnumerable<Move>` for LINQ in tests. `Move` is a
  readonly record struct (32 bits, `From`/`To` are `Square`s). There is no coloured-piece type: the
  engine only ever needs `Colour` and `PieceType` separately, so where a caller wants both at once
  (`Board[square]`, `PositionBuilder[square]`, `GameController.PieceAt`/`EditSquare`) it is a
  `(Colour Colour, PieceType Type)` tuple whose default is an empty square, and
  `PieceType.ToChar(colour)` gives the FEN letter. No `int` squares or raw `ulong` sets appear in
  any public API, including Play and the GUI.
- **Core.** `Board` is a mutable position held as `Bitboard`s (per piece type and per colour plus
  the occupancy) and a 64-entry `PieceType` array (a piece's colour comes from the colour
  bitboards; make/unmake pass colour and type explicitly), with `MakeMove`/`UnmakeMove` (strict LIFO), Zobrist
  and pawn keys, side to move, castling rights (lost by any move from or to a king or rook home
  square, see `CastlingRightsExtensions.LostBySquare`), en passant square, clocks, and `Checkers`
  (the pieces giving check, computed lazily and cached until the next move). `Attacks` holds the
  knight/king/pawn tables, `Between`/`Line` and the magic bitboard slider tables (pawnstar's magic
  numbers; built at startup and verified). `MoveGenerator` is static, deterministic and generates
  only legal moves into a `ref MoveList` (pins, check masks, en passant and castling resolved
  during generation, following pawnstar); `GenerateLegalCaptures` serves quiescence. `Game` layers
  a legal-moves-only API, move history and `GameStatus` on top and is what the controller uses.
  `Fen` and `MoveNotation` do text; `PositionBuilder` edits positions.
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
  `src/StoopidChess.Uci/Program.cs` is a thin entry point that dispatches to the session, `bench` or
  `perft`.
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
- Formatting follows `.editorconfig` (Allman braces, braces on every block, `var` only when the type
  is apparent, LF line endings). **All code is formatted with `dotnet format StoopidChess.slnx`
  before it is committed.** The pre-commit hook in `.githooks/pre-commit` refuses a commit that
  `dotnet format --verify-no-changes` would change; enable it once per clone with
  `git config core.hooksPath .githooks`. VS Code formats C# files on save with the C# extension
  (`.vscode/settings.json`), organising imports too. The style rules the hook does not enforce (they
  are suggestions, not errors) are applied by an occasional `dotnet format StoopidChess.slnx
  --severity info` pass.
- Everything except `Gui` must build and test on Linux (the CI job enforces it): no Windows-only APIs.
- Commit messages describe the change and, for search/eval changes, the new bench signature.
