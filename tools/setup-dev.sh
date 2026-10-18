#!/usr/bin/env bash
# Set up a Linux or macOS machine to build, test, debug and measure Stoopid Chess: the .NET SDK, the tools and the
# editor. Each component is checked first and installed only when it is missing, so the script is safe to run again at
# any time. The .NET SDK goes into the user's own profile (~/.dotnet); the system packages come from apt (Debian,
# Ubuntu, Mint: sudo asks for a password) or Xcode's command-line tools (macOS). tools/setup-dev.ps1 is the same for
# Windows. VS Code's "setup environment" and "check environment" tasks run the one for the platform.
#
# What it sets up:
#   - git and curl; on Linux the libraries .NET and the Avalonia GUI load (ICU, OpenSSL, zlib, fontconfig, X11)
#   - the .NET SDK of the TargetFramework in Directory.Build.props (net10.0: a 10.0 SDK); when no such SDK is on the
#     PATH, Microsoft's dotnet-install script puts one in ~/.dotnet, and DOTNET_ROOT and the PATH point at it
#   - the repo's git hooks (core.hooksPath .githooks) and the VS Code extensions .vscode/extensions.json recommends
#   - with --tuning, what tools/match.sh, tools/selfplay.sh and tools/spsa.py play with, under ~/tools: fastchess and
#     the reference pawnstar engine, the release binaries tools/tuning-tools.txt pins by version and SHA-256, and the
#     self-play baseline (stoopid-baseline), published from this checkout when there is none. The openings they play
#     from are the repo's own, tools/openings-6ply.epd.
#
#   tools/setup-dev.sh                # set up
#   tools/setup-dev.sh --tuning       # set up, with fastchess, pawnstar and a baseline
#   tools/setup-dev.sh --check        # report only; exit 1 if anything is missing
#   tools/setup-dev.sh --no-persist   # add nothing to the shell profile (a test run in a scratch HOME)
set -euo pipefail
cd "$(dirname "$0")/.."

check=0
persist=1
tuning=0
for arg in "$@"; do
    case "$arg" in
        --check) check=1 ;;
        --no-persist) persist=0 ;;
        --tuning) tuning=1 ;;
        -h | --help) sed -n '2,21p' "$0"; exit 0 ;;
        *) echo "unknown argument: $arg" >&2; exit 2 ;;
    esac
done

# ---------------------------------------------------------------------------------------------------------------------
# Helpers (the same in every reckless-technology repo's setup script).
missing=0
ok() { printf '  \033[32mok\033[0m       %-24s %s\n' "$1" "$2"; }
miss() { missing=$((missing + 1)); printf '  \033[33mmissing\033[0m  %-24s %s\n' "$1" "$2"; }
installing() { printf '  \033[36minstall\033[0m  %-24s %s\n' "$1" "$2"; }
note() { printf '  note     %-24s %s\n' "$1" "$2"; }
have() { command -v "$1" >/dev/null 2>&1; }
os=$(uname -s)

# Add a line to every profile a new shell reads (a login shell, bash, and zsh on macOS), unless --no-persist or --check.
persist_line() {
    [ "$persist" = 1 ] && [ "$check" = 0 ] || return 0
    local profiles=("$HOME/.profile" "$HOME/.bashrc")
    if [ "$os" = Darwin ]; then profiles+=("$HOME/.zprofile"); fi
    for f in "${profiles[@]}"; do
        grep -qxF "$1" "$f" 2>/dev/null || printf '\n%s\n' "$1" >>"$f"
    done
}

# Put a directory on this shell's PATH and, unless --no-persist, on every new shell's.
add_to_path() {
    case ":$PATH:" in *":$1:"*) ;; *) PATH="$1:$PATH"; export PATH ;; esac
    persist_line "export PATH=\"$1:\$PATH\""
}

# Set an environment variable for this shell and, unless --no-persist, for every new shell.
set_env() {
    export "$1=$2"
    persist_line "export $1=\"$2\""
}

sha256_of() { if have sha256sum; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }

# Download a file and refuse it unless its SHA-256 is the one given.
fetch_verified() {
    curl -fsSL "$1" -o "$3"
    local got
    got=$(sha256_of "$3")
    if [ "$got" != "$2" ]; then rm -f "$3"; echo "SHA-256 mismatch for $1 (got $got, want $2)" >&2; exit 1; fi
}

sudo_cmd() { if [ "$(id -u)" = 0 ]; then "$@"; else sudo "$@"; fi; }

