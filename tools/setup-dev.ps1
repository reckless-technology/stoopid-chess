<#
.SYNOPSIS
  Set up a Windows machine to build, test, debug and measure Stoopid Chess: the .NET SDK, the tools and the editor.

.DESCRIPTION
  Each component is checked first and installed only when it is missing or not the version the repo wants, so the
  script is safe to run again at any time. Everything goes into the user's own profile and needs no administrator,
  except Git for Windows when it is missing, which winget installs behind a UAC prompt. tools/setup-dev.sh is the same
  for Linux and macOS. VS Code's "setup environment" and "check environment" tasks run the one for the platform.

  What it sets up:
    - Git for Windows, which the repo's git hooks run under
    - the .NET SDK of the TargetFramework in Directory.Build.props (net10.0: a 10.0 SDK). When no such SDK is on the
      PATH, Microsoft's dotnet-install script puts one in %LOCALAPPDATA%\Microsoft\dotnet, and DOTNET_ROOT and the
      PATH point at it
    - the repo's git hooks (core.hooksPath .githooks) and the VS Code extensions .vscode/extensions.json recommends
    - with -Tuning, what tools\match.ps1, tools\selfplay.ps1 and tools\spsa.py play with, under %USERPROFILE%\tools:
      fastchess and the reference pawnstar engine, the release binaries tools\tuning-tools.txt pins by version and
      SHA-256, and the self-play baseline (stoopid-baseline), published from this checkout when there is none. The
      openings they play from are the repo's own, tools\openings-6ply.epd.

.PARAMETER Check
  Report what is present and what is missing, install nothing, and exit 1 if anything is missing.

.PARAMETER NoPersist
  Do not add anything to the user's PATH or environment for good (only to this process's): for a test run in a scratch
  profile.

.PARAMETER Tuning
  Also set up the match and self-play tools.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\setup-dev.ps1           # set up
  powershell -ExecutionPolicy Bypass -File tools\setup-dev.ps1 -Tuning   # set up, with fastchess, pawnstar, a baseline
  powershell -ExecutionPolicy Bypass -File tools\setup-dev.ps1 -Check    # report only
#>
param([switch]$Check, [switch]$NoPersist, [switch]$Tuning)
# Continue, not Stop: Windows PowerShell 5.1 makes a native command's stderr a terminating error under Stop, even
# redirected. The cmdlets that must stop say so; native commands' exit codes are checked.
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
Set-Location (Split-Path -Parent $PSScriptRoot)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---------------------------------------------------------------------------------------------------------------------
# Helpers (the same in every reckless-technology repo's setup script).
$script:missing = 0
function Write-Ok([string]$name, [string]$detail) { Write-Host ("  ok       {0,-24} {1}" -f $name, $detail) -ForegroundColor Green }
function Write-Missing([string]$name, [string]$detail) {
  $script:missing++
  Write-Host ("  missing  {0,-24} {1}" -f $name, $detail) -ForegroundColor Yellow
}
function Write-Installing([string]$name, [string]$detail) { Write-Host ("  install  {0,-24} {1}" -f $name, $detail) -ForegroundColor Cyan }
function Write-Note([string]$name, [string]$detail) { Write-Host ("  note     {0,-24} {1}" -f $name, $detail) }

# Put a directory on this process's PATH and, unless -NoPersist, on the user's PATH for every new shell.
function Add-ToPath([string]$dir) {
  if (($env:PATH -split ';') -notcontains $dir) { $env:PATH = "$dir;$env:PATH" }
  if ($NoPersist -or $Check) { return }
  $user = [Environment]::GetEnvironmentVariable('Path', 'User')
  if ((($user -split ';') | Where-Object { $_ }) -notcontains $dir) {
    [Environment]::SetEnvironmentVariable('Path', (@($dir) + (($user -split ';') | Where-Object { $_ })) -join ';', 'User')
  }
}

# Set an environment variable for this process and, unless -NoPersist, for the user's every new process.
function Set-UserVariable([string]$name, [string]$value) {
  Set-Item -Path "Env:$name" -Value $value
  if ($NoPersist -or $Check) { return }
  if ([Environment]::GetEnvironmentVariable($name, 'User') -ne $value) { [Environment]::SetEnvironmentVariable($name, $value, 'User') }
}

