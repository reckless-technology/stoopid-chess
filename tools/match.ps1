<#
.SYNOPSIS
    Play a fastchess match between the Stoopid Chess engine and another UCI engine.

.DESCRIPTION
    Builds the engine in Release, then runs paired games (each opening played with both colours)
    from an openings file. Both engines are deterministic, so an openings file is essential.
    fastchess defaults to the build tools\setup-dev.ps1 -Tuning installs under %USERPROFILE%\tools
    (tools\tuning-tools.txt), else the one on the PATH; the openings to the repo's own. Runs under
    Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    tools\match.ps1 -Opponent $env:USERPROFILE\tools\pawnstar\pawnstar-0.13.593-windows-x86_64.exe `
        -OpponentOptions "option.Threads=1","option.OwnBook=false" -Rounds 20 -TimeControl 10+0.1
    powershell -File tools\match.ps1 -Opponent <engine> -OpponentOptions "option.Threads=1,option.OwnBook=false"
#>
param(
    [Parameter(Mandatory)] [string] $Opponent,
    [string[]] $OpponentOptions = @(),
    [string] $Fastchess = "",
    [string] $Openings = "",
    [string] $TimeControl = "12+0.12",
    [int] $Rounds = 20,
    [int] $Concurrency = 8,
    [string] $PgnOut = "match.pgn"
)

# Continue, not Stop: Windows PowerShell 5.1 makes a native command's stderr a terminating error under Stop.
$ErrorActionPreference = "Continue"
$repo = Split-Path $PSScriptRoot -Parent
# "-File" hands an array parameter one argument, so the options may also come as one comma-separated string.
$OpponentOptions = @($OpponentOptions | ForEach-Object { $_ -split "," } | Where-Object { $_ })
if (-not $Openings) { $Openings = Join-Path $repo "tools\openings-6ply.epd" }
if (-not $Fastchess) {
    $platform = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "windows-arm64" } else { "windows-x64" }
    $pin = Get-Content (Join-Path $repo "tools\tuning-tools.txt") |
        ForEach-Object { , (-split $_) } | Where-Object { $_[0] -eq "fastchess" -and $_[2] -eq $platform } | Select-Object -First 1
    $Fastchess = Join-Path $env:USERPROFILE "tools\$($pin[3])"
    $onPath = Get-Command fastchess -ErrorAction SilentlyContinue
    if (-not (Test-Path $Fastchess) -and $onPath) { $Fastchess = $onPath.Source }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "no dotnet on the PATH: run tools\setup-dev.ps1" }
dotnet build (Join-Path $repo "src\StoopidChess.Uci\StoopidChess.Uci.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "engine build failed" }
$engine = Join-Path $repo "src\StoopidChess.Uci\bin\Release\net10.0\stoopid-chess.exe"
$opponentName = [IO.Path]::GetFileNameWithoutExtension($Opponent)

& $Fastchess `
    -engine "cmd=$engine" name=StoopidChess `
    -engine "cmd=$Opponent" "name=$opponentName" @OpponentOptions `
    -each "tc=$TimeControl" -rounds $Rounds -games 2 -repeat -concurrency $Concurrency `
    -openings "file=$Openings" format=epd order=random `
    -pgnout "file=$PgnOut" -ratinginterval 10
exit $LASTEXITCODE