# apt packages that are not installed yet, installed together (Debian, Ubuntu, Mint).
apt_packages() {
    local need=()
    for p in "$@"; do
        dpkg-query -W -f='${Status}' "$p" 2>/dev/null | grep -q 'install ok installed' || need+=("$p")
    done
    if [ ${#need[@]} = 0 ]; then ok 'apt packages' "$*"; return; fi
    if [ "$check" = 1 ]; then miss 'apt packages' "${need[*]}"; return; fi
    installing 'apt packages' "${need[*]}"
    sudo_cmd apt-get update -q
    sudo_cmd env DEBIAN_FRONTEND=noninteractive apt-get install -y -q "${need[@]}"
}

# macOS: Xcode's command-line tools (git, and the compilers).
macos_base() {
    if xcode-select -p >/dev/null 2>&1; then ok 'Xcode command-line tools' "$(xcode-select -p)"
    elif [ "$check" = 1 ]; then miss 'Xcode command-line tools' 'xcode-select --install'
    else
        installing 'Xcode command-line tools' 'a dialog opens: install, then run this script again'
        xcode-select --install || true
        exit 0
    fi
}

linux_base() {
    if have apt-get; then apt_packages "$@"
    else
        echo "  This is not a Debian-family Linux: install the equivalents of these packages, then run again: $*" >&2
        if [ "$check" = 1 ]; then missing=$((missing + 1)); fi
    fi
}

set_git_hooks() {
    [ -d .githooks ] || return 0
    if [ "$(git config core.hooksPath || true)" = .githooks ]; then ok 'git hooks' 'core.hooksPath .githooks'
    elif [ "$check" = 1 ]; then miss 'git hooks' 'core.hooksPath is not .githooks'
    else git config core.hooksPath .githooks; installing 'git hooks' 'core.hooksPath .githooks'; fi
}

# The extensions .vscode/extensions.json recommends, when VS Code's command line is on the PATH.
install_vscode_extensions() {
    have code && [ -f .vscode/extensions.json ] || return 0
    local installed
    installed=$(code --list-extensions 2>/dev/null || true)
    for ext in $(sed -e 's://.*$::' .vscode/extensions.json | tr -d '\n' | sed -e 's/.*"recommendations"[^[]*\[//' -e 's/\].*//' | tr -d '" ' | tr ',' ' '); do
        if grep -qixF "$ext" <<<"$installed"; then ok 'VS Code extension' "$ext"
        elif [ "$check" = 1 ]; then miss 'VS Code extension' "$ext"
        else installing 'VS Code extension' "$ext"; code --install-extension "$ext" --force >/dev/null; fi
    done
}

# ---------------------------------------------------------------------------------------------------------------------
echo "stoopid-chess: $([ "$check" = 1 ] && echo checking || echo 'setting up') the $os development environment"

case "$os" in
    Linux) linux_base ca-certificates curl git tar ;;
    Darwin) macos_base ;;
    *) echo "unsupported system: $os (tools/setup-dev.ps1 is the Windows script)" >&2; exit 2 ;;
esac

