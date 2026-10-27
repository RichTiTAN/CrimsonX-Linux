#!/bin/sh
# ---------------------------------------------------------------------------
# CrimsonX one-line installer
#
#   curl -fsSL https://raw.githubusercontent.com/RichTiTAN/CrimsonX-Linux/main/install/get-crimsonx.sh | sh
#
# Works out which build this machine needs, downloads it, verifies its checksum,
# and hands over to the install.sh packed inside the archive. Nothing outside
# $HOME is touched, except the single setcap that VPN Mode needs - and that one
# asks for your password through sudo.
#
#   sh get-crimsonx.sh                          install into ~/.local
#   PREFIX=/somewhere sh get-crimsonx.sh        install somewhere else
#   CRIMSONX_VERSION=v1.2.3 sh get-crimsonx.sh  pin a version (default: latest)
#   CRIMSONX_BASE_URL=https://mirror/...        use a mirror or a test server
#   KEEP=1 sh get-crimsonx.sh                   leave the download behind
# ---------------------------------------------------------------------------
set -eu

REPO=${CRIMSONX_REPO:-RichTiTAN/CrimsonX-Linux}
VERSION=${CRIMSONX_VERSION:-latest}

BASE=${CRIMSONX_BASE_URL:-}
if [ -z "$BASE" ]; then
    if [ "$VERSION" = latest ]; then
        BASE="https://github.com/$REPO/releases/latest/download"
    else
        BASE="https://github.com/$REPO/releases/download/$VERSION"
    fi
fi

say() { printf '%s\n' "$*"; }
die() { printf 'get-crimsonx.sh: %s\n' "$*" >&2; exit 1; }

# ── what are we running on ───────────────────────────────────────────────────
arch=$(uname -m 2>/dev/null || echo unknown)
case "$arch" in
    x86_64 | amd64)          tag=x64 ;;
    aarch64 | arm64)         tag=arm64 ;;
    armv7l | armv8l | armhf) tag=arm ;;
    armv6l)
        die "ARMv6 ($arch) is not supported; CrimsonX needs at least ARMv7" ;;
    *)
        die "unsupported architecture '$arch' (need x86_64, aarch64 or armv7)" ;;
esac

# ── downloader ───────────────────────────────────────────────────────────────
download() {
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL --retry 3 --connect-timeout 20 --max-time 1800 -o "$2" "$1"
    elif command -v wget >/dev/null 2>&1; then
        wget -q -O "$2" "$1"
    else
        die "this needs curl or wget installed"
    fi
}

tmp=$(mktemp -d 2>/dev/null || echo "/tmp/crimsonx.$$")
mkdir -p "$tmp"
cleanup() { [ "${KEEP:-0}" = 1 ] || rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM

archive="CrimsonX-linux-$tag.tar.gz"
say "CrimsonX installer"
say "  machine : $arch  ->  $tag"
say "  source  : $BASE/$archive"

download "$BASE/$archive" "$tmp/$archive" ||
    die "download failed - check your connection, or point CRIMSONX_BASE_URL at a mirror"

# ── integrity ────────────────────────────────────────────────────────────────
# The checksum is published next to the archive. It is optional on purpose: a
# missing sidecar should not stop an install, but a wrong one must.
expected=""
if download "$BASE/$archive.sha256" "$tmp/archive.sha256" 2>/dev/null; then
    expected=$(cut -d' ' -f1 "$tmp/archive.sha256" 2>/dev/null | head -n 1 || true)
fi
if [ -n "$expected" ]; then
    actual=""
    if command -v sha256sum >/dev/null 2>&1; then
        actual=$(sha256sum "$tmp/$archive" | cut -d' ' -f1)
    elif command -v shasum >/dev/null 2>&1; then
        actual=$(shasum -a 256 "$tmp/$archive" | cut -d' ' -f1)
    fi
    if [ -z "$actual" ]; then
        say "  checksum: no sha256 tool here, skipped"
    else
        [ "$actual" = "$expected" ] ||
            die "checksum mismatch - the download is corrupt or was tampered with"
        say "  checksum: ok"
    fi
fi

# ── unpack and install ───────────────────────────────────────────────────────
command -v tar >/dev/null 2>&1 || die "this needs tar installed"
say "  unpacking"
# Unpack into a subdirectory, not $tmp itself: install.sh copies everything that
# sits next to it into the destination, so the archive and its checksum must not
# be there or they would be installed as if they were part of the app.
mkdir -p "$tmp/app"
tar -xzf "$tmp/$archive" -C "$tmp/app" || die "could not unpack $archive"
[ -f "$tmp/app/install.sh" ] || die "install.sh is missing from the archive"

# install.sh is given /dev/null so that, when this whole script is piped into sh,
# the child can never swallow the rest of the script from stdin. sudo still
# prompts, because it opens /dev/tty itself rather than reading stdin.
say "  handing over to install.sh"
say ""
sh "$tmp/app/install.sh" < /dev/null
