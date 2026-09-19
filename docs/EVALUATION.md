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
