# The board and move generation

How Stoopid Chess holds a position and generates its legal moves, in the order to read the code:
`Core/Square.cs` and `Bitboard.cs`, then `Attacks.cs`, then `Position.cs` (the board, make and
unmake) and `Position.MoveGeneration.cs` (the legal moves). The design follows the sibling C++
engine pawnstar and the techniques the [Chess Programming Wiki](https://www.chessprogramming.org)
describes; the appendix says how it got here.

## Squares and bitboards

A square is a number from 0 to 63: a1 = 0, b1 = 1, ..., h1 = 7, a2 = 8, ..., h8 = 63. The file is
the low three bits and the rank the next three, so `e4` is file 4, rank 3, square 28. `Square` is
a one-byte struct around that number. It converts to `int` by itself, so it can index a table,
but an `int` becomes a square only on purpose, so a stray number can never pass for one.

A **bitboard** is a set of squares held in one 64-bit word, bit *n* standing for square *n*. Sets
of squares are what chess is made of - the squares a knight attacks, the white pieces, the empty
squares - and on a bitboard each set operation is one machine instruction:

| Question | Bitboard expression |
|---|---|
| Which squares hold white pieces? | `Pieces(Color.White)` |
| Which white knights are there? | `Pieces(Color.White, Piece.Knight)` |
| Which squares are empty? | `~Occupied` |
| Where can this knight go? | `Attacks.Knight(from) & ~Pieces(us)` |
| Where can every white pawn push? | `Pieces(Color.White, Piece.Pawn).North() & ~Occupied` |

`Bitboard` is a struct over the word with the operators `& | ^ ~`, the shifts (`North()`,
`NorthEast()` and so on, which drop squares that would wrap round the edge of the board),
`Count`, `First` and `foreach (Square s in set)`, which visits the squares from a1 upward by
finding the lowest set bit and clearing it. See https://www.chessprogramming.org/Bitboards

## The position

`Position` keeps the pieces three ways at once, each answering a different question fast:

- a bitboard per piece type (all pawns, all knights, ...) and one per color, so that "white
  knights" is one AND;
- the occupancy, every occupied square;
- a 64-entry array of piece types, so that "what is on e4?" is one read. The piece's color comes
  from the color bitboards.

Alongside the pieces it keeps the side to move, the castling rights, the en passant square, the
half-move clock for the fifty-move rule, the move number, two Zobrist keys (the whole position, and
the pawns alone for the pawn-structure cache), the pieces giving check, and a history of the moves
made.

A position comes from FEN (`Position.FromFen`, `Position.Start`) and then changes only by
`MakeMove` and `UnmakeMove`. Make moves the pieces and updates every key incrementally, XORing out
what changed and XORing in what replaced it, then works out the pieces now giving check. It pushes
an undo record with what the move cannot tell it afterwards - the castling rights, en passant
square, clock, keys and checkers it replaced - and unmake pops it, putting the pieces back from the
move itself (which records the piece moved and the piece captured). Make followed by unmake leaves
the position exactly as it was, keys included; the search, which makes and unmakes millions of
moves a second on one position, relies on that, and a test recomputes the keys from scratch after
every move of random games to prove it.

The en passant square is recorded only when an enemy pawn stands ready to capture there. A double
push nobody can answer then leaves the same key as any other way of reaching the position, which
repetition detection needs.

## Attack tables

`Attacks` answers "which squares does a piece on this square attack?" for every piece.

- **Knights and kings** attack the same squares whatever else is on the board, so each has a
  64-entry table built at start-up by stepping in each direction and keeping the steps that stay
  on the board.
- **Pawns** attack the two squares diagonally forward: a table per color. Read the other way, the
  same table says where a pawn would have to stand to attack a square: a white pawn attacks e4
  from d3 or f3, which are exactly the squares a *black* pawn on e4 attacks.
- **Bishops, rooks and queens** slide until they hit something, so their attacks depend on the
  occupancy. Walking each ray square by square (`BishopAttacksByWalking`) is simple and too slow
  to do millions of times a second, so the answers for every occupancy are worked out once and
  looked up with **magic bitboards**. Only the squares on the piece's rays matter, and not the
  last square of each ray: a rook on a1 cares about 12 squares (a2-a7 and b1-g1), which can be
  occupied in 2^12 = 4,096 ways. Multiplying those 12 bits, masked out of the occupancy, by a
  suitable 64-bit "magic" number gathers them into the top 12 bits of the product, and shifting
  down leaves an index from 0 to 4,095 into the rook-on-a1 slice of one big table:

  ```
  index = ((occupied & mask) * magic) >> (64 - 12)
  ```

  A number is magic if no two occupancies with different attacks land on the same index. Nobody
  works them out: `Attacks` tries random numbers with few bits set until one works for each
  square, about 30 ms at start-up, with fixed seeds so that every run finds the same ones. Which
  magic is found does not matter: every valid one gives exactly the same attack sets. A queen's
  attacks are a bishop's and a rook's together. See https://www.chessprogramming.org/Magic_Bitboards
  and https://www.chessprogramming.org/Looking_for_Magics
- **Lines between squares**: `Between(a, b)` is the squares strictly between two squares on a
  rank, file or diagonal; `Line(a, b)` is the whole line through them, edge to edge. The move
  generator uses both for checks and pins.

## Legal move generation

The rule: **a move is legal if it does not leave the mover's own king attacked.** The plain way
to apply it generates every move the pieces could make (the *pseudo-legal* moves), plays each,
asks whether the king is attacked, and takes it back. That is how the reference generator in
`tests/.../Core/MoveGenerationTests.cs` works, and it is the clearest statement of the rule; the
engine's generator is checked against it on thousands of positions.

`Position.Generate` works out instead, once and before generating anything, every way a move
could break the rule, and generates only moves that avoid them all. It is the same rule, spelled
out in three parts:

1. **The king must not step onto a square the enemy attacks.** The attack map is made with our
   king lifted off the board: a rook checking the king along a rank also attacks the square
   behind the king, and the king cannot escape by stepping back along the line.
2. **In check, every other move must capture the checker or block its line.** The *check mask*
   is the squares a non-king move may land on: every square when not in check, the checker and
   the squares between it and the king in check, and none in double check (no single move can
   deal with two checkers at once), when only the king moves.
3. **A pinned piece must stay on its pin line.** An enemy bishop, rook or queen on a line through
   our king with exactly one piece between them, that piece ours, pins it: moving off the line
   would expose the king. A pinned piece's targets are cut to `Line(king, piece)`, which also
   lets it capture the pinner.

So a knight, bishop, rook or queen's moves are its attack set, cut to the check mask, cut to its
pin line if it is pinned, and split into captures (enemy squares) and quiet moves (empty squares).

Pawns are generated for the whole set at once: shifting the pawn bitboard one rank forward gives
every push, two ranks (from the third rank, through an empty square) every double push, and one
diagonal step every capture in that direction. Each target is mapped back to its pawn by the same
step. The check mask cuts the targets as for any piece. Pins are applied to the pawn set before the
shift: a pawn pinned along its king's file may still push, a pawn pinned along a diagonal may still
capture along that diagonal (which can only mean taking the pinner), and a pawn pinned along the
rank cannot move.

**En passant** has one case of its own that the three rules do not cover. The capture takes two
pawns off the capturing pawn's rank at once. If our king stands on that rank with an enemy rook or
queen beyond the two pawns, the capture opens the line between them, yet neither pawn was pinned on
its own. The generator tests that one case directly. The check mask applies to the captured pawn:
in check, en passant is legal only if the pawn being taken is the checker.

**Castling** is generated only when the right is intact, the king and rook stand on their home
squares, the squares between them are empty, the king is not in check, and neither square the king
crosses or lands on is attacked.

`GenerateLegalCaptures` generates only captures and promotions, for the quiescence search.
Generation order is a fixed function of the position (pieces by type, squares from a1 up), so the
search is deterministic and the bench node count is a signature of it.

See https://www.chessprogramming.org/Move_Generation and https://www.chessprogramming.org/Pin

## How it is tested

- **Perft** counts the leaves of the legal move tree to a fixed depth for 128 standard positions
  whose counts are known; any wrong move, missing or extra, changes a count. `stoopid-chess perft 3`
  runs them. See https://www.chessprogramming.org/Perft
- **The reference generator**: the plain make, test and unmake generator, cross-checked against the
  engine's on every perft position and its children and through random games, with the en passant,
  pin and double-check special cases pinned by name.
- **The key oracle**: `ComputeKeys` rebuilds the keys from nothing and `IsConsistent` checks the
  bitboards against the square array after every move of random games.
- **The attack tables** are checked against ray walking for every square and many occupancies.

## Why this shape

The legal generator and magic sliders are what modern engines use, and they are measurably faster
here than the plainer alternatives, measured with the same moves in the same order so that the
search was identical (Legion, 2026-11-10, `DOTNET_TieredCompilation=0`):

| Variant | Search nps | Perft |
|---|---|---|
| This design | 3.24M nps | 232M nps |
| Pseudo-legal moves, each made and tested for check | -30% | 7.8 times slower |
| Classical sliders (ray tables cut at the first blocker) instead of magics | -16% | -12% |

## Appendix: how we got here

Until September 2026 the board was 0x88, and legality was the make, test and unmake filter. The
bitboard board and legal generator replaced both in one change, gated by perft, the key oracle and
eighteen evaluation scores frozen beforehand, modelled on pawnstar (`src/position.h`, `pins.h`,
`attacks.h`). The evaluation was bit-for-bit the same, so the only signature change came from the
Zobrist keys and generation order.

| Measure | 0x88 | Bitboards | Change |
|---|---|---|---|
| perft, 128 positions, depths under 1M nodes | 19.1M nps (1,624 ms) | 81.2M nps (381 ms) | 4.3x |
| perft, every listed depth (651M nodes) | not measured | 190M nps (3.4 s) | |
| bench depth 6 (short run, cold JIT) | 1.42M nps | 2.05M nps | 1.45x |
| 3 s search, start position | depth 13, 2.09M nps | depth 14, 3.80M nps | 1.8x |
| 3 s search, kiwipete | depth 10, 1.89M nps | depth 11, 3.92M nps | 2.1x |

Self-play against the 0x88 build (60 games, 10+0.1, single thread, six-ply openings): 31 wins, 15
losses, 14 draws, **+95 ± 70 Elo**. The evaluation and search were identical, so the whole gain is
the depth bought by speed, about one ply at that time control.

A follow-up replaced the raw `int` squares, `ulong` sets and `List<Move>` with the `Square`,
`Bitboard` and `MoveList` structs, with the bench signature identical and the speed unchanged or
better: perft over every depth went from 211M to 258M nps, because `MoveList` holds its moves
inline instead of allocating a list per node. The colored-piece type went next: with a bitboard per
color, the mailbox holds the piece type alone and make and unmake take the color from the move.

In November 2026 the generator was restated as the three rules above, with pins applied to the
pawn sets instead of being checked target by target, and the 128 magic numbers copied from
pawnstar gave way to the search that finds them, both with the same moves in the same order and the
same speed per node (docs/SEARCH.md has the figures). `Position` was split into its two files, and
board editing, which the GUI needs and the engine does not, moved out to Play's `BoardSetup`.
