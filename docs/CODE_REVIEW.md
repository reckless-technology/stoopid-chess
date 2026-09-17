# Code review of Stoopid Chess (September 2026)

Review of the code as it stood after the .NET 10 port (commit `5a89d68`): a single Windows Forms
project of about 5,400 lines written in 2009. The findings below drove the refactoring that
followed; each item notes what was done about it.

## Architecture

1. **Engine and GUI were one assembly with no boundary.** `IChessEngine` mixed three concerns:
   rules (`IsMoveLegal`, `IsCheckmated`, `EditSquare`), search (`StartComputerThinking`,
   `SetMaxDepth`) and persistence (`Serialize`). The GUI called into all of them and the engine
   called back into the GUI through a delegate and an event marshalled with `Control.Invoke`.
   *Resolved:* split into `StoopidChess.Core` (rules), `StoopidChess.Engine` (evaluation, search,
   UCI), `StoopidChess.Uci` (console) and `StoopidChess.Gui`. The GUI talks to the engine only
   through the UCI text protocol, so any UCI engine can be plugged in.
2. **Global mutable state.** `Game.debug` (search counters), `MoveGenerator.checkHashTable`
   (a 4M-entry static array), `Evaluator.pawnHashTable` and `Transposition.numberOfTranspositionsCreated`
   were static, so two engines in one process would corrupt each other and nothing was testable in
   isolation. *Resolved:* all state lives in `Searcher`, `Evaluator` and `TranspositionTable`
   instances; counters are a `SearchStatistics` object.
3. **The board had no side to move.** Colour was threaded through every method as a parameter, and
   the Zobrist key had the side XORed in on demand. There was no FEN support, so positions could not
   be set up, exchanged or tested. *Resolved:* `Board` tracks side to move, castling rights, en
   passant square, half-move clock and move number, and reads and writes FEN.
4. **Threading by hand.** A raw `Thread` per move, a `System.Timers.Timer` to set a non-volatile
   `bool doCancelSearch`, and cross-thread UI updates via `Invoke`. *Resolved:* the search takes a
   `CancellationToken` and a `SearchLimits` (depth, nodes, move time, clock); the UCI session runs it
   on a `Task`; the GUI uses `async`/`await` and never touches engine threads.
5. **Persistence coupled to object layout.** Save games serialised the whole `Board` graph with
   `BinaryFormatter`, so any change to a private field broke old files (and BinaryFormatter is gone
   from .NET). *Resolved:* save files are JSON holding a FEN and UCI moves, both open standards.

## Correctness bugs

6. **Fifty-move rule counted plies, not moves.** `IsDrawByFiftyMoveRule` looked at the last 50
   entries of the move stack, i.e. 25 moves each. *Fixed:* a half-move clock, draw at 100.
7. **Capturing a rook on its home square kept the opponent's castling right.** `DoMove` cleared
   flags only when the king or rook moved. If a rook was captured on h8 and another rook later
   reached h8, black could castle with it. *Fixed:* rights are lost for any move from or to the
   king and rook home squares; covered by a test.
8. **Only queen and knight promotions were generated.** Rook and bishop under-promotions were
   impossible, which is both a rules error and the reason standard perft counts could never have
   matched. *Fixed:* all four promotions; the 128 standard perft positions now pass.
9. **The in-check cache could return the wrong answer.** `MoveGenerator.IsInCheck` cached results
   in a table indexed and verified by the primary hash only, so a 64-bit collision would have let an
   illegal move through. *Fixed:* the cache is gone; attack detection is cheap enough.
10. **Cancelled searches polluted the transposition table.** When time ran out, `AlphaBeta`
    returned `alpha` from every level, and parents stored those meaningless values as exact or
    lower-bound entries that survived into the next search. *Fixed:* nothing is stored once the
    search has been stopped.
11. **A very short time limit could return no move.** If the timer fired before depth 0 completed,
    `computerMove` stayed `Move.NONE`, and the GUI then called `DoMove` on it, which threw. *Fixed:*
    the root search always keeps the best move found so far, starting from the first legal move.
12. **Inconsistent draw contempt.** Terminal draws returned `-200 + depth` for the side to move,
    which the parent negated, so the sign of the "contempt" depended on who reached the draw.
    *Fixed:* draws score 0.
13. **Mate scores in the transposition table were root-relative.** An entry found from a different
    root gave the wrong distance to mate. *Fixed:* scores are converted to node-relative on store
    and back on probe (`ScoreToTable`/`ScoreFromTable`).
