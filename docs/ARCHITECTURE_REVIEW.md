# Architecture review and proposals (September 2026)

A high-level review of the restructured code (commit `b23d537`), combining the author's own
assessment with an independent architect's pass. The verdict first: the four-project cut is right,
every dependency points the right way, and the 0-40 result against pawnstar is a search-technique
gap, not an architecture or board-representation problem. What follows is refinement, ordered by
value per line changed, and clearly split into simplifications and capabilities.

Sizes: Core 2,000 lines, Engine 1,900, Uci 80, Gui 1,160 (+1,090 designer), tests 1,670.

## What is right and should stay

- **Core / Engine / Uci / Gui.** Core has no engine knowledge; Engine depends only on Core; the GUI
  talks to the engine only through UCI text. A fifth project for the UCI client would not remove the
  GUI's compile-time reference to Engine, because hosting the built-in engine in-process needs
  `UciSession`. Keep `UciEngineClient` in `StoopidChess.Engine.Uci`.
- **In-process engine over anonymous pipes.** One code path, exercised by the GUI exactly as the
  tests exercise it. An in-process `IChessEngine` adapter would give the GUI a second path and
  reintroduce the coupling the refactoring removed.
- **`UciSession` on a `Task` with a synchronous stop.** The simplest correct design for a
  single-threaded command loop.
- **Perft, bench signature, make/unmake key oracle** as the three regression gates.
- **`int` squares with a static `Squares` helper**, record structs for `Move` and `Piece`, and the
  rest of the modern C# idiom.

## Where the strength is (capability, do first)

Depth 5-7 against 11-15 is a 6-8 ply gap. Bitboards would buy roughly 2-3x nodes per second,
which is about one ply. The rest is pruning and ordering, and `Searcher.cs` accepts all of it
without structural change:

| Feature | Where it goes | What it needs |
|---|---|---|
| Null-move pruning | top of `AlphaBeta` after the TT probe | `Board.MakeNullMove`/`UnmakeNullMove` (~30 lines in Core) |
| Late move reductions, late move pruning | the move loop | a move index and `IsQuiet` |
| History heuristic | `ScoreMove` | `int[colour, from, to]` updated beside `RecordKiller` |
| Aspiration windows | the iterative deepening loop | `SearchRoot` taking alpha/beta |
| TT probe and delta pruning in quiescence | `Quiescence` | nothing |
| Check extension by "gives check" | move loop | `MoveGenerator.GivesCheck` |
| Static exchange evaluation | ordering and quiescence pruning | attackers-to-square; the first item bitboards make markedly easier |
| Lazy SMP | one `Searcher` per thread (already the model) | a 16-byte lockless TT entry, see below |

About 280 lines. Every step changes the bench signature (expected) and must be judged by
`tools/match.ps1`, not by the unit tests. One feature per commit.

Cheap speed within the current design (~40 lines, guarded by perft and bench):

- `Board.SquaresOf` returns `IReadOnlyList<int>`, so every `foreach` in generation, evaluation and
  attack detection boxes an enumerator at every node. Return `ReadOnlySpan<int>` via
  `CollectionsMarshal.AsSpan`.
- The public indexer range-checks on every ray step; add an internal unchecked accessor for the
  generator and evaluator.
- `Squares.IsOnBoard` tests two masks where `(square & ~0x77) == 0` already implies the second.
- Maintain the material and piece-square score incrementally in `Board.MakeMove` rather than
  summing the piece lists at every evaluation.

## Simplifications (do now, low risk)

1. **`PositionBuilder` replaces editing on `Board`.** `SetPiece`, `SetSideToMove`, `Empty()` and
   `EditCount` exist only for the GUI's position editor, yet they sit on the search workhorse and
   forced `Game` to defend itself three ways (refusing `Ply != 0`, a cache keyed on key, ply and
   edit count, and review item 33). A Core `PositionBuilder` with `From(Board)`, `Set(square,
   piece)`, `SideToMove`, `CastlingRights`, `IsLegal` and `Build()` leaves `Board` mutable only by
   `MakeMove`/`UnmakeMove`, collapses `Game`'s cache to plain invalidation on its own moves, and
   turns the GUI's `editedBoard` into `PositionBuilder? editing`. Roughly line-neutral; removes a
   whole class of invariant bugs.
2. **Merge `SearchProgress` and `SearchResult`** (the same record with one field renamed) and trim
   `SearchStatistics` to the four counters anything reads. About -30 lines.
3. **`IllegalPositionForm` and `MoveNotPermittedForm`** with their designers (~220 lines) are two
   `MessageBox.Show` calls.
