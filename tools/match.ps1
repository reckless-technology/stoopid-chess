<#
.SYNOPSIS
    Play a fastchess match between the Stoopid Chess engine and another UCI engine.

.DESCRIPTION
    Builds the engine in Release, then runs paired games (each opening played with both colours)
    from an openings file. Both engines are deterministic, so an openings file is essential.

.EXAMPLE
    tools\match.ps1 -Opponent C:\Users\jonny\tools\pawnstar\pawnstar-0.13.593-windows-x86_64.exe `
        -OpponentOptions "option.Threads=1","option.OwnBook=false" -Rounds 20 -TimeControl 10+0.1
#>
param(
    [Parameter(Mandatory)] [string] $Opponent,
    [string[]] $OpponentOptions = @(),
    [string] $Fastchess = "C:\Users\jonny\tools\fastchess\fastchess-windows-x86-64\fastchess.exe",
    [string] $Openings = "C:\Users\jonny\tools\openings-6ply.epd",
    [string] $TimeControl = "10+0.1",
    [int] $Rounds = 20,
    [int] $Concurrency = 8,
    [string] $PgnOut = "match.pgn"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source ?? "C:\Program Files\dotnet\dotnet.exe"
& $dotnet build (Join-Path $repo "src\StoopidChess.Uci\StoopidChess.Uci.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "engine build failed" }
$engine = Join-Path $repo "src\StoopidChess.Uci\bin\Release\net10.0\stoopid-chess.exe"
$opponentName = [IO.Path]::GetFileNameWithoutExtension($Opponent)

& $Fastchess `
    -engine "cmd=$engine" name=StoopidChess `
    -engine "cmd=$Opponent" "name=$opponentName" @OpponentOptions `
    -each "tc=$TimeControl" -rounds $Rounds -games 2 -repeat -concurrency $Concurrency `
    -openings "file=$Openings" format=epd order=random `
    -pgnout "file=$PgnOut" -ratinginterval 10
