# How Stoopid Chess is put together

This is the tour for someone who wants to learn from the code. [README.md](../README.md) says what the program does and
how to build it; this says where things are, how they fit, and what happens between a click on the board and the
engine's reply. [CLAUDE.md](../CLAUDE.md) is the rulebook for working on the repository.

## The five projects

```
Core  <-  Engine  <-  Play  <-  Desktop          Uci  ->  Engine
```

Every arrow points one way: a project knows nothing of the ones to its right.

| Project | What it is | Read it for |
|---|---|---|
| `StoopidChess.Core` | The rules of chess, with no dependencies. | Bitboards, move generation, FEN, game status. |
| `StoopidChess.Engine` | The search, the evaluation and the engine's side of UCI. | Alpha-beta, the transposition table, `UciSession`. |
| `StoopidChess.Play` | Everything a chess GUI needs except drawing: the game, the engine it plays against, clocks, saving. | `GameController`: how a GUI holds an engine. |
| `StoopidChess.Desktop` | The window: SkiaSharp drawing in an SDL3 window. | Skip it on a first read; it is GUI plumbing, not chess. |
| `StoopidChess.Uci` | The `stoopid-chess` console program: the engine as a UCI executable, plus `bench` and `perft`. | A few lines of `Program.cs`. |

## Where to start reading

In this order, each file standing on the one before:

1. `Core/Square.cs`, `Bitboard.cs`, `Move.cs`: the value types everything is written in. A square is its bit number
   (a1 = 0 to h8 = 63), a set of squares is a `ulong`.
2. `Core/Position.cs` with `Attacks.cs`: the board, make and unmake; then `Position.MoveGeneration.cs`, legal move
   generation ([BITBOARDS.md](BITBOARDS.md) explains the design). Then `Game.cs`, which adds move history and "is it over?".
3. `Engine/Evaluator.cs` ([EVALUATION.md](EVALUATION.md) says what each term is worth) and `Engine/Searcher.cs`.
4. `Engine/Uci/UciSession.cs`: the engine side of the UCI protocol.
5. `Play/UciEngineClient.cs`: the GUI side of the same protocol.
6. `Play/GameController.cs`: the GUI's whole brain, with no drawing in it.
7. Only then `Desktop/MainView.cs` and `BoardView.cs`.

## Life of a move

You drag the white e-pawn from e2 to e4, and the engine answers. Every step, in the order it happens:

1. **`BoardView.OnPointerDown`** (Desktop) turns the press into a square and, if it holds one of your pieces on your turn,
   starts a drag, remembering the legal destinations to draw as dots. `OnPointerMove` follows the pointer.
2. **`BoardView.OnPointerUp`** turns the release into a square. A release off the board cancels; otherwise the board
   raises its `MoveDropped(from, to)` event. The board decides nothing about legality.
3. **`MainView.OnMoveDroppedAsync`** receives it. It asks the controller which legal moves go from e2 to e4
   (`GameController.HumanMoveCandidates`): none gives a "not permitted" message; several (a pawn reaching the last rank)
   open the promotion dialog; exactly one is played.
4. **`GameController.TryHumanMove`** finds the move on its `Game` and calls `MakeMove`, which plays it, gives the mover the
   clock's increment and calls `Resume`.
5. **`Resume`** raises `Changed` (the window redraws, showing your move) and then `StartThinkingIfEngineTurn`.
6. **`StartThinkingIfEngineTurn`** sees it is the engine's turn. It sends the engine the *whole game so far*
   (`UciEngineClient.SetPosition`, which writes `position startpos moves e2e4`, UCI engines keeping no game of their own)
   and starts `ThinkAsync`, which awaits `UciEngineClient.GoAsync`: `go depth 3 wtime ... btime ... winc ... binc ...`,
   the depth coming from the chosen difficulty level and the times from the chess clocks.
