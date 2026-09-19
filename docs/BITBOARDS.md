# Bitboard board representation (September 2026)

The plan for replacing the 0x88 board and its make/test/unmake legality filter with a bitboard
board and a fully legal move generator, modelled on pawnstar (`reckless-technology/pawnstar`,
`src/position.h`, `pins.h`, `attacks.h`, `generated_data.h`). Written before the work started; the
results section at the end was filled in afterwards.

## Why now

The architecture review deferred bitboards ("about one ply, revisit when attack maps pay").
Two things changed the calculus: the search now has the pruning that the review put first, so
raw speed is the next lever, and the next evaluation and search features on the list (static
exchange evaluation, mobility, king safety, `GivesCheck`) all want attack sets, which the 0x88
board cannot provide cheaply. Baseline before the change, single thread, Release:

| Measure | Before |
|---|---|
| perft, 128 standard positions, depths under 1M nodes | 31.0M nodes, 16.1M nps |
| bench depth 5 | 241,437 nodes, ~600K nps |

## Design

### Squares: 0x88 becomes 0..63 (little-endian rank-file)

`Squares` keeps its public shape (`int` squares, `Index`, `FileOf`, `RankOf`, `IsOnBoard`, `All`,
`Name`, `Parse`, the `A1`..`H8` constants, `None = -1`) but a1 is 0, h1 is 7 and h8 is 63, so a
square is a bit index. Everything that did 0x88 arithmetic (`PawnDirection` is now ±8, the
generator's deltas, `Zobrist`'s 128-entry tables, the evaluator's 128-entry tables,
`PositionBuilder`'s array, `Move`'s 7-bit square fields) follows. Callers that only use the
`Squares` helpers (the GUI, the controller, FEN, notation) do not change.

### Board: eight bitboards plus a mailbox

```
ulong[] byType    (indexed by PieceType, both colours)
ulong[] byColour  (indexed by Colour)
ulong   occupied
Piece[] squares   (64-entry mailbox: what is on a square, for captures, FEN and the GUI)
```

`PutPiece`/`RemovePiece` toggle the three bitboards, write the mailbox and XOR the Zobrist keys,
exactly as before but without the `List<int>` piece lists. Public accessors:

- `Pieces(colour, type)`, `Pieces(colour)`, `Pieces(type)`, `Occupied`, `Count(colour, type)`
  (population count), `KingSquare(colour)`, `PieceCount`.
- `Checkers`: the enemy pieces attacking the side to move's king, computed lazily and cached until
  the next make/unmake. `IsInCheck => Checkers != 0`. `AttackersTo(square, by)`.
- Gone: `SquaresOf` (iterate a bitboard instead) and `NormalisePieceOrder` (bitboard iteration is
  least-significant-bit first, so generation order is a pure function of the position and the
  searcher no longer needs to sort anything).

### Attack tables (`Attacks`, new)

- Knight, king and pawn attack tables (64 entries each), `Between[from, to]` (squares strictly
  between two aligned squares), empty-board bishop and rook rays.
- Sliding attacks by magic bitboards: `((occupancy & mask) * magic) >> shift` indexes a shared
  attack table (about 108K entries, 860 KB). The 128 magic multipliers are pawnstar's; they are a
  plain 64-bit multiply, so they are portable (no `pext`, which is slow on AMD Zen 1/2). The tables
  are built at startup from the constants; a debug assertion checks that no two occupancies with
  different attack sets share a slot.
- Bit iteration uses `System.Numerics.BitOperations.TrailingZeroCount` and `PopCount`, which the
  JIT compiles to `tzcnt`/`popcnt`, with `bits &= bits - 1` to clear the lowest bit (`blsr`).

### Fully legal move generation

`MoveGenerator.GenerateLegalMoves` produces only legal moves, so the searcher's make/test/unmake
filter (`TryMakeLegal`) and `GeneratePseudoLegalMoves` go. Following pawnstar:

1. King moves: target squares minus every square the enemy attacks with our king removed from
   the occupancy (so x-ray attacks through the king count).
2. In double check only king moves are legal. In single check the capture mask is the checker and
   the quiet mask is `Between[king, checker]`.
3. Pins: for each enemy slider aligned with our king, if exactly one piece stands between and it is
   ours, it may only move along the ray (between squares or the pinner).
