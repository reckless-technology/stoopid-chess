<#
.SYNOPSIS
    Self-play match of the current engine against a saved baseline build.

.DESCRIPTION
    Snapshot a baseline first with:  dotnet publish src/StoopidChess.Uci -c Release -o C:\Users\jonny\tools\stoopid-baseline
    Then after a search or evaluation change:  tools\selfplay.ps1
    The result tells you whether the change gained strength; the unit tests cannot.
    The defaults are the standard test setting, 100 games (50 paired rounds) at 12+0.12.
#>
param(
    [string] $Baseline = "C:\Users\jonny\tools\stoopid-baseline\stoopid-chess.exe",
    [string] $TimeControl = "12+0.12",
    [int] $Rounds = 50,
    [int] $Concurrency = 8,
    [string] $PgnOut = "selfplay.pgn"
)
& (Join-Path $PSScriptRoot "match.ps1") -Opponent $Baseline -TimeControl $TimeControl -Rounds $Rounds -Concurrency $Concurrency -PgnOut $PgnOut