4. **Designer names.** `debugToolStripMenuItem` is relabelled "Engine" at runtime and
   `cPUVsCPUGameToolStripMenuItem` is retitled in code. Rename and set the text in the Designer once
   and delete `BuildEngineMenu`'s relabelling. Pick one handler naming convention (`OnXxx`) for the
   three currently in use.
5. **`UciSession`'s `eval` command** allocates a fresh evaluator (and 1 MB pawn hash) per call; use
   the searcher's.
6. **`MainForm.OnLoad` is `async void`**; wrap its body so an engine start failure cannot bring the
   process down.

## Capabilities for later

7. **Compact `Move` and a 16-byte transposition entry** (done: `Move` is 4 bytes, the entry 16, the secondary key removed; self-play neutral at 10+0.1, perft 16 -> 20 Mnps). `Move` carries the moving and captured
   piece (derivable from the board) and `TranspositionEntry` is 40-48 bytes, so 64 MB holds about
   1.4M positions where 16-byte entries would hold 4M, and concurrent stores tear, which blocks
   Lazy SMP. Pack the move into 16-32 bits and the entry into 16 bytes verified by a partial key
   plus pseudo-legality of the stored move (the table move is already only sorted first, never
   played blind). The secondary Zobrist key then goes, saving hashing work in `MakeMove`. About 200
   lines across Core, Engine and tests; medium risk; do after the search features.
8. **`GameController` in a cross-platform `StoopidChess.Play` project** (done: `MainForm` is now ~470 lines of drawing, mouse and menus; 13 controller tests run on the Linux CI job). `MainForm.cs` (860 lines)
   mixes engine lifecycle, the game state machine, editing, drawing, mouse and menus, and every GUI
   bug the adversarial passes found lived in the untested state machine. A controller owning
   `Game`, the engine client, `humanColour`, `level`, the thinking state and generation counter,
   the clocks and the editor, with events for changes and engine loss, leaves `MainForm` as paint,
   drag and menus (~400 lines). Putting it, `Level` and `SavedGame` in a small non-Windows project
   lets the Linux CI job test it against the in-process engine: new game and undo during a search,
   engine crash, edit-to-legal. About 550 lines; medium risk (keep the controller single-threaded
   and capture the `SynchronizationContext`).
9. **`TimeManager`.** `SearchLimits.TimeBudget` puts time policy in a `go` DTO while the other half
   (the half-budget iteration rule, the node interval between clock checks) is in `Searcher`. Merge
   when adding soft and hard limits.

## Tried, then kept with PVS

- **Aspiration windows** alone measured -35 ± 76 Elo; after principal variation search was added
  they measured +64 ± 75 and are now in.

## Not recommended

- **Bitboards now.** Rewriting `Board`, `MoveGenerator` and `Evaluator` (~1,000 lines) with perft
  as the safety net is feasible but returns about one ply. Revisit only if profiling after the
  search work shows attack detection dominating, or when SEE, mobility and king-safety terms are
  wanted, which is where attack maps genuinely pay. The Core boundary means a bitboard `Board`
  could replace the current one without touching Engine, UCI or GUI code beyond the generator API.
- **A separate UCI client project, an in-process engine interface, or replacing the pipes.** See
  "what is right".
- **Moving `perft.epd` out of Core.** It is a few kilobytes consumed by the production `perft`
  command.

## Testing and CI

The structure is right and needs no change now. Evaluation symmetry under colour flip and the
`FormatGo`/`ParseGo` round trip are already tested. Add `GameControllerTests` when item 8 lands. A
nightly fastchess job is worth it only once opponent binaries are checked in or fetched.

## Priority summary

| # | Change | Kind | Size | Risk | When |
|---|---|---|---|---|---|
| 1 | Null move, LMR, history, TT in quiescence (done); aspiration windows (tried, no gain) | capability | ~280 lines | signature churn; judge by match | done |
| 2 | Span piece lists, unchecked indexer (done; incremental PST score not done, it would couple Core to the evaluator) | capability | ~60 | low | done |
| 3 | `PositionBuilder`; strip editing from `Board` and `Game` | simplification | neutral | low | done |
| 4 | Merge result records, trim statistics, dialogs to `MessageBox`, Designer names | simplification | about -300 | low | done |
| 5 | Compact `Move`, 16-byte TT entry, drop secondary key | capability | ~200 | medium | done |
| 6 | `GameController` in `StoopidChess.Play` with tests | capability | ~550 | medium | done |
| 7 | `TimeManager` | simplification | ~50 | low | with soft/hard limits |
| 8 | Bitboards | capability | ~1000 | high | not now |
| 9 | Separate client project, in-process interface, replace pipes | – | – | – | no |