7. **The pipes.** The built-in engine is not a separate program. `UciEngineClient.StartInProcess` runs a `UciSession` on a
   background thread of the same process and connects it to the client with two anonymous pipes, one each way; the two
   talk the same text protocol they would over a console. An external engine (*Engine > Open UCI engine...*) is a child
   process with its standard input and output as the pipes. Either way the GUI has one code path, and the only thing it
   ever knows about an engine is UCI.
8. **`UciSession`** reads the lines, rebuilds the position from `position ... moves ...` and, on `go`, starts the search on
   a task (so `stop` and `isready` still work while it thinks) over a clone of the position.
9. **`Searcher.Search`** (Engine) does iterative deepening, alpha-beta with a transposition table, and quiescence, from the
   `SearchLimits` the `go` line described. While it runs it writes `info depth ... score ... pv ...` lines, which the client
   hands to the GUI for the analysis window.
10. **`bestmove e7e5`.** The session writes it back down the pipe. The client's reader thread completes the `GoAsync` task.
11. **`ThinkAsync` continues** on the GUI's thread (the awaits capture the synchronization context), always after a yield:
    handled in the same call that started the search, the reply would play the move and start the next search before
    the first was stored. It asks `Game.FindMove` to turn "e7e5" into a legal `Move`, plays it with `MakeMove`, and the
    cycle of step 5 raises `Changed` again. The window redraws with the engine's move on the board.

If you move before the engine has answered, or undo, or start a new game, the controller bumps a generation counter and
cancels the search; the late reply is recognised as stale and dropped.

## Core: the rules

- **Value types.** `Square` (one byte), `Bitboard` (a `ulong`), `MoveList` (256 moves held inline, so generating allocates
  nothing) and `Move` (32 bits). They are structs with operators, at no run-time cost; the engine's `bench` node count is
  the proof, since it would change if they changed anything. There is no coloured-piece type: where a caller wants both
  at once it is a `(Color, Piece)` pair.
- **`Position`** is the board as bitboards (per piece type and per colour) plus a 64-entry square array, with `MakeMove`
  and `UnmakeMove` (strictly last in, first out), Zobrist keys for the position and for the pawn structure, castling
  rights, the en-passant square and the move clocks. Move generation is part of it and produces *legal* moves only, working
  out pins, check masks, en passant and castling as it goes instead of making each move to see whether it was legal.
  A position comes from FEN; the GUI sets boards up by hand in Play's `BoardSetup` and turns them into FEN.
- **`Attacks`** holds the knight, king and pawn tables and the magic-bitboard tables for sliders, built at startup.
- **`Game`** wraps a position with its history and a `GameStatus` (checkmate, stalemate, the draws), and is what the
  controller holds. `Fen` and `Move.ParseUci` turn text into these and back (SAN and PGN, which only the GUI needs, are in
  Play).

## Engine: the search

`Searcher` owns the per-search state (killer moves, history tables, a move list per ply) and searches a clone of the
caller's position. Mate scores are `±(MateScore - ply)`, converted to node-relative form across the transposition table.
`SearchLimits` mirrors UCI's `go`, and `SearchLimits.TimeAllocation` is the time manager: it shares the clock over the moves to
go, adds most of the increment and never spends more than most of what is left. `TranspositionTable` has two tiers of
16-byte entries, each verified by the full 64-bit key. `Evaluator` starts from the Simplified Evaluation Function and keeps
a small cache of scores. `Bench` is the fixed set of positions behind the node-count signature that a test asserts: the
search is deterministic, so any change to the search or the evaluation shows up as a different number.

## UCI, both halves

UCI is the text protocol chess GUIs and engines speak. It has two sides and each is here:

- `Engine/Uci/UciSession` is the *engine* side. It reads commands from any `TextReader` and writes replies to any
  `TextWriter`, so the console program wires it to standard input and output and the tests wire it to strings.
- `Play/UciEngineClient` is the *GUI* side, with `StartInProcess()` for the built-in engine and `StartProcess(path)` for
  an external one. It sends commands, reads replies on a reader thread, and turns `uciok`, `readyok` and `bestmove` into
  awaitable tasks.