4. Pawns by whole-board shifts: pushes, double pushes, captures east and west, promotions, then en
   passant with pawnstar's two special cases: the captured pawn must be in the capture mask (so an
   en passant capture answers a check only by taking the checking pawn) and, when our king shares
   the rank, removing both pawns must not expose a rook or queen along it.
5. Castling when not in check, the between squares empty and the two crossed squares unattacked.

`GenerateLegalCaptures` (captures and promotions) serves quiescence.

### Evaluation

Same terms, same values, computed from bitboards: piece-square sums iterate each bitboard; bishop
mobility is `PopCount(Bishop(occupied, sq) & ~own)`; pawn structure uses precomputed forward-file
and forward-adjacent-file masks per colour and square. The evaluation of every position is
unchanged, and a test pins the scores of eighteen positions recorded from the 0x88 evaluator.

### Search

`Searcher` loses `NormalisePieceOrder`, `TryMakeLegal` and the pseudo-legal generation; the fifty
move rule in check becomes "any legal move draws". The history table shrinks to `[3, 64, 64]`.
Everything else (ordering, pruning, table) is untouched.

## What changes the bench signature

The Zobrist keys are regenerated for 64 squares (different table indexing), and the generation
order of equal-scored quiet moves differs, so the node count changes even though the evaluation
and the search algorithm do not. The new signature is recorded in `BenchTests`, README.md and
CLAUDE.md as the testing discipline requires.

## Steps

1. Freeze the oracles: perft counts (already embedded), the evaluator's scores for eighteen
   positions (recorded from the 0x88 build), the current bench and perft speed.
2. `Squares` to 0..63; `Bitboards` helpers and `Attacks` tables with unit tests against naive
   ray-walking implementations.
3. `Move` with 6-bit squares, `Zobrist` with 64-square tables.
4. `Board` and `Board.MakeMove` on bitboards with a lazily cached `Checkers`.
5. `MoveGenerator` rewritten as a legal generator; `Perft` unchanged.
6. `Fen` and `PositionBuilder` for 64 squares.
7. `Evaluator` and `Searcher` on bitboards.
8. Tests: update the few that hard-code 0x88 indices, add `AttacksTests`, `MoveGeneratorTests`
   (a slow reference generator cross-checked on random positions plus the classic en passant,
   pin and double-check cases) and the frozen evaluation scores; record the new bench signature.
9. Measure perft and bench speed, self-play against the pre-change build with
   `tools/selfplay.ps1`, update README.md, CLAUDE.md and ARCHITECTURE_REVIEW.md.

Steps 2 to 8 cannot be landed separately: changing the square indexing breaks the 0x88 generator,
so they form one change gated by perft, the make/unmake key oracle and the frozen evaluations.

## Risks

- A wrong magic table or a subtle generator bug: caught by perft (128 positions, all depths).
- A silent evaluation change: caught by the frozen scores.
- Slower than expected in C# (bounds checks, no `constexpr`): measured; the table accessors are
  small static methods over arrays and the generator works on local copies of the bitboards.
- API breaks for the GUI and controller: none expected, they use `Squares` helpers and `Piece`.

## Results

Everything landed as one change: 13 files modified, 5 added, about 560 lines added and 460 removed
in `src` and about 600 lines of new tests. All 345 tests pass in Release and Debug; perft matches
every listed depth of every one of the 128 positions (651 million nodes); the eighteen frozen
evaluation scores are unchanged, so the evaluation is bit-for-bit the same and the only signature
change comes from the Zobrist keys and generation order.

| Measure | Before | After | Change |
|---|---|---|---|
| perft, 128 positions, depths under 1M nodes | 19.1M nps (1,624 ms) | 81.2M nps (381 ms) | 4.3x |
| perft, every listed depth (651M nodes) | not measured | 190M nps (3.4 s) | |
| bench depth 6 (short run, cold JIT) | 579,880 nodes, 1.42M nps | 585,859 nodes, 2.05M nps | 1.45x |
| 3 s search, start position | depth 13, 2.09M nps | depth 14, 3.80M nps | 1.8x |
| 3 s search, kiwipete | depth 10, 1.89M nps | depth 11, 3.92M nps | 2.1x |
| 3 s search, middlegame (bench position 8) | depth 12, 2.11M nps | depth 12, 3.77M nps | 1.8x |

