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
dotnet test StoopidChess.slnx -c Release                          # the whole xunit suite, a few seconds
dotnet test tests/StoopidChess.Tests -c Release                   # only the chess tests (Core, Engine, Uci), no graphics stack
dotnet test tests/StoopidChess.App.Tests -c Release               # only Play and the GUI
dotnet test StoopidChess.slnx -c Release --filter "FullyQualifiedName~SearchTests"          # one class
dotnet test StoopidChess.slnx -c Release --filter "Name=KeysAreMaintainedIncrementallyThroughRandomPlay"  # one test
dotnet test StoopidChess.slnx -c Debug                            # with the Debug.Assert checks in Position and Searcher on
dotnet test StoopidChess.slnx -c Release --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults/coverage
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
   `BenchTests.Depth5Signature` (42,533 nodes; depth 6 is 79,688). Any change to search
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
   What has been tried and what it measured, kept or not, is in [docs/SEARCH.md](docs/SEARCH.md) (search) and
   [docs/EVALUATION.md](docs/EVALUATION.md) (evaluation): add a line there for every change you judge. A 100-game result
   between about +15 and +25 is neither kept nor rejected: a second 100 games are played and the change is judged on the 200.

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
- **`Position` comes only from FEN** and changes only by make/unmake; the GUI edits a Play `BoardSetup` and builds the
  position from its FEN. `Position` is one partial class in two files: `Position.cs` (the board, make/unmake) and
  `Position.MoveGeneration.cs` (the legal generator). `Checkers` is worked out once per move
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

- .NET idioms: file-scoped namespaces, records for data,
  `ArgumentNullException.ThrowIfNull` on public entry points, `CancellationToken` last, nullable
  returns rather than `Try`/`out` pairs. No static mutable state; the node count lives in its `Searcher`.
- **Names follow Microsoft's "C# identifier naming rules and conventions"** (learn.microsoft.com/dotnet/csharp/
  fundamentals/coding-style/identifier-names, checked 2026-11-10), enforced as warnings in `.editorconfig`
  (IDE1006, which `dotnet format style` fails on): PascalCase for types, namespaces, methods, properties, events and
  constants (including private ones); camelCase for locals and parameters; `_camelCase` for private and internal
  instance fields; `s_camelCase` for private and internal static fields, `static readonly` tables included; `I` on
  interfaces and `T` on type parameters. Underscores appear nowhere else. A declaration that mirrors a native name
  (SDL's `SDLK_*`) would be exempt in a section of its own in `.editorconfig`; none is declared here, the SDL3
  bindings being a package. The house rule on top: names spelled out, booleans starting is/has/can/may/do/does.
- American spelling in identifiers and comments: `Color`, not `Colour`.
- **Formatting is two tools with separate jobs.** CSharpier (pinned in `.config/dotnet-tools.json`, configured by
  `.csharpierrc.json`: `printWidth` 120, the C++ repositories' ColumnLimit, 4-space indent, LF) owns the layout of
  every `.cs` file: run `dotnet csharpier format .`. `dotnet format` keeps the style and analyser rules (usings,
  `var`, braces on every block, the naming rules above) and never the whitespace: run
  `dotnet format style StoopidChess.slnx`, and fix by hand what it cannot (it cannot rename). The pre-commit hook
  in `.githooks/pre-commit` and CI's `format` job run `dotnet csharpier check .` and
  `dotnet format style StoopidChess.slnx --verify-no-changes`; enable the hook once per clone with
  `git config core.hooksPath .githooks`, and `dotnet tool restore` (or `tools/setup-dev.*`) installs CSharpier.
  VS Code formats C# files on save with the CSharpier extension (`.vscode/settings.json`).
  The house C++ style (`.clang-format`: Microsoft base, braces on their own lines, braces on every statement, a blank
  line between definitions, 120 columns) is matched where C# allows: Allman braces and `csharp_prefer_braces` (a
  warning) from `.editorconfig`, the blank lines and 120 columns from CSharpier. **Not expressible:** column
  alignment of consecutive assignments, declarations, operands and trailing comments (`AlignConsecutive*`,
  `AlignOperands`, `AlignTrailingComments`): CSharpier has no such option and rewrites any alignment by hand, so the
  C# is not aligned. CSharpier's own style (its wrapping of long chains and argument lists) is not configurable
  either, and is the one place the C# differs from clang-format's.
- **The reformat and rename commit is listed in `.git-blame-ignore-revs`**: `git config blame.ignoreRevsFile
  .git-blame-ignore-revs` makes `git blame` skip it (GitHub does so already).
- American spelling in identifiers and comments: `Color`, not `Colour`.
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
