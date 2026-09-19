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
