# Stoopid Chess

[![CI](https://github.com/reckless-technology/stoopid-chess/actions/workflows/ci.yml/badge.svg)](https://github.com/reckless-technology/stoopid-chess/actions/workflows/ci.yml)

A chess engine and a Windows chess GUI in C#. The engine speaks [UCI](https://www.chessprogramming.org/UCI),
so it can be used from any chess GUI and tested against other engines; the GUI can play against any
UCI engine, with the built-in one as the default. Originally written in 2009, ported to .NET 10 and
restructured in 2026.

## Layout

| Project | Purpose |
|---|---|
| `src/StoopidChess.Core` | The rules of chess: `Board` (bitboards plus a square array, with incremental Zobrist keys), `Attacks` (attack tables and magic bitboards), `Move`, `Piece`, FEN, legal move generation, `Game` (move history and result detection), perft. Cross-platform, no dependencies. |
| `src/StoopidChess.Engine` | The engine: `Evaluator`, `TranspositionTable`, `Searcher` (iterative deepening alpha-beta with quiescence), `SearchLimits`, `UciSession` (the protocol handler) and `UciEngineClient` (drives any UCI engine, in-process or external). Cross-platform. |
| `src/StoopidChess.Play` | `GameController`: the state machine behind a chess GUI (game, engine lifecycle, thinking, editing, clocks, save/load) with no UI, so it is tested on Linux against the in-process engine. Also `Level` and `SavedGame`. |
| `src/StoopidChess.Uci` | Console program `stoopid-chess`: the engine as a UCI executable, plus `bench` and `perft` subcommands. |
| `src/StoopidChess.Gui` | Windows Forms GUI: drawing, mouse and menus over `GameController`. Talks to the engine only through UCI. |
| `tests/StoopidChess.Tests` | xunit tests for everything above. |

The GUI is Windows only; everything else builds and runs on Linux and macOS too.

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build StoopidChess.slnx -c Release          # everything (Windows)
dotnet build src/StoopidChess.Uci -c Release       # engine only (any OS)
dotnet test tests/StoopidChess.Tests -c Release    # the test suite
dotnet run --project src/StoopidChess.Gui          # play
dotnet run --project src/StoopidChess.Uci -c Release -- bench      # search signature and speed
dotnet run --project src/StoopidChess.Uci -c Release -- perft      # move generation check
```

Warnings are errors and nullable reference types are on throughout. `.vscode/` has tasks for all of
the above and launch configurations for debugging the GUI and the engine.

## The engine

- Bitboard board representation (a set of squares per piece type and per colour, plus a square
  array), magic bitboards for the sliding pieces, and a fully legal move generator that resolves
  pins, checks, en passant and castling during generation rather than by make/test/unmake. Modelled
  on pawnstar; the design and measurements are in `docs/BITBOARDS.md`.
- Zobrist keys for the position and for the pawn structure, generated from fixed seeds so every run
  is reproducible; 16-byte transposition entries verified by the full 64-bit key.
- Negamax alpha-beta (principal variation search) with iterative deepening and aspiration windows, a two-tier transposition table (depth-preferred and
  always-replace), null-move pruning, late move reductions, killer moves and a history heuristic, MVV/LVA capture ordering, a quiescence search
  with stand-pat, a check extension at the horizon, and mate distance pruning.
- Evaluation: material and piece-square tables, pawn structure (passed, doubled, blocked, defended
  pawns, cached by pawn hash), bishop mobility, and separate king tables for the endgame.
- UCI: `uci`, `isready`, `setoption name Hash`, `ucinewgame`, `position`, `go` (depth, nodes,
  movetime, wtime/btime/winc/binc/movestogo, infinite), `stop`, `quit`; plus `d`, `eval`,
  `perft N` and `bench [depth]`.

The search is deterministic, so the `bench` node count is a signature of the search and evaluation.
Current values: depth 5 = 242,733 nodes, depth 6 = 585,859 nodes. A test asserts the depth-5
figure; update it (and CLAUDE.md) whenever a change to search or evaluation is intended.

## Testing

Modelled on the sibling engines pawnstar and zenith:

- **Perft** against the 128 standard positions (Ethereal's `standard.epd`, embedded in Core) is the
  move generation gate. The unit tests run each position to the deepest depth under 300k nodes;
  `stoopid-chess perft [max million nodes]` runs the full set (about 80 million nodes per second).
- **Move generation** is also cross-checked against a deliberately naive reference generator on the
  standard positions, their children and random games, with the awkward cases (en passant that
  exposes the king along the rank, pinned pawns, double check, castling through attacked squares)
  asserted directly; the magic bitboard tables are checked against ray walking on random occupancies.
- **Make/unmake invariants**: random games check that the incremental keys match a recomputation
  after every move, that the bitboards and square array agree, and that unwinding restores the
  start position exactly.
- **Evaluation regression**: the scores of eighteen positions recorded before the move to bitboards.
- **Rules**: checkmate, stalemate, repetition, the fifty-move rule, insufficient material,
  castling rights, en passant, promotions, FEN round trips.
- **Search**: mates in one and two, being mated, draw recognition, determinism, limits and
  cancellation, principal variation legality.
- **UCI**: the session driven through in-memory streams, and the client talking to the engine over
  pipes exactly as the GUI does.
- **Bench signature**.

CI (`.github/workflows/ci.yml`) runs the engine build, tests, perft and bench on Linux, and the full
solution build, tests and single-file publishes of the GUI and engine on Windows.

## Playing against other engines

The engine executable works in any UCI GUI (Arena, Cute Chess, Banksia) and with tournament tools
such as `cutechess-cli` or `fastchess`, which is how it can be measured against pawnstar or zenith.
In the Stoopid Chess GUI, *Engine > Choose UCI engine...* swaps the built-in engine for an external one.

## Saved games

`*.chess` files are JSON: the starting position as FEN, the moves in UCI notation, and the game
settings. Files from before September 2026 used .NET binary serialisation and cannot be loaded.

## History

This project lived in the `StoopidChess` directory of the `reckless-technology/software` repository
until September 2026, when it was split out with its history. `docs/CODE_REVIEW.md` records the
review that preceded the restructuring and `docs/ARCHITECTURE_REVIEW.md` the proposals that followed it.
