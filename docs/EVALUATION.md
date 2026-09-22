# Evaluation experiments (September 2026)

The evaluation was reset to Tomasz Michniewski's
[Simplified Evaluation Function](https://www.chessprogramming.org/Simplified_Evaluation_Function)
to form a baseline that every later term is measured against: material 100/320/330/500/900, his
piece-square tables, and his endgame rule (no queens, or each side with a queen has at most one
minor piece and nothing else) selecting the king table. The previous evaluator's extras (pawn
structure with a pawn hash, bishop mobility, material 330/370 for the minors) went with the reset.

Method, the same as for the search: one term per commit, judged by `tools/selfplay.ps1` against
the build before it (80 games at 10+0.1, single thread, six-ply openings, `fastchess`), kept only
on a positive result, recorded here either way. The bench signature changes with every evaluation
change and is updated in `BenchTests`, README.md and CLAUDE.md each time.

## Log

| Change | Self-play vs previous | Kept |
|---|---|---|
| Reset to the Simplified Evaluation Function (baseline) | +13 ± 68 against the previous evaluation (pawn structure, bishop mobility, 330/370 minors): those terms were worth nothing | baseline |
| Mobility: squares each knight, bishop, rook and queen can move to or capture on, at 4, 3, 2 and 1 cp per square | +108 ± 63 (41 W, 17 L, 22 D) | yes |
| King safety: pawn shield, 10 cp per own pawn one rank ahead of the king on its three files and 5 cp two ranks ahead, middlegame only | -4 ± 52 (27 W, 28 L, 25 D) | no |
| King safety: enemy piece attacks into the king zone (king square and neighbours), 5 cp per attacked square for knights, bishops and rooks, 10 for queens, middlegame only | +30 ± 59 (31 W, 24 L, 25 D) | yes |
| Passed pawns: no enemy pawn ahead on the pawn's file or the two beside it, bonus by rank 5/10/20/35/60/100 from the second to the seventh | +61 ± 62 (39 W, 25 L, 16 D) | yes |
| Rooks on open files (15 cp) and semi-open files (8 cp, no own pawn) | -4 ± 59 (31 W, 32 L, 17 D) | no |
| Bishop pair, 30 cp for a side with two bishops | +57 ± 58 (36 W, 23 L, 21 D) | yes |
| King shelter and pawn storm on the three files around the king, middlegame only: nearest own pawn ahead scores by rank (+25 unmoved, +15, +5, none -30), nearest enemy pawn ahead costs by distance (-30 at two ranks, -20, -10; halved when blocked by an own pawn) | +30 ± 53 (32 W, 25 L, 23 D) | yes |
| Isolated pawns (-15 each) and doubled pawns (-10 per extra pawn on a file) | -39 ± 61 (27 W, 36 L, 17 D) | no |
| Tempo bonus, 10 cp for the side to move | +30 ± 60 (32 W, 25 L, 23 D) | yes |
| Knight outposts: a knight on ranks 4 to 6 defended by an own pawn where no enemy pawn can ever attack it, +20 | +44 ± 55 (31 W, 21 L, 28 D) | yes |
| Passed pawn bonus doubled in the endgame | -4 ± 55 (28 W, 29 L, 23 D) | no |
| Tapered evaluation: king tables interpolated and king safety scaled by a 0-24 material phase instead of the binary endgame rule | -70 ± 58 (20 W, 36 L, 24 D) | no |
| Passed pawns in the endgame: 5 cp per square of enemy king distance to the square ahead, minus own king distance (from here on tests are 100 games at 12+0.12) | -14 ± 50 (34 W, 38 L, 28 D) | no |
| Pawn threats: 15 cp per enemy piece (not pawn or king) attacked by an own pawn | -17 ± 49 (34 W, 39 L, 27 D) | no |
| Safe mobility: squares attacked by enemy pawns no longer count | +38 ± 55 (41 W, 30 L, 29 D) | yes |
| King danger by attack units (N 2, B 2, R 3, Q 5 per attacked zone square) through the classic convex table, replacing the linear penalty | -14 ± 52 (35 W, 39 L, 26 D) | no |
| King zone attack penalty halved when the attacker has no queen | -14 ± 49 (32 W, 36 L, 32 D) | no |
| Mobility by per-piece tables with diminishing returns instead of a linear rate (knight -32..26 over 0-8 squares, bishop -30..36, rook -20..22, queen -15..15) | -3 ± 58 (38 W, 39 L, 23 D) | no |
| Weak king zone squares: 15 cp per zone square the enemy attacks that no own piece or pawn other than the king defends, middlegame only, on top of the linear attack count | +49 ± 55 (44 W, 30 L, 26 D) | yes |
| Safe checks: a penalty per square from which an enemy knight (25), bishop (20), rook (30) or queen (40) could give check with nothing but the king to take it | -7 ± 64 (36 W, 38 L, 26 D) | no |
| Safe major-piece mobility: rooks skip squares attacked by enemy minors, queens also those attacked by enemy rooks (about 10% slower) | -53 ± 58 (31 W, 46 L, 23 D) | no |
| Weak king zone square penalty raised from 15 to 25 cp | -14 ± 57 (33 W, 37 L, 30 D) | no |
| King zone extended one rank forward: the three squares beyond the king's neighbours count for the attack and weak square penalties too | +53 ± 63 (44 W, 29 L, 27 D) | yes |
| Mobility area includes squares holding own pieces other than pawns and the king (defending a piece counts as a square) | -14 ± 50 (31 W, 35 L, 34 D) | no |
| King attacker count: 4 cp times the square of the number of enemy pieces attacking the zone, on top of the linear count | -10 ± 52 (34 W, 37 L, 29 D) | no |
| Connected pawns: a pawn defended by a pawn or beside one scores by rank 5/7/12/20/40 from the third to the seventh | +17 ± 47 then -17 ± 56, 0 ± 36 over 200 games (76 W, 76 L, 48 D) | no |
| Piece threats: 25 cp per minor piece attack on an enemy rook or queen, 20 per rook attack on the queen | -56 ± 61 (28 W, 44 L, 28 D) | no |
| King zone attack penalties raised from 5 and 10 to 7 and 14 cp | -42 ± 58 (32 W, 44 L, 24 D) | no |
| King zone attack penalties lowered from 5 and 10 to 4 and 8 cp, the extended zone having enlarged the linear term | +21 ± 43 then +21 ± 50, +21 ± 33 over 200 games (76 W, 64 L, 60 D) | yes |
| One attack pass per side serving mobility, king zone pressure and weak squares (refactor, every score identical) | about 2% faster at bench depth 9, signature unchanged | yes |
| Passed pawn races, Crafty'''s rule of the square: with no enemy pieces, a passer the king cannot catch counts 525 + 10 per rank for the side that queens first (the search already sees such races at this time control) | -17 ± 54 (30 W, 35 L, 35 D) | no |
| Passed pawns in the endgame, after Crafty: the rank bonus times (twice the enemy king'''s distance from the square in front minus our king'''s) over 8 | +28 ± 49 then +49 ± 55, +38 ± 36 over 200 games (83 W, 61 L, 56 D) | yes |
| Free-path passers, after Crafty: the rank bonus again when no square ahead of the passer is attacked, half when only the next square is safe | -28 ± 53 (28 W, 36 L, 36 D) | no |
| Drawish endings after Crafty'''s winning chances: with no pawns a lone minor or a lead of at most a minor against a king off the edge scores one eighth, a pure opposite-coloured-bishop ending half | -28 ± 57 (34 W, 42 L, 24 D) | no |
| Candidate passers after Crafty: no enemy pawn ahead on the file and at least as many own pawns beside or behind as enemy pawns guarding the files ahead, half the passer bonus | -7 ± 61 (38 W, 40 L, 22 D) | no |
| Passed pawn king distance divisor 6 instead of 8 | -21 ± 49 (31 W, 37 L, 32 D) | no |
| Endgame king activity: 6 cp per square of the king'''s distance beyond one from the nearest pawn of either colour | -42 ± 51 (24 W, 36 L, 40 D) | no |
| Tempo bonus 20 cp instead of 10 | -38 ± 54 (30 W, 41 L, 29 D) | no |
| Mobility rates 5/4/3/1 cp per square instead of 4/3/2/1 | +67 ± 59 (48 W, 29 L, 23 D) | yes |
| Mobility rates 6/5/4/2 | +14 ± 42 then +53 ± 47, +33 ± 31 over 200 games (74 W, 55 L, 71 D) | yes |
| Mobility rates 7/6/5/2 | +21 ± 46 then -17 ± 44, +2 ± 33 over 200 games (72 W, 71 L, 57 D) | no |
| Passed pawn table scaled by 1.5 (8/15/30/52/90/150) | -31 ± 49 (27 W, 36 L, 37 D) | no |
| Passed pawn table scaled by 0.75 (4/8/15/26/45/75) | -24 ± 46 (28 W, 35 L, 37 D) | no |
| Bishop pair 45 cp instead of 30 | +38 ± 49 (37 W, 26 L, 37 D) | yes |
| Bishop pair 60 cp | -35 ± 47 (30 W, 40 L, 30 D) | no |
| Knight outpost 30 cp instead of 20 | 0 ± 42 (31 W, 31 L, 38 D) | no |
| Weak king square penalty 10 cp instead of 15 | +21 ± 53 then +7 ± 51, +14 ± 37 over 200 games (70 W, 62 L, 68 D) | no |
| Shelter and storm tables scaled by 1.5 | -42 ± 54 (27 W, 39 L, 34 D) | no |
| Shelter and storm tables scaled by two thirds (shelter 17/10/3, missing -20, storm 20/13/7) | +31 ± 54 (39 W, 30 L, 31 D) | yes |
| Shelter and storm tables at half the original values (12/8/3, -15, 15/10/5) | -21 ± 43 (24 W, 30 L, 46 D) | no |
| King zone attack penalties 3 and 6 cp | -3 ± 51 (31 W, 32 L, 37 D) | no |
| Evaluation cache: 65,536 entries of 48-bit key and 16-bit score, always replaced, cleared on a new game (exact, every score identical) | about 10% faster at bench depth 10 with JIT tiering off; 512 KB measured best of 128 KB to 8 MB | yes |
| King tropism after Crafty: knight 6/6/6/4/2, bishop 4/4/4/2, rook 8/8/6/4/2/2/2, queen 12/12/10/8/6/4/4 cp by distance from the enemy king, middlegame only | -31 ± 51 (28 W, 37 L, 35 D) | no |
| Bad bishop after Crafty: 4 cp per own pawn on the bishop'''s colour | -31 ± 55 (31 W, 40 L, 29 D) | no |
| Queen 950 instead of 900 | +49 ± 44 (38 W, 24 L, 38 D) | yes |
| Queen 1000 | +45 ± 44 (39 W, 26 L, 35 D) | yes |
| Queen 1050 | +10 ± 50 (34 W, 31 L, 35 D) | no |
| Rook 550 instead of 500 | +14 ± 53 then -14 ± 46, 0 ± 35 over 200 games (69 W, 69 L, 62 D) | no |
| Knight 300 and bishop 310 instead of 320 and 330 | -24 ± 53 (31 W, 38 L, 31 D) | no |
| Knight 340 and bishop 350 | +17 ± 47 then -38 ± 51, -10 ± 35 over 200 games (63 W, 69 L, 68 D) | no |
| Knight, bishop, rook and queen piece-square tables at half strength | +7 ± 54 (41 W, 39 L, 20 D) | no |
| Rook on the seventh rank after Crafty: 25 cp with the enemy king on the eighth or enemy pawns on the seventh | -31 ± 43 (30 W, 39 L, 31 D) | no |
| Blocked passers: a passed pawn with the square in front occupied is worth half | +53 ± 52 (40 W, 25 L, 35 D) | yes |
| Protected passers: a passer defended by an own pawn worth half again | -3 ± 49 (31 W, 32 L, 37 D) | no |
| Blocked passers worth a third instead of half | -10 ± 44 (33 W, 36 L, 31 D) | no |
| Connected passers: a passer with another passer on an adjacent file worth half again | -21 ± 57 (32 W, 38 L, 30 D) | no |
| King tables blended by a 0-24 material phase instead of switched by the endgame rule (everything else unchanged) | -45 ± 60 (29 W, 42 L, 29 D) | no |
| SPSA retune of all 27 parameters (tools/spsa.py, 500 iterations of 8 games at 4+0.04, run of 2026-09-19/20, archived in tools/spsa-runs/2026-09-20-complete): knight 325, bishop 320, rook 510, queen 1002, mobility 5/6/6/2, outpost 26, shelter 15/12/0 and missing -16, storm 19/10/7, passers 5/9/19/39/73/115 | +49 ± 52 (40 W, 26 L, 34 D) | yes |
| Second SPSA retune at the test control (500 iterations of 8 games at 12+0.12, started from the kept values, archived in tools/spsa-runs/2026-09-20-tc12): knight 335, bishop 343, queen 1004, rook mobility 7, king zone 6/10, bishop pair 35, tempo 7, shelter 16/15/4 and missing -15, storm 19/11/6, passers 2/12/18/42/75/137 | -21 ± 44 (32 W, 38 L, 30 D) | no |
| Pawnless drawishness after classical Stockfish's material rules: a side ahead with no pawns and at most a bishop of extra material scores a sixteenth (under a rook of own material) or a quarter; two knights against a bare king score a sixteenth. Bench signature unchanged | +35 ± 46 (36 W, 26 L, 38 D) | yes |
| Opposite-colored bishop scaling (Stockfish's 31/64 with only the bishops, 9/64 on a single pawn, 46/64 with more pieces) | -10 ± 49 (27 W, 30 L, 43 D) | no |
| Mop-up against a bare king (Stockfish's KXK PushToEdges and PushClose added when the winner has a rook's material, two knights excepted) | -3 ± 53 (36 W, 37 L, 27 D); no mechanism case either: the proven-mate stop rule already has fifty-move draws at zero in these matches | no |
| KPK bitbase in the evaluation (a retrograde solver of all 196,608 king-and-pawn-versus-king arrangements, verified against textbook positions and 39 pawnstar-adjudicated random positions, all agreeing; parked on the kpk-bitbase branch). Three wirings, all negative: exact values (drawn 0, won 750 + 30 per rank) -45 ± 46; the ordinary score plus 300 when won and 0 when drawn -38 ± 51; the ordinary score with drawn arrangements at a sixteenth -17 ± 51. Correct knowledge, no Elo: the search already plays KPK well enough here, and the overrides only disturb its conversion gradients | -45, -38, -17 | no |
| Third SPSA retune, overnight at the test control with twice the games per iteration (400 iterations of 16 games at 12+0.12, archived in tools/spsa-runs/2026-09-22-tc12-g16): knight 322, bishop 321, rook 508, queen 996, mobility 6/6/7/3, bishop pair 44, tempo 11, shelter 14/12/2 and missing -15, passers 4/9/17/36/72/127. Mild drifts only — the doubled gradient signal held it near the defaults, unlike the 8-game run | +21 ± 43 then -14 ± 43, +3 ± 30 over the 200 (62 W, 60 L, 78 D) | no |