# Download a file and refuse it unless its SHA-256 is the one given.
function Get-VerifiedFile([string]$url, [string]$sha256, [string]$out) {
  Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing -ErrorAction Stop
  $got = (Get-FileHash $out -Algorithm SHA256).Hash
  if ($got -ne $sha256.ToUpper()) { Remove-Item $out; throw "SHA-256 mismatch for $url (got $got, want $sha256)" }
}

function New-ScratchDir {
  $dir = Join-Path ([IO.Path]::GetTempPath()) ("setup-dev-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
  New-Item -ItemType Directory -Path $dir -ErrorAction Stop | Out-Null
  return $dir
}

function Set-GitHooks {
  if (-not (Test-Path '.githooks')) { return }
  $current = git config core.hooksPath
  if ($current -eq '.githooks') { Write-Ok 'git hooks' 'core.hooksPath .githooks'; return }
  if ($Check) { Write-Missing 'git hooks' 'core.hooksPath is not .githooks'; return }
  git config core.hooksPath .githooks
  Write-Installing 'git hooks' 'core.hooksPath .githooks'
}

# The extensions .vscode/extensions.json recommends, when VS Code's command line is on the PATH.
function Install-VSCodeExtensions {
  $code = Get-Command code -ErrorAction SilentlyContinue
  if (-not $code -or -not (Test-Path '.vscode/extensions.json')) { return }
  $json = (Get-Content '.vscode/extensions.json' -Raw) -replace '(?m)^\s*//.*$', '' | ConvertFrom-Json
  $installed = & $code.Source --list-extensions 2>$null
  foreach ($ext in $json.recommendations) {
    if ($installed -contains $ext) { Write-Ok 'VS Code extension' $ext; continue }
    if ($Check) { Write-Missing 'VS Code extension' $ext; continue }
    Write-Installing 'VS Code extension' $ext
    & $code.Source --install-extension $ext --force | Out-Null
  }
}

# ---------------------------------------------------------------------------------------------------------------------
Write-Host "stoopid-chess: $(if ($Check) { 'checking' } else { 'setting up' }) the Windows development environment"

$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) { Write-Ok 'Git for Windows' "$(git --version) ($($git.Source))" }
elseif ($Check) { Write-Missing 'Git for Windows' 'git, which the hooks run under' }
else {
  Write-Installing 'Git for Windows' 'through winget'
  winget install --id Git.Git -e --accept-package-agreements --accept-source-agreements
  Write-Host '  Git for Windows is installed: open a new terminal and run this script again.'
  exit 0
}

# The .NET SDK: the major version of Directory.Build.props' TargetFramework. A per-user install, when there is one with
# that SDK, goes first on the PATH; otherwise whatever dotnet the PATH has (the system-wide one) is asked.
$tfm = [regex]::Match((Get-Content 'Directory.Build.props' -Raw), '<TargetFramework>net(\d+)\.(\d+)</TargetFramework>')
if (-not $tfm.Success) { throw 'no <TargetFramework>netX.Y</TargetFramework> in Directory.Build.props' }
$channel = "$($tfm.Groups[1].Value).$($tfm.Groups[2].Value)"
$sdkMajor = $tfm.Groups[1].Value
$dotnetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'

# The newest SDK of the wanted major the dotnet on the PATH lists ("10.0.401 [C:\Program Files\dotnet\sdk]"), or none.
function Get-DotnetSdk {
  $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
  if (-not $dotnet) { return $null }
  $sdks = @(& $dotnet.Source --list-sdks 2>$null | Where-Object { $_ -match "^$sdkMajor\." })
  if ($sdks.Count -eq 0) { return $null }
  return "$($sdks[-1]) via $($dotnet.Source)"
}

