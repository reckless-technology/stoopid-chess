# Search techniques: what was tried and what it measured

The search's counterpart of [EVALUATION.md](EVALUATION.md). `Engine/Searcher.cs` opens with a numbered map of the
techniques it uses, in the order `AlphaBeta` applies them; this file is the record behind that map: every technique
and setting that was tried, what self-play said about it, and whether it stayed.

## How a result was measured

One change per commit, played against the build before it with fastchess from the repo's 200 six-ply openings, single
threaded and with no book (`dotnet run tools/match.cs -- --baseline`). The early results are 40-60 games at 10+0.1;
from the null-move reduction change on, the test setting is 100 games at 12+0.12. A result is Elo with its 95% error
bar, then wins, losses and draws where they were recorded. With 100 games the bar is about ±50, so anything under about
+40 is weakly evidenced: its bar includes zero. A 100-game result between about +15 and +25 is neither kept nor
rejected; a second 100 games are played and the change is judged on the 200.

Changes that leave the play identical (the bench signature unchanged) are judged on speed instead: nodes per second at
bench depth 13 or more with `DOTNET_TieredCompilation=0`, runs interleaved, medians.

At the start pawnstar 0.13.593 beat both the original search and the improved one 40-0.

## Kept

| Technique | Chess Programming Wiki | Result |
|---|---|---|
| Null-move pruning | [Null Move Pruning](https://www.chessprogramming.org/Null_Move_Pruning) | +199 ± 90 |
| History heuristic | [History Heuristic](https://www.chessprogramming.org/History_Heuristic) | +41 ± 67 |
| Late move reductions | [Late Move Reductions](https://www.chessprogramming.org/Late_Move_Reductions) | +101 ± 68 |
| Transposition table in quiescence | [Quiescence Search](https://www.chessprogramming.org/Quiescence_Search) | +41 ± 61 |
| Principal variation search | [Principal Variation Search](https://www.chessprogramming.org/Principal_Variation_Search) | +58 ± 78 |
| Aspiration windows, on top of PVS (alone they measured -35 ± 76) | [Aspiration Windows](https://www.chessprogramming.org/Aspiration_Windows) | +64 ± 75 |
| Bitboards with a legal move generator (a pure speed change) | [Bitboards](https://www.chessprogramming.org/Bitboards) | +95 ± 70 |
| Reverse futility pruning (80 games) | [Reverse Futility Pruning](https://www.chessprogramming.org/Reverse_Futility_Pruning) | +122 ± 50 |
| Late move pruning at depth <= 4 after the (5 + depth²)th quiet move (80 games) | [Futility Pruning: Move Count Based Pruning](https://www.chessprogramming.org/Futility_Pruning#MoveCountBasedPruning) | +30 ± 61 |
| Countermove heuristic: the quiet reply that last refuted the opponent's move, ordered after the killers | [Countermove Heuristic](https://www.chessprogramming.org/Countermove_Heuristic) | +103 ± 65 |
| One-ply continuation history added to the quiet-move history score | [History Heuristic](https://www.chessprogramming.org/History_Heuristic) | +30 ± 56 |
| Static exchange evaluation pruning of losing captures in quiescence | [Static Exchange Evaluation](https://www.chessprogramming.org/Static_Exchange_Evaluation) | +30 ± 55 |
| Null-move reduction 3 + depth/4 instead of 2 + depth/6 (100 games at 12+0.12, the test setting from here on) | [Null Move Pruning](https://www.chessprogramming.org/Null_Move_Pruning) | +45 ± 50 |
| Internal iterative reduction: one ply less at depth >= 4 without a table move | [Internal Iterative Reductions](https://www.chessprogramming.org/Internal_Iterative_Reductions) | +21 ± 44 then +24 ± 49; +23 ± 33 over the 200 (74 W, 61 L, 65 D) |
| Stopping iterative deepening only on a mate proven within the searched depth, not on any mate score | [Iterative Deepening](https://www.chessprogramming.org/Iterative_Deepening) | -38 ± 54 then -24 ± 52: kept on the mechanism (below) |
| Half as much time again, within 80% of the clock, the first time the root score falls 30 cp or more between iterations, from depth 4 | [Time Management](https://www.chessprogramming.org/Time_Management) | +78 ± 48 (45 W, 23 L, 32 D) |
| The same extension when the best move changes, from depth 6 | [Time Management](https://www.chessprogramming.org/Time_Management) | +45 ± 46 (43 W, 30 L, 27 D) |
| Sharing the clock over 20 moves to go instead of 30 | [Time Management](https://www.chessprogramming.org/Time_Management) | +14 ± 63 then +24 ± 46; +19 ± 38 over the 200 (75 W, 64 L, 61 D) |
| An aspiration window of 20 cp instead of 40 | [Aspiration Windows](https://www.chessprogramming.org/Aspiration_Windows) | +14 ± 52 then +31 ± 53; +23 ± 37 over the 200 (80 W, 67 L, 53 D) |
| The countermove exempt from late move reductions and pruning, like the killers | [Countermove Heuristic](https://www.chessprogramming.org/Countermove_Heuristic) | +35 ± 50 (37 W, 27 L, 36 D) |
| A reverse futility margin of 60 cp per ply instead of 80 | [Reverse Futility Pruning](https://www.chessprogramming.org/Reverse_Futility_Pruning) | +38 ± 49 (40 W, 29 L, 31 D) |
| Null-move pruning from depth 2 instead of 3 | [Null Move Pruning](https://www.chessprogramming.org/Null_Move_Pruning) | +45 ± 51 (39 W, 26 L, 35 D) |
| Razoring: a non-PV node at depth <= 3 whose static evaluation is 250 cp per ply below alpha is cut to the quiescence score when that score also stays at or below alpha | [Razoring](https://www.chessprogramming.org/Razoring) | +31 ± 47 (36 W, 27 L, 37 D) |
| The table move searched before any move generation, trusted without a legality check (the full 64-bit key match makes a foreign move vanishingly unlikely, as in pawnstar): a table-move cutoff skips generation entirely, 8.5% fewer nodes and 10% less time to depth 16 | [Transposition Table](https://www.chessprogramming.org/Transposition_Table) | +45 ± 53 (37 W, 24 L, 39 D) |

The mate-stop rule was kept against its score because in all 200 games the side that saw a mate won, so the rule could
not have changed a result (the score is noise), while fifty-move-rule draws, the one thing it can change, fell from 21
in 500 games to 7 in 200. That exception to "kept only on a positive result" was reviewed and confirmed on
20 September 2026.

## Not kept

| Technique | Chess Programming Wiki | Result |
|---|---|---|
| Late move pruning as in pawnstar: after the (3 + depth²)th quiet move, to depth 8 | [Futility Pruning](https://www.chessprogramming.org/Futility_Pruning#MoveCountBasedPruning) | -22 ± 59 |
| A logarithmic LMR table, 0.75 + ln(depth) ln(move) / 2.25, one less in PV nodes | [Late Move Reductions](https://www.chessprogramming.org/Late_Move_Reductions) | -22 ± 57; retried on the newer ordering +17 ± 63 |
| Futility pruning of quiet moves at depth <= 3, 100 cp per ply | [Futility Pruning](https://www.chessprogramming.org/Futility_Pruning) | +4 ± 58 |
| Delta pruning in quiescence, 200 cp | [Delta Pruning](https://www.chessprogramming.org/Delta_Pruning) | -4 ± 54 |
| Ordering captures that lose by the exchange evaluation behind all quiet moves | [Move Ordering](https://www.chessprogramming.org/Move_Ordering) | -30 ± 68 |
| Main-search SEE pruning of captures losing more than 100 cp per ply, at depth <= 6 | [Static Exchange Evaluation](https://www.chessprogramming.org/Static_Exchange_Evaluation) | +17 ± 59 |
| A hard time limit of three times the soft budget, so that a started iteration can finish | [Time Management](https://www.chessprogramming.org/Time_Management) | +13 ± 60 |
| A history malus for the quiet moves searched before a cutoff | [History Heuristic](https://www.chessprogramming.org/History_Heuristic) | +13 ± 60 |
| An improving heuristic (static evaluation above two plies ago): one ply off the reverse futility margin and half the late move pruning count when not improving | [Improving](https://www.chessprogramming.org/Improving) | +10 ± 57 |
| The same on the reverse futility margin alone | [Improving](https://www.chessprogramming.org/Improving) | -67 ± 47 |
| A one-ply extension of every checking move | [Check Extensions](https://www.chessprogramming.org/Check_Extensions) | -3 ± 47 |
| One ply less LMR for quiet moves with any cutoff history | [Late Move Reductions](https://www.chessprogramming.org/Late_Move_Reductions) | -17 ± 47 |
| Stopping at 30% of the budget instead of 50% once the best move has stood for four iterations | [Time Management](https://www.chessprogramming.org/Time_Management) | +10 ± 50 |
| Doubling the budget on instability instead of adding half | [Time Management](https://www.chessprogramming.org/Time_Management) | 0 ± 47 |
| Sharing the clock over 40 moves instead of 30 | [Time Management](https://www.chessprogramming.org/Time_Management) | -60 ± 49 |
| Sharing the clock over 15 moves | [Time Management](https://www.chessprogramming.org/Time_Management) | -3 ± 56 |
| A second extension on renewed instability | [Time Management](https://www.chessprogramming.org/Time_Management) | -14 ± 58 |
| One more ply of null-move reduction per 200 cp of static evaluation above beta, at most three | [Null Move Pruning](https://www.chessprogramming.org/Null_Move_Pruning) | +3 ± 46 |
| An aspiration window of 12 cp | [Aspiration Windows](https://www.chessprogramming.org/Aspiration_Windows) | -7 ± 58 |
| A two-ply follow-up history (our own previous move, the moved piece and its square) added to the quiet-move score | [History Heuristic](https://www.chessprogramming.org/History_Heuristic) | 0 ± 52 |
| A reverse futility margin of 45 cp per ply | [Reverse Futility Pruning](https://www.chessprogramming.org/Reverse_Futility_Pruning) | 0 ± 52 |
| Late move pruning to depth 6 instead of 4 | [Futility Pruning](https://www.chessprogramming.org/Futility_Pruning#MoveCountBasedPruning) | -42 ± 58 |
| Reverse futility pruning to depth 9 instead of 7 | [Reverse Futility Pruning](https://www.chessprogramming.org/Reverse_Futility_Pruning) | 0 ± 49 |
| Late move reductions from the third move instead of the fourth | [Late Move Reductions](https://www.chessprogramming.org/Late_Move_Reductions) | -38 ± 47 |
| Late move reductions from the fifth move | [Late Move Reductions](https://www.chessprogramming.org/Late_Move_Reductions) | -7 ± 48 |
| Late move reductions from depth 2 | [Late Move Reductions](https://www.chessprogramming.org/Late_Move_Reductions) | +14 ± 52 then +14 ± 50; +14 ± 36 over the 200 |
| ProbCut: a capture beating beta + 200 in a search four plies shallower cuts a non-PV node, from depth 5 | [ProbCut](https://www.chessprogramming.org/ProbCut) | -24 ± 48 |
| Starting another iteration until 60% of the budget is used instead of 50% | [Time Management](https://www.chessprogramming.org/Time_Management) | 0 ± 41 |
| The whole increment in the budget instead of 80% | [Time Management](https://www.chessprogramming.org/Time_Management) | 0 ± 44 |
| Singular extensions: from depth 8, the table move searched a ply deeper when every other move fails 2 cp per ply below the table score at half depth (53% more nodes at depth 10) | [Singular Extensions](https://www.chessprogramming.org/Singular_Extensions) | +3 ± 50 |
| A capture history (cutoffs by piece, square and victim), an eighth of it added to the MVV/LVA order | [History Heuristic](https://www.chessprogramming.org/History_Heuristic) | -53 ± 50 |
| Halving the history tables between root iterations | [History Heuristic](https://www.chessprogramming.org/History_Heuristic) | -3 ± 45 |
| Root moves ordered by the previous iteration's subtree node counts, the table move first, from the second iteration on | [Move Ordering](https://www.chessprogramming.org/Move_Ordering) | -14 ± 52 |
| Razoring margins of 200 and 300 cp per ply | [Razoring](https://www.chessprogramming.org/Razoring) | -24 ± 56 and -24 ± 47 |
| Quiet checking moves at the first ply of quiescence, skipped when they lose material by the exchange evaluation (all legal quiets generated and filtered by precomputed check squares and discovered-check candidates): the extra generation at every quiescence entry cost more than the tactics it found | [Quiescence Search](https://www.chessprogramming.org/Quiescence_Search) | -28 ± 57 |

## Speed and structure, with identical play

| Change | Measured | Kept |
|---|---|---|
| A profile-guided speed pass: both transposition tiers in one array so a probe touches adjacent lines, aggressive inlining of `Opponent` and `KingSquare`, the history tables as flat arrays, a prefetch of the child's table bucket after every make | +8.6% nps at bench 16 | yes |
| The evaluation cache (Zobrist key to score) | about 10% faster | yes |
| Indexing the slider attack table by pext instead of the magic multiply, bounds checks removed (the `slider-speed` branch) | no change at bench 16 or perft: the dependent table load hides both the multiply and the bounds check | no |
| Legality by making every pseudo-legal move and testing for check, play identical (reviewer's prototype, 2026-11-10) | -30% search nps, perft 7.8 times slower | no |
| Classical ray sliders (eight ray tables cut at the first blocker) instead of magic bitboards (reviewer's prototype, 2026-11-10) | -16% search nps, -12% perft | no |
| The legal generator restated as its three rules, with pins applied to the pawn sets before shifting, and the 128 magic constants replaced by a seeded search when the tables are built | per node unchanged: bench 16 3.57M against 3.57M nps, perft 50 253M against 256M including the table build; the search adds about 27 ms to start-up | yes |
| `Checkers` worked out once per move and kept in the undo record instead of a lazy cache | bench 16 3.41M against 3.35M nps (with the rest of that change) | yes |
| The killers as one two-move record per ply instead of a flat array | bench 16 3.60M against 3.62M nps, bench 13 3.67M against 3.64M | yes |
| No prefetch of the child's table bucket | bench 16 3.55M against 3.62M nps (9 runs), 3.05M against 3.34M (5 runs) | no |

The 2026-11-10 figures are from the Legion, `DOTNET_TieredCompilation=0`, runs interleaved, medians of 5 to 9.

## Corrections that changed play slightly

| Change | Effect | Kept |
|---|---|---|
| The fifty-move rule as one check at the top of the main search and quiescence: in check it is decided before the table probe, so a stored score no longer stands in for the draw | bench unchanged | yes |
| No repetitions across a null move: the half-move clock restarts at zero, so a line of passes cannot reach a false repetition | bench 13 3,258,845 to 3,258,856 nodes; not matched | yes |
| The quiet history capped at 69,999, below the countermove's 70,000 (it was 79,999; after 10-second searches 2.0% of quiet scores reached the cap from the start position and 7.3% in an endgame) | bench 16 17,776,320 to 17,768,957 nodes; not matched | yes |