# The shared libraries .NET loads (ICU for globalisation, OpenSSL for NuGet's HTTPS, zlib) and the Avalonia GUI does
# (fontconfig, X11), looked for by file so the check holds whatever a distribution release names their packages; a
# missing one is installed through the package that provides it under a name every Debian-family release shares.
if [ "$os" = Linux ] && have apt-get; then
    ldconfig_cmd=$(command -v ldconfig || echo /sbin/ldconfig)
    libs=$("$ldconfig_cmd" -p 2>/dev/null || true)
    lacking=()
    for pair in libicuuc.so:libicu-dev libssl.so.3:libssl-dev libz.so.1:zlib1g libfontconfig.so.1:libfontconfig1 \
        libX11.so.6:libx11-6 libICE.so.6:libice6 libSM.so.6:libsm6; do
        # A match on the text, not a pipe into grep -q: under pipefail, grep leaving early fails the writer with SIGPIPE.
        case "$libs" in *"${pair%%:*}"*) ;; *) lacking+=("${pair#*:}") ;; esac
    done
    if [ ${#lacking[@]} = 0 ]; then ok 'runtime libraries' 'ICU, OpenSSL, zlib, fontconfig, X11'
    else apt_packages "${lacking[@]}"; fi
fi

# The .NET SDK: the major version of Directory.Build.props' TargetFramework. A per-user install, when there is one with
# that SDK, goes first on the PATH; otherwise whatever dotnet the PATH has (a system one) is asked.
channel=$(sed -n 's:.*<TargetFramework>net\([0-9]*\.[0-9]*\)</TargetFramework>.*:\1:p' Directory.Build.props | head -1)
[ -n "$channel" ] || { echo 'no <TargetFramework>netX.Y</TargetFramework> in Directory.Build.props' >&2; exit 1; }
sdk_major=${channel%%.*}
dotnet_root="$HOME/.dotnet"

# The newest SDK of the wanted major the dotnet on the PATH lists ("10.0.401 [/home/me/.dotnet/sdk]"), or nothing.
dotnet_sdk() {
    have dotnet || return 0
    local sdks
    sdks=$(dotnet --list-sdks 2>/dev/null | grep "^$sdk_major\." || true)
    if [ -n "$sdks" ]; then echo "$(printf '%s\n' "$sdks" | tail -1) via $(command -v dotnet)"; fi
}

if compgen -G "$dotnet_root/sdk/$sdk_major.*" >/dev/null; then
    add_to_path "$dotnet_root"
    set_env DOTNET_ROOT "$dotnet_root"
fi
sdk=$(dotnet_sdk)
if [ -n "$sdk" ]; then ok ".NET SDK $channel" "$sdk"
elif [ "$check" = 1 ]; then miss ".NET SDK $channel" "for net$channel (Directory.Build.props)"
else
    installing ".NET SDK $channel" "Microsoft's dotnet-install.sh into $dotnet_root"
    scratch=$(mktemp -d)
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$scratch/dotnet-install.sh"
    bash "$scratch/dotnet-install.sh" --channel "$channel" --install-dir "$dotnet_root" --no-path
    rm -rf "$scratch"
    add_to_path "$dotnet_root"
    set_env DOTNET_ROOT "$dotnet_root"
    sdk=$(dotnet_sdk)
    [ -n "$sdk" ] || { echo "dotnet-install.sh finished but no $channel SDK is listed" >&2; exit 1; }
    ok ".NET SDK $channel" "$sdk"
fi

set_git_hooks
install_vscode_extensions

# The match and self-play tools.
if [ "$tuning" = 1 ]; then
    tools_dir="$HOME/tools"
    case "$os-$(uname -m)" in
        Linux-x86_64) platform=linux-x64 ;;
        Linux-aarch64 | Linux-arm64) platform=linux-arm64 ;;
        Darwin-arm64) platform=macos-arm64 ;;
        Darwin-x86_64) platform=macos-x64 ;;
        *) platform="$os-$(uname -m)" ;;
    esac
    for tool in fastchess pawnstar; do
        pin=$(awk -v t="$tool" -v p="$platform" '$1 == t && $3 == p { print; exit }' tools/tuning-tools.txt)
        if [ -z "$pin" ]; then note "$tool" "no release binary for $platform: build it and pass its path (tools/tuning-tools.txt)"; continue; fi
        read -r _ version _ exe file sha url <<<"$pin"
        name="$tool $version"
        # In place when the executable is there and the download it came from is the pinned one.
        if [ -x "$tools_dir/$exe" ] && [ -f "$tools_dir/$file" ] && [ "$(sha256_of "$tools_dir/$file")" = "$sha" ]; then
            ok "$name" "$tools_dir/$exe"; continue
        fi
        if [ "$check" = 1 ]; then miss "$name" "$tools_dir/$exe"; continue; fi
        installing "$name" "$url"
        mkdir -p "$(dirname "$tools_dir/$file")"
        fetch_verified "$url" "$sha" "$tools_dir/$file"
        case "$file" in *.tar) tar -xf "$tools_dir/$file" -C "$(dirname "$tools_dir/$file")" ;; esac
        chmod +x "$tools_dir/$exe"
    done

    baseline="$tools_dir/stoopid-baseline"
    if [ -x "$baseline/stoopid-chess" ]; then ok 'self-play baseline' "$baseline"
    elif [ "$check" = 1 ]; then miss 'self-play baseline' "$baseline (the 'publish baseline' task)"
    elif [ -z "$(dotnet_sdk)" ]; then miss 'self-play baseline' 'needs the .NET SDK'
    else
        installing 'self-play baseline' "published from this checkout into $baseline"
        dotnet publish src/StoopidChess.Uci -c Release -o "$baseline" >/dev/null
    fi
fi

if [ "$check" = 1 ]; then
    if [ "$missing" -gt 0 ]; then echo "$missing missing: run tools/setup-dev.sh (VS Code: the 'setup environment' task)."; exit 1; fi
    echo 'Everything is in place.'
else
    echo 'Done. Open a new terminal (or restart VS Code) for PATH changes to reach it, then: dotnet test tests/StoopidChess.Tests -c Release'
fi