if (Test-Path (Join-Path $dotnetRoot "sdk\$sdkMajor.*")) {
  Add-ToPath $dotnetRoot
  Set-UserVariable 'DOTNET_ROOT' $dotnetRoot
}
$sdk = Get-DotnetSdk
if ($sdk) { Write-Ok ".NET SDK $channel" $sdk }
elseif ($Check) { Write-Missing ".NET SDK $channel" "for net$channel (Directory.Build.props)" }
else {
  Write-Installing ".NET SDK $channel" "Microsoft's dotnet-install.ps1 into $dotnetRoot"
  $scratch = New-ScratchDir
  $installer = Join-Path $scratch 'dotnet-install.ps1'
  Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing -ErrorAction Stop
  # In a process of its own: the installer sets its own error preference and may exit.
  & (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File $installer -Channel $channel -InstallDir $dotnetRoot -NoPath
  if ($LASTEXITCODE -ne 0) { throw "dotnet-install.ps1 exited with $LASTEXITCODE" }
  Remove-Item -Recurse -Force $scratch
  Add-ToPath $dotnetRoot
  Set-UserVariable 'DOTNET_ROOT' $dotnetRoot
  $sdk = Get-DotnetSdk
  if (-not $sdk) { throw "dotnet-install.ps1 finished but no $channel SDK is listed" }
  Write-Ok ".NET SDK $channel" $sdk
  # The machine's PATH comes before the user's in every new shell, so a system-wide dotnet without the SDK would still
  # be the one found there.
  $systemDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
  if (-not $NoPersist -and (Test-Path $systemDotnet) -and -not (& $systemDotnet --list-sdks 2>$null | Where-Object { $_ -match "^$sdkMajor\." })) {
    Write-Note '.NET SDK' "$systemDotnet comes first on new shells' PATH and lacks a $channel SDK: winget install Microsoft.DotNet.SDK.$sdkMajor"
  }
}

Set-GitHooks
Install-VSCodeExtensions

# The match and self-play tools.
if ($Tuning) {
  $toolsDir = Join-Path $env:USERPROFILE 'tools'
  $platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'windows-arm64' } else { 'windows-x64' }
  $pins = Get-Content 'tools/tuning-tools.txt' -ErrorAction Stop | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' } | ForEach-Object {
    $f = -split $_
    [pscustomobject]@{ Tool = $f[0]; Version = $f[1]; Platform = $f[2]; Exe = $f[3]; File = $f[4]; Sha256 = $f[5]; Url = $f[6] }
  }
  foreach ($tool in 'fastchess', 'pawnstar') {
    $pin = $pins | Where-Object { $_.Tool -eq $tool -and $_.Platform -eq $platform } | Select-Object -First 1
    if (-not $pin) { Write-Note $tool "no release binary for ${platform}: build it and pass its path (tools\tuning-tools.txt)"; continue }
    $name = "$tool $($pin.Version)"
    $exe = Join-Path $toolsDir $pin.Exe
    $file = Join-Path $toolsDir $pin.File
    # In place when the executable is there and the download it came from is the pinned one.
    if ((Test-Path $exe) -and (Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash -eq $pin.Sha256.ToUpper()) { Write-Ok $name $exe; continue }
    if ($Check) { Write-Missing $name $exe; continue }
    Write-Installing $name $pin.Url
    New-Item -ItemType Directory -Force -Path (Split-Path $file) -ErrorAction Stop | Out-Null
    Get-VerifiedFile $pin.Url $pin.Sha256 $file
    if ($file -like '*.zip') { Expand-Archive -Path $file -DestinationPath (Split-Path $file) -Force -ErrorAction Stop }
    if (-not (Test-Path $exe)) { throw "$($pin.Url) did not provide $exe" }
  }

  $baseline = Join-Path $toolsDir 'stoopid-baseline'
  if (Test-Path (Join-Path $baseline 'stoopid-chess.exe')) { Write-Ok 'self-play baseline' $baseline }
  elseif ($Check) { Write-Missing 'self-play baseline' "$baseline (the 'publish baseline' task)" }
  elseif (-not (Get-DotnetSdk)) { Write-Missing 'self-play baseline' 'needs the .NET SDK' }
  else {
    Write-Installing 'self-play baseline' "published from this checkout into $baseline"
    dotnet publish src/StoopidChess.Uci -c Release -o $baseline | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish exited with $LASTEXITCODE" }
  }
}

if ($Check) {
  if ($script:missing) { Write-Host "$($script:missing) missing: run tools\setup-dev.ps1 (VS Code: the 'setup environment' task)."; exit 1 }
  Write-Host 'Everything is in place.'
}
else {
  Write-Host 'Done. Open a new terminal (or restart VS Code) for PATH changes to reach it, then: dotnet test tests/StoopidChess.Tests -c Release'
}
