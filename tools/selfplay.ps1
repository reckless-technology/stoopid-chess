<#
.SYNOPSIS
    Self-play match of the current engine against a saved baseline build.

.DESCRIPTION
    Snapshot a baseline first with:  dotnet publish src/StoopidChess.Uci -c Release -o C:\Users\jonny\tools\stoopid-baseline
    Then after a search or evaluation change:  tools\selfplay.ps1 -Rounds 30
    The result tells you whether the change gained strength; the unit tests cannot.
#>
param(
    [string] $Baseline = "C:\Users\jonny\tools\stoopid-baseline\stoopid-chess.exe",
    [string] $TimeControl = "10+0.1",
    [int] $Rounds = 30,
    [int] $Concurrency = 4,
    [string] $PgnOut = "selfplay.pgn"
)
& (Join-Path $PSScriptRoot "match.ps1") -Opponent $Baseline -TimeControl $TimeControl -Rounds $Rounds -Concurrency $Concurrency -PgnOut $PgnOut