## Play: how a GUI holds an engine

`GameController` is the class to study. It owns the `Game`, the one engine client, whose turn it is, and the thinking
state; it raises `Changed`, `EngineFailed` and `EngineIllegalMove`. The human always plays White or Black and the engine the
other side.

- **One thread.** It is not thread safe. Engine replies arrive on another thread, but the awaits continue on the thread
  that made the call, so the controller must be used from one with a single-threaded `SynchronizationContext` (the GUI's UI
  thread; the tests give theirs a `ControllerThread`). `WaitUntilIdleAsync` completes once the engine has stopped thinking
  and its last reply has been handled.
- **Level and clock.** `Level` is the difficulty and caps the search depth. Every game is also played against a countdown
  `TimeControl` (base time plus increment, or "40 moves in 5 minutes": the base time is added again after each 40 moves a
  side makes): `Tick` runs the side to move's clock down, the mover gains the increment after each move, a side that
  reaches zero loses on time (`TimedOut`), and the engine is told the clocks with every `go`, with `movestogo` counting
  down the moves left in the period.
- **Editing.** Right-clicking a square edits the position (`EditSquare`, `SetSideToMove`, `ClearBoard`) on a `BoardSetup`,
  which may hold anything while it is edited. It is shown, but not played from, until play can proceed from it; then it
  becomes a new game.
- **Saving.** `SavedGame` is JSON holding the starting FEN, the moves in UCI notation and the settings. PGN import and export
  are `Play/Pgn` and `Play/San`.
- **Failure.** An engine that cannot start, dies, or plays an illegal move is reported through the events, and the controller
  falls back to the built-in engine or to none.

## Desktop: the window

SkiaSharp draws, SDL3 supplies the windows, events, file dialogs and clipboard. Three layers:

- **`Look/`**: the colours (`Theme`), fonts and the drawing helpers (`Plates`, `Drawing`) that give the program the
  Reckless series' look.
- **`Views/`**: a small retained tree of views with no window system under it. A `View` draws in its own coordinates and takes
  pointer, wheel, key and text events; `ViewRoot` sits at the top of a window, holds the content and the overlays on it (an
  open menu, a modal dialog), and routes events: a press goes to the topmost view that takes it, which then keeps the pointer
  until the release; keys go to the focused view, the top overlay first. There is no layout engine: each view sets its
  children's bounds in `Layout`. The widgets are `PlateButton`, `MenuBarView`, `MenuPopup`, `LabelView`, `TextListView` and
  `DialogView`.
- **The app**: `MainView` (the layout, the menus and the wiring of the controller to everything) with `BoardView`,
  `CaptureBinView`, `StatusBarView`, `AnalysisView` and `Dialogs/`. It asks the window system for files, the clipboard and
  other windows only through `IDesktopPlatform`, which `Sdl/` implements and the tests replace with a fake.

`Sdl/` is the host: `SdlHost` runs the event loop and the clock's 100 ms tick, `SdlWindow` draws Skia into a bitmap shown
through an SDL texture, `SdlPlatform` does the native dialogs. It holds no logic of its own; everything it does is a call
to SDL or to a view, which is why the views can be tested without a window.

## Tests

`tests/StoopidChess.Tests` holds everything. Perft against the standard positions gates move generation; make/unmake
invariants run over random games; the bench signature pins the search; the UCI session is driven through in-memory streams;
`GameController` is played against the real in-process engine and against scripted fakes (an engine that answers at once,
dies, or plays an illegal move); and every view is drawn to a bitmap and worked by synthesized events, so the whole
GUI is tested without a display.

## Further reading

- [BITBOARDS.md](BITBOARDS.md): the board and the legal move generator.
- [EVALUATION.md](EVALUATION.md): the evaluation, term by term, with what each was measured to be worth.
- [history/](history/): the 2026 reviews that shaped the restructuring, kept as a record; their names are out of date.
