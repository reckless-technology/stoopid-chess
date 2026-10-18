# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Stoopid Chess: a C# chess engine (`StoopidChess.Core` rules + `StoopidChess.Engine` search and UCI
protocol, wrapped by the `StoopidChess.Uci` console) and a GUI over the `StoopidChess.Play`
controller that plays against any UCI engine: `StoopidChess.Desktop` (Avalonia, any OS; the Windows
Forms GUI it replaced was removed after 3.0.0). .NET 10;
`Directory.Build.props` applies nullable, warnings-as-errors and the recommended analyzers to every
project. Read [README.md](README.md) for the architecture and
[docs/CODE_REVIEW.md](docs/CODE_REVIEW.md) for why the code is shaped the way it is,
[docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) for the prioritised list of proposed improvements,
[docs/BITBOARDS.md](docs/BITBOARDS.md) for the bitboard board and legal move generator, and
[docs/EVALUATION.md](docs/EVALUATION.md) for the evaluation baseline and the log of terms tried.

## Build / test / run

**Setting up a machine.** `tools/setup-dev.ps1` (Windows) and `tools/setup-dev.sh` (Linux, macOS) install what
building, testing and debugging need - the .NET SDK of `Directory.Build.props`' target framework (per-user, through
Microsoft's dotnet-install script into `%LOCALAPPDATA%\Microsoft\dotnet` or `~/.dotnet`, when no such SDK is on the
PATH), git, on Linux the libraries .NET and the GUI load - plus the git hooks and the recommended VS Code extensions,
checking each first, so they are safe to run again; `-Check` / `--check` only reports, `-NoPersist` / `--no-persist`
leaves the user's PATH and profile alone. `-Tuning` / `--tuning` adds fastchess and the reference pawnstar under
`~/tools` (the release binaries `tools/tuning-tools.txt` pins by version and SHA-256) and a first self-play baseline.
In VS Code they are the `setup environment`, `setup environment + tuning tools` and `check environment` tasks.

```bash
dotnet build StoopidChess.slnx -c Release                         # everything, on any OS
dotnet build src/StoopidChess.Uci -c Release                      # engine only
dotnet test tests/StoopidChess.Tests -c Release                   # the xunit suite, a few seconds
dotnet test tests/StoopidChess.Tests -c Release --filter "FullyQualifiedName~SearchTests"          # one class
dotnet test tests/StoopidChess.Tests -c Release --filter "Name=KeysAreMaintainedIncrementallyThroughRandomPlay"  # one test
dotnet test tests/StoopidChess.Tests -c Debug                     # with the Debug.Assert checks in Position and Searcher on
dotnet run --project src/StoopidChess.Uci -c Release -- bench 5   # deterministic node signature + nps (the test asserts depth 5; CI runs the default, 6)
dotnet run --project src/StoopidChess.Uci -c Release -- perft 3   # all 128 standard perft positions, depths up to 3M nodes
dotnet run --project src/StoopidChess.Desktop                     # the Avalonia GUI (any OS)
```

`.vscode/tasks.json` wraps the same commands and `.vscode/launch.json` debugs the GUI and the engine.
CI (`.github/workflows/ci.yml`) runs the full test suite, perft 3 and bench on Linux, on the org's
Apple-silicon runner and (suite only) on Windows, and publishes self-contained engine + GUI binaries
for all three platforms as artifacts, kept 24 hours (`retention-days: 1`; the organisation's artifact
storage is one shared quota); none is released. `release.yml` makes a release from a pushed `vMAJOR.MINOR` tag (or by
hand on `main`): built and tested from nothing on all three platforms, the Avalonia GUI the download on each (Windows
too after 3.0.0, whose Windows GUI was the since-removed Windows Forms one), the Windows engine and GUI
eSigner-signed (publishing runs only - each signing spends the allowance), the macOS engine and `Stoopid Chess.app` Developer
ID-signed and notarised, every file checked to report the version and each GUI started, published as
`vMAJOR.MINOR.BUILD` with a source archive (README, "Versions and releases"). `gh workflow run release.yml --ref
<branch> -f dry_run=true` proves a change to it without publishing or spending a Windows signing.

## The testing discipline

Same as the sibling engines pawnstar and zenith. Every change must keep the tests green, and:

1. **Move generation is guarded by perft.** `tests/.../PerftTests.cs` runs every one of the 128
   standard positions (embedded as `src/StoopidChess.Core/Data/perft.epd`). Never weaken it. If a
   perft count changes, the move generator is wrong, not the reference. `MoveGenerationTests` also
   cross-checks the legal generator against a naive reference generator and pins the en passant,
   pin and double-check special cases; `AttacksTests` checks the magic tables against ray walking
   and the `Bitboard` operations; `MoveListTests` covers the inline move list.
2. **The search is deterministic and has a signature.** `Bench.Run(5)` must equal
   `BenchTests.Depth5Signature` (42,531 nodes; depth 6 is 79,698). Any change to search
   ordering, pruning, evaluation, table size or Zobrist keys changes it. When the change is
   intended, run `stoopid-chess bench 5`, update the constant in `tests/.../BenchTests.cs` and the
   numbers in README.md, and say so in the commit message. Nondeterminism (e.g. time-based
   decisions inside a fixed-depth search) is a bug. Speed is compared at bench depth 9 or more with the environment
   variable `DOTNET_TieredCompilation=0`: the sub-second default bench is dominated by JIT warm-up and can invert a
   real speed difference. `EvaluatorRegressionTests` pins the static
   evaluation of eighteen positions, so a refactoring of the evaluator can prove it changed nothing.
3. **Make/unmake must be exact.** `PositionTests.KeysAreMaintainedIncrementallyThroughRandomPlay`
   recomputes the keys after every random move. Anything that touches `Position`'s state must keep the
   incremental keys, the bitboards, the square array and the history consistent;
   `Position.ComputeKeys()` and `Position.IsConsistent()` are the oracles.
