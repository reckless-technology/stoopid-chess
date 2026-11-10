# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Stoopid Chess: a C# chess engine (`StoopidChess.Core` rules + `StoopidChess.Engine` search and UCI
protocol, wrapped by the `StoopidChess.Uci` console) and a GUI over the `StoopidChess.Play`
controller that plays against the built-in engine or any UCI engine: `StoopidChess.Desktop` (SkiaSharp in an SDL3
window, any OS). Its purpose is to be simple, clean and canonical enough that the source teaches chess programming;
strength is secondary. Engine matches and tournaments are Amphitheater's job, not this program's. .NET 10;
`Directory.Build.props` applies nullable, warnings-as-errors and the recommended analyzers to every project.
Read [README.md](README.md) first (it opens with a reading order), then
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the tour and how a click becomes a move,
[docs/BITBOARDS.md](docs/BITBOARDS.md) for the bitboard board and legal move generator, and
[docs/EVALUATION.md](docs/EVALUATION.md) for the evaluation baseline and the log of terms tried. The September 2026
reviews that shaped the restructuring are kept in [docs/history/](docs/history/); their names are out of date.

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
dotnet test tests/StoopidChess.Tests -c Release --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults/coverage
dotnet run tools/coverage-gate.cs -- TestResults/coverage --uncovered   # per-assembly line coverage, fails under 90%
dotnet run --project src/StoopidChess.Uci -c Release -- bench 5   # deterministic node signature + nps (the test asserts depth 5; CI runs the default, 6)
dotnet run --project src/StoopidChess.Uci -c Release -- perft 3   # all 128 standard perft positions, depths up to 3M nodes
dotnet run --project src/StoopidChess.Desktop                     # the GUI (any OS); -- --smoke, -- --version
```

`.vscode/tasks.json` wraps the same commands and `.vscode/launch.json` debugs the GUI and the engine.
CI (`.github/workflows/ci.yml`) runs on GitHub-hosted runners (free for a public repository), each image pinned by
version - `ubuntu-24.04`, `macos-15` (Apple silicon), `windows-2025` - with setup-dotnet's default install on each
fresh machine; so does `release.yml`. CI runs the full test suite, perft 3 and bench on Linux, on
macOS and (suite only) on Windows, the line-coverage gate on Linux, and publishes self-contained engine + GUI binaries
for all three platforms as artifacts, kept 24 hours (`retention-days: 1`; the organisation's artifact
storage is one shared quota); none is released. `release.yml` makes a release from a pushed `vMAJOR.MINOR` tag (or by
hand on `main`): built and tested from nothing on all three platforms, the GUI the download on each (up to 3.0.0
the Windows GUI was the since-removed Windows Forms one), its native libraries - libSkiaSharp and SDL3 - inside the
single file on Linux and Windows and beside the executable in the macOS app, the Windows engine and GUI
eSigner-signed (publishing runs only - each signing spends the allowance), the macOS engine and `Stoopid Chess.app` Developer
ID-signed and notarised, every file checked to report the version and each GUI started, published as
`vMAJOR.MINOR.BUILD`. No source archive is published - the source is the repository, to clone or fork, as in the
sibling engines (README, "Versions and releases"). `gh workflow run release.yml --ref
<branch> -f dry_run=true` proves a change to it without publishing or spending a Windows signing.

## The testing discipline

Same as the sibling engines pawnstar and trebuchet. Every change must keep the tests green, and:

1. **Move generation is guarded by perft.** `tests/.../PerftTests.cs` runs every one of the 128
   standard positions (embedded as `src/StoopidChess.Core/Data/perft.epd`). Never weaken it. If a
   perft count changes, the move generator is wrong, not the reference. `MoveGenerationTests` also
   cross-checks the legal generator against a naive reference generator and pins the en passant,
   pin and double-check special cases; `AttacksTests` checks the magic tables (found by a seeded search at start-up) against ray walking
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
4. **Line coverage of the product code stays at 90% or more**, gated in CI on Linux by
   `tools/coverage-gate.cs` (the one place the threshold lives; it also fails when a gated assembly is missing
   from the report). Gated: Core, Engine, Play, the Uci console and the Desktop GUI. Left out, in
   `tests/coverage.runsettings`: the test assembly, the GUI's SDL host (`src/StoopidChess.Desktop/Sdl/`: windows,
   event loop, presenting, file dialogs, clipboard, key codes) and the code generated into `obj/`. So no logic goes in the SDL host: it only calls SDL and the
   views. Every view is product code, tested without a window by drawing it to a bitmap and driving it with
   synthesized events (`ViewHarness`): a new widget, dialog or menu gets a test there (`WidgetTests`, `MenuTests`,
   `BoardViewTests`, `DialogTests`), and the main view's wiring is driven against `FakePlatform` and scripted engines
   (`MainViewTests`). The window's text is `GameText`.
   `GameController`'s failure paths are played against scripted fake engines (`ScriptedEngine`, through the
   internal `GameController.StartExternalEngine` and `UciEngineClient.Connect`).
5. Strength changes should be checked by self-play or a match against another engine with
   `fastchess`: `dotnet run tools/match.cs -- --opponent <uci engine>` (one file-based C# program for
   Windows, Linux and macOS) builds the engine and runs paired
   games from an openings file (the repo's `tools/openings-6ply.epd`, 200 six-ply positions;
   fastchess defaults to the build `tools/setup-dev.* --tuning` installs
   under `~/tools`, `%USERPROFILE%\tools` on Windows; both engines are deterministic, so openings are essential).
   `dotnet run tools/match.cs -- --baseline` plays the current build against a snapshot in
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

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) is the tour of the code and of how a click becomes a move. These are
only the things a change must not break.

- **Dependencies point one way:** Core <- Engine <- Play <- Desktop (which also references Core directly), and
  Uci -> Engine. The two halves of UCI are `Engine/Uci/UciSession` (engine side) and `Play/UciEngineClient` (GUI side).
- **Core's value types are zero-cost**, and the bench signature and nps prove it. `Square` is a one-byte struct, a1 = 0
  to h8 = 63, with an implicit conversion to `int` for table lookups and only an explicit one back, so an `int` is never
  mistaken for a square (`Square.Unchecked` is the internal no-validation constructor for hot paths). `MoveList` holds
  256 moves inline: pass it by `ref`, keep one per ply (`Searcher`) or per depth (`Perft`). No `int` squares or raw
  `ulong` sets appear in any public API, Play and the GUI included. There is no coloured-piece type; where a caller wants
  both, it is a `(Color Color, Piece Type)` tuple whose default is an empty square.
- **`Position`'s editing methods** (`Empty`, `Setup`, `Place`, `Clear`, `SetSideToMove`, `SetCastlingRights`) work only
  before a move is made. `Position` is one partial class in three files: `Position.cs` (the board, make/unmake),
  `Position.MoveGeneration.cs` (the legal generator) and `Position.Editing.cs`. `Checkers` is worked out once per move
  and kept in the undo record, so every mutator must leave it right.
- **`Searcher` searches a clone** of the caller's position. Mate scores are `±(MateScore - ply)` and are converted to
  node-relative form across the transposition table boundary; a draw scores 0. `Evaluator` is an instance with a 512 KB
  evaluation cache cleared by `ucinewgame`, and its weights are named constants (docs/EVALUATION.md).
- **`GameController` is single-threaded**: engine replies continue on the caller's `SynchronizationContext`. The tests
  drive it on a `ControllerThread`, a single-threaded context of their own, so a reply is handled between a test's steps
  and never beside them, and `await WaitUntilIdleAsync()` for the engine to finish. A reply is always handled after a
  yield, never inside the call that started the search (`ConfigureAwaitOptions.ForceYielding`): handled inline, it would
  start the next search before the first was stored, and the controller would wait on the wrong one (`InstantEngine` in
  the tests answers that fast every time). Stale replies are dropped by a generation counter.
- **Desktop** is drawn with SkiaSharp (4.154.0, pinned, with its Linux natives as the no-dependencies build: no
  fontconfig, only the C and C++ runtimes; paths are built with `SKPathBuilder`, since v4 makes `SKPath`'s own mutators
  obsolete) in SDL3 windows (ppy.SDL3-CS, natives for every desktop platform in the package), in the Reckless series' look
  as Amphitheater wears it: `Look/Theme` holds Amphitheater's `src/app/Theme.h` tokens, and `Look/Plates` is a C#
  translation of reckless-toolkit's `look/LookDrawing.cpp`. The fonts are embedded from `Assets/Fonts` with their OFL
  licences. `KeyModifiers.Primary` is Cmd on macOS and Ctrl elsewhere. `Sdl/` is the host and holds no logic: it only
  calls SDL and the views (Skia draws into a raster bitmap at the window's physical pixels, shown through an SDL streaming
  texture, with no GL context of the app's own).
- **The application icon** (the Windows .ico, the window's PNG, the macOS app's .icns) is drawn by `tools/make-icons.py`
  from `packaging/icon/BlackQueen.svg`, on a rounded square of the board's light colour: run it again, and commit what it
  writes, when either changes. Keep the square's corners circular: macOS 26+ put a superellipse-cornered icon on their
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
  what pawnstar and trebuchet are for. Improve strength through hand-written terms, search and speed only.
- Commit messages describe the change and, for search/eval changes, the new bench signature.
- **Versioning** follows the organisation's rule (pawnstar, trebuchet, vst-plugins, ir-capture): the
  version is `MAJOR.MINOR.BUILD` from git alone - the nearest annotated `vMAJOR.MINOR` tag and the
  commits since it, stamped by Directory.Build.targets at build time; 0.0.0 with no such tag
  reachable. Nothing in the tree is edited to release: tag `main` `vMAJOR.MINOR` and push the tag,
  and the release workflow creates `vMAJOR.MINOR.BUILD` itself. CI checks out the full history and
  the tags so its builds carry the version. The GUI answers `--version` without starting SDL,
  and `--smoke` starts it, says when its main window opens and quits it ten seconds later with exit
  code 0: the release's check that the Windows GUI starts on a runner whose screen no one watches.