Bench signature: depth 5 = 242,733 nodes (was 241,437), depth 6 = 585,859 (was 579,880).

The gap between the perft gain and the search gain is expected: perft is almost pure move
generation and make/unmake, while the search also evaluates, probes the table and orders moves,
none of which changed. In a real search the JIT has time to tier up, which is why the timed
searches show about 1.9x and the 300 ms bench only 1.45x.

Self-play against the pre-change build (60 games, 10+0.1, single thread, six-ply openings):
31 wins, 15 losses, 14 draws, **+95 ± 70 Elo** (likelihood of superiority 99.8%). The evaluation
and search are identical, so the whole gain is the extra depth bought by speed: about one ply at
this time control, which is what the architecture review predicted.

## Value types: Square, Bitboard, MoveList

A follow-up replaced the raw `int` squares, `ulong` sets and `List<Move>` with three structs, as
pawnstar does in C++, with the constraint that they cost nothing at run time:

- `Square`: one byte, a1 = 0 .. h8 = 63 plus `Square.None` (64). It converts implicitly to `int`
  so tables index by it directly, but only explicitly from `int` (`new Square(i)`, range checked;
  `Square.Unchecked` internally where the index is known good), so an index can never be passed
  where a square is meant. `Move.From`/`To`, `Board.KingSquare`, `EnPassantSquare`, the attack
  tables, `Game`, `GameController` and the GUI all take and return `Square`.
- `Bitboard`: a readonly record struct over `ulong` with the set operators, `Count`, `IsEmpty`,
  `IsSingle`, `First`, `Contains`, the shifts and constants, and `foreach (Square s in set)` via a
  two-field struct enumerator. `Board.Pieces`, `Occupied`, `Checkers`, `AttackersTo` and every
  `Attacks` method use it.
- `MoveList`: 256 moves inline (`[InlineArray]`) plus a count, so a generated list lives on the
  stack or in a preallocated per-ply array and never touches the heap. The generator appends to a
  `ref MoveList`; `Searcher` keeps one per ply, `Perft` one per depth; `AsSpan()` (marked
  `[UnscopedRef]`) gives the ordering code in-place access; `IEnumerable<Move>` is implemented for
  LINQ in tests.

Cost, measured A/B against the commit before the types (same machine, same session; the bench
signature is identical, so the trees searched are identical):

| Measure | Raw types | Value types |
|---|---|---|
| bench depth 6 (best of 3) | 2.16M nps | 2.13M nps |
| perft, depths under 1M nodes | 86.6M nps | 87.2M nps |
| perft, every listed depth (651M nodes) | 211M nps | 258M nps |
| 3 s search nps, start position / kiwipete | 4.55M / 4.65M | 4.71M / 4.60M |

The struct wrappers are free, as the JIT promotes single-field structs to registers and inlines
the enumerator; the perft gain comes from `MoveList` replacing a heap-allocated list per node.

### No coloured-piece type

`Piece` (a `(Colour, PieceType)` record struct) went next. With a bitboard per colour the colour
half of "what is on this square" is redundant, so the mailbox holds `PieceType` alone and make/unmake
pass colour and type explicitly from the move (the mover is the side to move, a capture is the
opponent's, a promotion is the mover's), exactly as pawnstar does. Callers that want both at once
get a `(Colour Colour, PieceType Type)` tuple from `Board[square]`, `PositionBuilder[square]` and
`GameController.PieceAt`, with the default tuple meaning an empty square; `Game.CapturedPieces`
returns `PieceType`s since its argument is the colour; `PieceType.ToChar(colour)` produces the FEN
letter. Measured against the build before the value types: bench within noise (2.0M vs 2.1M nps on
a 280 ms run), perft 85.7M vs 81.4M nps at the 1M-node budget and 254M vs 195M nps over every
depth, timed searches identical in nodes and within 3% in nps either way.

### What the next steps can now use

- `Board.AttackersTo(square, colour)` and the `Attacks` tables make static exchange evaluation,
  mobility and king-safety terms and a `GivesCheck` test cheap.
- `Board.Checkers` is cached per position, so the searcher's in-check tests cost nothing after the
  first one.
- The searcher no longer needs to make a move to know it is legal, so late move pruning and
  move-count based decisions can count moves before making them.