4. Strength changes should be checked by self-play or a match against another engine with
   `fastchess`: `tools/match.sh --opponent <uci engine>` (Linux/macOS) or
   `tools/match.ps1 -Opponent <uci engine exe>` (Windows) builds the engine and runs paired
   games from an openings file (the repo's `tools/openings-6ply.epd`, 200 six-ply positions;
   fastchess defaults to the build `tools/setup-dev.* --tuning` installs
   under `~/tools`, `%USERPROFILE%\tools` on Windows; both engines are deterministic, so openings are essential).
   `tools/selfplay.sh` / `tools/selfplay.ps1` plays the current build against a snapshot in
   `~/tools/stoopid-baseline` (`%USERPROFILE%\tools\stoopid-baseline`) (refresh it with
   `dotnet publish src/StoopidChess.Uci -c Release -o <that dir>` after each accepted change);
   its defaults are the standard test setting, 100 games at 12+0.12.
   Results so far, all 40-60 games at 10+0.1, single-threaded, no book: pawnstar 0.13.593 beat
   the original search 40-0 and the improved search 40-0; self-play gains were null move
   +199 ± 90, history heuristic +41 ± 67, LMR +101 ± 68, quiescence TT +41 ± 61, aspiration
   windows -35 ± 76 alone (not kept), PVS +58 ± 78, aspiration windows on top of PVS +64 ± 75 (kept),
   bitboards with a legal move generator (a pure speed change) +95 ± 70, reverse futility pruning
   +122 ± 50 (80 games), late move pruning at depth <= 4 after the (5 + depth^2)th quiet move +30 ± 61
   (80 games; pawnstar's 3 + depth^2 to depth 8 measured -22 ± 59 and was not kept), a logarithmic LMR
   table (0.75 + ln(depth) ln(move) / 2.25, one less in PV nodes) -22 ± 57 (not kept), futility pruning
   of quiet moves at depth <= 3 with a 100 cp per ply margin +4 ± 58 (not kept), delta pruning in
   quiescence with a 200 cp margin -4 ± 54 (not kept), countermove heuristic (the quiet reply that last refuted
   the opponent's move, ordered after the killers) +103 ± 65, one-ply continuation history added to the
   quiet-move history score +30 ± 56, the logarithmic LMR table retried on top of the new ordering +17 ± 63 (still
   not kept; retry after further ordering work), static exchange evaluation pruning of losing captures in
   quiescence +30 ± 55, ordering SEE-losing captures behind all quiet moves -30 ± 68 (not kept), main-search SEE
   pruning of captures losing more than 100 cp per ply at depth <= 6 +17 ± 59 (not kept), a hard time limit of
   three times the soft budget so that a started iteration can finish +13 ± 60 (not kept), a history malus for the
   quiet moves searched before a cutoff +13 ± 60 (not kept), null move reduction 3 + depth/4 instead of 2 + depth/6
   +45 ± 50 (100 games at 12+0.12, the test setting from here on), an improving heuristic (static evaluation above
   two plies ago) taking one ply off the reverse futility margin and halving the late move pruning count when not
   improving +10 ± 57 (not kept), the same on the reverse futility margin alone -67 ± 47 (not kept), internal
   iterative reduction (one ply less at depth >= 4 without a table move) +21 ± 44 then +24 ± 49, +23 ± 33 over the
   200 games (74 W, 61 L, 65 D; kept), a one-ply extension of every checking move -3 ± 47 (not kept), one ply less LMR for quiet moves with any
   cutoff history -17 ± 47 (not kept). Stopping the iterative deepening only on a mate proven within the searched depth, instead
   of on any mate score, -38 ± 54 then -24 ± 52: kept anyway, because in all 200 games the side that saw a mate won,
   so the rule could not have changed a result (noise), while fifty-move-rule draws, the one thing it can change,
   fell from 21 in 500 games to 7 in 200 (the keep-on-mechanism-evidence exception was reviewed and confirmed
   on 20 September 2026). Half as much time again, within 80% of the clock, the first time the root
   score falls 30 cp or more between iterations from depth 4 +78 ± 48 (45 W, 23 L, 32 D), the same extension when the
   best move changes from depth 6 +45 ± 46 (43 W, 30 L, 27 D), stopping at 30% of the budget instead of 50% once the
   best move has stood for four iterations +10 ± 50 (not kept), doubling the budget on instability instead of adding
   half 0 ± 47 (not kept), sharing the clock over 40 moves instead of 30 -60 ± 49 (not kept), over 20
   moves +14 ± 63 then +24 ± 46, +19 ± 38 over the 200 (75 W, 64 L, 61 D; kept), over 15 moves -3 ± 56 (not kept), a second extension on renewed
   instability -14 ± 58 (not kept), an aspiration window of 20 cp instead of 40 +14 ± 52 then +31 ± 53, +23 ± 37 over
   the 200 (80 W, 67 L, 53 D; kept), one more ply of null move reduction per 200 cp of static evaluation above beta,
   at most three, +3 ± 46 (not kept), an aspiration window of 12 cp -7 ± 58 (not kept), the countermove exempt from late
   move reductions and pruning like the killers +35 ± 50 (37 W, 27 L, 36 D), a two-ply follow-up history (our own previous
   move, the moved piece and its square) added to the quiet-move score 0 ± 52 (not kept), a reverse futility margin of
   60 cp per ply instead of 80 +38 ± 49 (40 W, 29 L, 31 D), 45 cp 0 ± 52 (not kept), late move pruning to depth 6
   instead of 4 -42 ± 58 (not kept), reverse futility pruning to depth 9 instead of 7 0 ± 49 (not kept), late move
   reductions from the third move instead of the fourth -38 ± 47 (not kept), from the fifth -7 ± 48 (not kept), null move
   pruning from depth 2 instead of 3 +45 ± 51 (39 W, 26 L, 35 D), late move reductions from depth 2 +14 ± 52 then +14 ± 50,
   +14 ± 36 over the 200 (not kept), ProbCut (a capture beating beta + 200 in a search four plies shallower cuts a
   non-PV node from depth 5) -24 ± 48 (not kept), starting another iteration until 60% of the budget is used instead
   of 50% 0 ± 41 (not kept), the whole increment in the budget instead of 80% 0 ± 44 (not kept), singular extensions
   (from depth 8, the table move searched a ply deeper when every other move fails 2 cp per ply below the table score
   at half depth; 53% more nodes at depth 10) +3 ± 50 (not kept), a capture history (cutoffs by piece, square and victim,
   an eighth of it added to the MVV/LVA order) -53 ± 50 (not kept), halving the history tables between root iterations
   -3 ± 45 (not kept), root moves ordered by the previous iteration's subtree node counts, the table move first,
   from the second iteration on -14 ± 52 (not kept), razoring (a non-PV node at depth <= 3 whose static evaluation
   is 250 cp per ply of depth below alpha is cut to the quiescence score when that score also stays at or below
   alpha) +31 ± 47 (36 W, 27 L, 37 D; kept), a razoring margin of 200 cp per ply -24 ± 56 (not kept), of
   300 cp per ply -24 ± 47 (not kept), a profile-guided speed pass (both transposition tiers in one array so a
   probe touches adjacent lines, aggressive inlining of Opponent and KingSquare, the killer/countermove/history
   tables as flat arrays instead of multi-dimensional ones, and a prefetch of the child's table bucket after
   every make) +8.6% nodes per second at bench 16 with tiering off, identical signature so the play is unchanged;
   kept on speed evidence like the evaluation cache. Quiet checking moves at the first ply of quiescence, skipped
   when they lose material by the exchange evaluation (all legal quiets generated and filtered by precomputed
   check squares and discovered-check candidates) -28 ± 57 (not kept: the extra generation at every quiescence
   entry cost more than the tactics it found). The table move searched before any move generation and trusted
   without a legality check (the full 64-bit key match makes a foreign move vanishingly unlikely, following
   pawnstar), so a table-move cutoff skips generation entirely: 8.5% fewer nodes and 10% less time to depth 16,
   and +45 ± 53 (37 W, 24 L, 39 D; kept). Indexing the slider attack table by pext instead of the magic
   multiply, with the array bounds checks removed by MemoryMarshal (identical play, parked on the slider-speed
   branch), measured no nps change at bench 16 or perft with tiering off — the dependent table load hides both
   the multiply and the predictably-taken bounds check (not kept). A
   100-game result between about +15 and +25 is neither kept nor rejected: a
   second 100 games are played and the change is judged on the 200. Evaluation experiments are logged in docs/EVALUATION.md;
   the reset to the Simplified Evaluation Function measured +13 ± 68 against the richer evaluation it replaced.

## Architecture notes

- **Dependencies point one way:** Core ← Engine ← Play ← Desktop (which also references Core
  directly), and Uci ← Engine.
- **Core value types** (zero-cost wrappers, like pawnstar's C++ classes; the bench signature and
  nps prove it): `Square` is a one-byte readonly record struct, a1 = 0 .. h8 = 63 so a square is its
  bit number, with `Square.None` (index 64, `IsValid` false), `File`/`Rank`/`Name`, `Square.At`,
  `Square.Parse`, the `A1`..`H8` constants, `square + 8` arithmetic, an implicit conversion to `int`
  for table lookups and only an explicit one back (`new Square(i)`), so an `int` can never be
  mistaken for a square; `Square.Unchecked` is the internal no-validation constructor for hot
  paths. `Bitboard` is a readonly record struct over `ulong` with `& | ^ ~`, `Count`, `IsEmpty`,
  `IsSingle`, `First`, `Contains`, the shifts (`North()`, `NorthEast()`, `Forward(color)`), the
  file/rank/light-square constants, `Bitboard.Of(square)` and `foreach (Square s in set)` (a struct
  enumerator the JIT inlines; `Bitboard.PopFirst(ref set)` is the explicit form). `MoveList` is a
  fixed-capacity (256) struct holding its moves inline (an `[InlineArray]`), so generation
  allocates nothing: pass it by `ref`, keep one per ply (`Searcher`) or per depth (`Perft`), order
  it through `AsSpan()`; it also implements `IEnumerable<Move>` for LINQ in tests. `Move` is a
  readonly record struct (32 bits, `From`/`To` are `Square`s). There is no colored-piece type: the
  engine only ever needs `Color` and `Piece` separately, so where a caller wants both at once
  (`Position[square]`, `Position.Place`, `GameController.PieceAt`/`EditSquare`) it is a
  `(Color Color, Piece Type)` tuple whose default is an empty square, and
  `Piece.ToChar(color)` gives the FEN letter. No `int` squares or raw `ulong` sets appear in
  any public API, including Play and the GUI.
- **Core.** `Position` is a mutable position held as `Bitboard`s (per piece type and per color plus
  the occupancy) and a 64-entry `Piece` array (a piece's color comes from the color
  bitboards; make/unmake pass color and type explicitly), with `MakeMove`/`UnmakeMove` (strict LIFO), Zobrist
  and pawn keys, side to move, castling rights (lost by any move from or to a king or rook home
  square, see `CastlingRightsExtensions.LostBySquare`), en passant square, clocks, and `Checkers`
  (the pieces giving check, computed lazily and cached until the next move). `Attacks` holds the
  knight/king/pawn tables, `Between`/`Line` and the magic bitboard slider tables (pawnstar's magic
  numbers; built at startup and verified). Move generation is part of `Position` (`GenerateLegalMoves`,
  `GenerateLegalCaptures` for quiescence, `IsLegal`): deterministic, and only legal moves into a `ref MoveList`
  (pins, check masks, en passant and castling resolved during generation, following pawnstar). Everything
  about a position, including make/unmake and the editing methods (`Empty`, `Setup`, `Place`, `Clear`,
  `SetSideToMove`, `SetCastlingRights`, usable only before a move is made), lives in the single file
  `Position.cs`. `Game` layers
  a legal-moves-only API, move history and `GameStatus` on top and is what the controller uses.
  `Fen` does FEN text; `Move.ParseUci` and `Move.ToLongAlgebraic` do move text.
- **Engine.** `Searcher` owns per-search state (killers, history, per-ply move lists, statistics) and
  searches a clone of the caller's board. Mate scores are `±(MateScore - ply)` and are converted to
  node-relative form across the transposition table boundary. Draw score is 0. `SearchLimits`
  mirrors UCI `go`; `SearchLimits.TimeBudget` is the time manager. `TranspositionTable` is two tier
  with 16-byte entries verified by the full 64-bit key. `Evaluator` reads its tunable values from an `EvaluationParameters` instance (31 named values defaulting to
  the constants in `Evaluator`; every one is a UCI spin option, and `tools/spsa.py` tunes them by SPSA over fastchess
  games); it rebuilds its material tables and empties its cache when they change. `Evaluator` is an instance and keeps a 512 KB evaluation cache (Zobrist key to score, exact, cleared by `ucinewgame`
  through `Searcher.NewGame`; about 10% faster; terms may keep further
  caches such as a pawn hash); it starts from the Simplified Evaluation Function, see docs/EVALUATION.md.
  `Bench` is the fixed position set behind the signature.
- **UCI lives in Engine, not in the Uci project.** `src/StoopidChess.Engine/Uci/UciSession` handles
  the protocol over any `TextReader`/`TextWriter`, running searches on a `Task` so `stop`/`isready`
  work while thinking; `UciEngineClient` is the GUI side, with `StartInProcess()` (anonymous pipes to
  a `UciSession` on a background thread) and `StartProcess(path)` for external engines. The GUI only
  ever uses the client, so the built-in and external engines share one code path.
  `src/StoopidChess.Uci/Program.cs` is a thin entry point that dispatches to the session, `bench` or
  `perft`.
- **Play.** `GameController` (cross-platform, `StoopidChess.Play`) owns the game, the engine client,
  whose turn it is, the thinking state and generation counter (stale engine replies are discarded),
  position editing via `Position.Setup` and `Place` (the `Editing` position until it is legal), clocks and save/load, and raises `Changed`,
  `EngineFailed` and `EngineIllegalMove`. It is single-threaded: engine replies continue on the
  caller's `SynchronizationContext`; tests `await WaitUntilIdleAsync()` instead. `Level` maps
  difficulty to `SearchLimits`; with a `TimeControl` set (`SetTimeControl`) the clocks count down
  instead, the mover gains the increment and, under a session control ("40 moves in 5 minutes",
  `TimeControl.Standard`), the base time again whenever it completes a session, the engine budgets
  from the clocks (wtime/btime/winc/binc, movestogo) rather than the level, a side reaching zero
  loses on time (`TimedOut`, `IsGameOver`), and a running game finishes under the clock it started
  with (`ActiveClock`).
  `StartEngineAsync(path, options)` sets each UCI option after the handshake;
  `StartMatchAsync(whitePath, blackPath, ...)` runs an engine match (white on the main engine,
  black on `OpponentName`'s; `NewGame(Color.None)` is a rematch, a human game or `Load` ends the
  match). `EngineRoster` is the JSON-persisted list of registered external engines with their
  options (the GUI chooses the file); `SavedGame` is JSON with FEN + UCI moves.
- **Desktop.** The Avalonia GUI, the only one: `MainWindow` (menus, status bar, dialogs and the
  wiring; it redraws on `Changed`), `BoardControl` and `CaptureBinControl` (rendering and pointer
  input), `PieceImages` (the PNGs in `Assets/`). Beyond the original Windows Forms GUI's feature set
  (that GUI, `StoopidChess.Gui`, was removed after 3.0.0) it is a small UCI host: registered engines
  (the Engine menu is built from `EngineRoster`, persisted at
  `ApplicationData/StoopidChess/engines.json`, engines added by picking the executable and named by
  their UCI reply), per-engine Hash/Threads options (`EngineOptionsDialog`, applied at engine
  start), time controls (a new game opens `NewGameDialog` to pick its clock — the standard 40-move
  sessions, the 5+5 incremental, or `CustomTimeControlDialog` with minutes, increment and moves per
  session; clock play with flagging) and engine matches (`MatchDialog`: any two registered engines
  or the built-in play each other on the clock). The move list is SAN, View > Analysis window shows
  the engine's info lines (depth, score, nodes, PV), File imports and exports PGN, and Ctrl+C /
  Ctrl+V copy and paste the position as FEN (pasting behaves like a finished board edit). The board
  draws coordinates, the checked king and the dragged piece's legal destinations; the common menu
  actions have keyboard shortcuts. The application icon (the Windows .ico, the window's PNG, the
  macOS app's .icns) is drawn by `tools/make-icons.py` from `packaging/icon/BlackQueen.svg`, on a
  rounded square of the board's light colour: run it again, and commit what it writes, when either
  changes. Keep the square's corners circular: macOS 26+ put a superellipse-cornered icon on their
  grey square.

## Conventions

- .NET naming and idioms: PascalCase members, file-scoped namespaces, records for data,
  `ArgumentNullException.ThrowIfNull` on public entry points, `CancellationToken` last, nullable
  returns rather than `Try`/`out` pairs. No static mutable state; counters live in `SearchStatistics`.
- American spelling in identifiers and comments: `Color`, not `Colour`.
- Formatting follows `.editorconfig` (Allman braces, braces on every block, `var` only when the type
  is apparent, LF line endings). **All code is formatted with `dotnet format StoopidChess.slnx`
  before it is committed.** The pre-commit hook in `.githooks/pre-commit` refuses a commit that
  `dotnet format --verify-no-changes` would change; enable it once per clone with
  `git config core.hooksPath .githooks`. VS Code formats C# files on save with the C# extension
  (`.vscode/settings.json`), organising imports too. The style rules the hook does not enforce (they
  are suggestions, not errors) are applied by an occasional `dotnet format StoopidChess.slnx
  --severity info` pass.
- Everything must build, test and run on Linux, macOS and Windows (CI enforces it): no Windows-only
  APIs outside an `OperatingSystem.IsWindows()` guard.
- **The evaluation stays hand-crafted.** No NNUE or other learned evaluation in this engine, ever; that is
  what pawnstar and zenith are for. Improve strength through hand-written terms, search and speed only.
- Commit messages describe the change and, for search/eval changes, the new bench signature.
- **Versioning** follows the organisation's rule (pawnstar, zenith, vst-plugins, ir-capture): the
  version is `MAJOR.MINOR.BUILD` from git alone - the nearest annotated `vMAJOR.MINOR` tag and the
  commits since it, stamped by Directory.Build.targets at build time; 0.0.0 with no such tag
  reachable. Nothing in the tree is edited to release: tag `main` `vMAJOR.MINOR` and push the tag,
  and the release workflow creates `vMAJOR.MINOR.BUILD` itself. CI checks out the full history and
  the tags so its builds carry the version. The Avalonia GUI answers `--version` without starting,
  and `--smoke` starts it, says when its main window opens and quits it ten seconds later with exit
  code 0: the release's check that the Windows GUI starts on a runner with no interactive desktop.
