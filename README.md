# Stoopid Chess

A chess program written in C# with a Windows Forms front end. Originally written in 2009, now
targeting .NET 10.

The engine uses:

- 0x88 board representation with incremental Zobrist hashing
- Negamax search with alpha-beta pruning, iterative deepening and a quiescence search extension
- Two transposition tables (always-replace and replace-by-depth), killer move heuristics and
  MVV/LVA move ordering
- Piece-square table evaluation with a pawn structure hash

The GUI lets you play either colour against the computer, watch the computer play itself, edit
positions, undo moves, and save and load games.

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows Forms is only
  supported on Windows, so the project targets `net10.0-windows`)

## Building and running

```
dotnet build -c Release
dotnet run -c Release
```

Or open `StoopidChess.sln` in Visual Studio 2022 or later.

Warnings are treated as errors and nullable reference types are enabled.

## Saved games

Games are saved as JSON (`*.chess`). The file records the position at the start of the move
history, any pieces captured before that point and the moves made since; loading replays the
moves to rebuild the board. Files saved by pre-.NET versions used the .NET Framework binary
serializer and cannot be loaded.

## History

This project lived in the `StoopidChess` directory of the `reckless-technology/software`
repository until September 2026, when it was split out into its own repository with its history.