14. **A transposition-table move was played without checking it belonged to the position.** Only
    `Debug.Assert` guarded it; a collision in release would corrupt the board. *Mitigated:* entries
    are verified with two independent 64-bit keys, and the pseudo-legality of the ordered move list
    is what is searched, with the table move merely sorted first.
15. **Position editing left the undo stacks inconsistent.** `EditSquare` cleared the move stack but
    not the castling, en passant or capture stacks. *Fixed:* `Board.SetPiece` resets the history.
16. **`Move.CompareTo` subtracted hash codes**, which can overflow; and `GetHashCode` doubled as a
    unique key in a `SortedDictionary`, an invariant that held only by careful bit layout. *Fixed:*
    `Move` is a record struct with value equality and no ordering; ordering is a search concern.

## Design and idiom

17. `Square` (a piece on a square) and `Move` were classes allocated per node; `SortedDictionary`
    was used for move ordering, allocating heavily in the hottest loop. *Now:* `Piece` and `Move`
    are readonly record structs; moves go into per-ply reusable lists sorted by a stable insertion
    sort on a score.
18. Enum members were `UPPER_CASE`, fields `camelCase` public, and `Colour.XOR` was an enum member
    used to switch sides. *Now:* .NET naming, `Colour.Opponent()`.
19. `MoveType` conflated what happened (`EN_PASSANT` meant a double pawn push) with castling
    flag bookkeeping (`KING`, `ROOK`). *Now:* `MoveKind` describes the move; rights are derived.
20. `IsDrawn` returned a bool and set a `reasonForDraw` string as a side effect. *Now:*
    `Game.Status` returns a `GameStatus` enum.
21. The pawn promotion dialog wrote its answer into a field of the main form. *Now:* the dialog
    exposes `Choice`.
22. `saveFileDialog.Dispose()` was called in `finally` and the dialog reused afterwards.
23. No tests, no CI, no FEN, no UCI, no way to play the engine against another. *Now:* 260-odd
    xunit tests (perft on the standard positions, make/unmake key invariants, game rules, search,
    UCI session and client, a bench signature), GitHub Actions on Linux and Windows.

## Second pass: review of the refactored code

An independent review of the restructured code (commit `f2cc207`) found the following, all fixed in
the commit that followed:

24. **Pawns on the first or eighth rank crashed move generation**, and both the position editor
    and FEN parsing let them in. *Fixed:* `MoveGenerator.IsPositionLegal` and `Fen.Parse` reject
    them; the UCI search task also now catches failures and still answers `bestmove`.
25. **The fifty-move check ran before mate detection in the search tree**, so a mating hundredth
    half-move scored as a draw. *Fixed:* when in check the rule is applied only once a legal move
    exists; a test covers the mate-on-move-100 case.
26. **FEN accepted an en passant square inconsistent with the side to move**, after which the
    generated capture removed a non-existent pawn and corrupted the board. *Fixed:* the square must
    be empty, on the right rank, with the double-pushed pawn behind it; the generator also checks.
27. **Editing a position into a legal one with the engine to move left the GUI idle** with both
    mouse buttons ignored. *Fixed:* `ApplyEditedBoard` starts the engine.
28. **The UCI client had no "engine has exited" state**, so a crashed external engine surfaced as an
    unhandled exception in a mouse handler. *Fixed:* `UciEngineClient.HasExited`; the GUI reports
    the loss once and offers the Engine menu.
29. **Computer-versus-computer games disabled the main menu for the whole game.** *Fixed:* menus
    stay enabled; every game-changing action cancels the search itself.
30. **A search stopped during its first iteration returned a generation-order move** rather than
    the best root move completed so far.
31. **Two tests asserted nothing useful** (a repetition test whose moves were never played, an
    insufficient-material test that was trivially true). Replaced, and tests added for the items
    above, for node-relative mate scores in the transposition table, and for command sequencing
    during a search.

## Things that were good and were kept

- The 0x88 board with piece lists and incrementally maintained Zobrist keys, which is simple and
  fast enough (12 Mnps perft, 1.5 Mnps search).
- Two independent Zobrist key sets from fixed-seed LCGs, so hash verification is strong and every
  run is reproducible.
- The two-tier (depth-preferred plus always-replace) transposition table.
- Iterative deepening, killer moves, MVV/LVA ordering, quiescence search with stand-pat, and the
  check extension at the horizon.
- The piece-square tables, pawn structure terms (passed, doubled, blocked, defended) with a pawn
  hash, bishop mobility, and the middle-game/endgame king tables.
