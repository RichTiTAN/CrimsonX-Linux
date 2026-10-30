#!/bin/sh
# ---------------------------------------------------------------------------
# CrimsonX installer
#
# Unpacks the app into ~/.local/opt/CrimsonX and adds a menu entry. Nothing
# outside $HOME is touched, which is what makes this safe on SteamOS: its root
# filesystem is immutable and is replaced by OS updates.
#
#   ./install.sh                    install for the current user
#   PREFIX=/some/dir ./install.sh   install somewhere else
# ---------------------------------------------------------------------------
set -eu

APP_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PREFIX=${PREFIX:-"$HOME/.local"}
DEST="$PREFIX/opt/CrimsonX"
BIN_DIR="$PREFIX/bin"
DATA_HOME=${XDG_DATA_HOME:-"$HOME/.local/share"}
APPS_DIR="$DATA_HOME/applications"
ICON_ROOT="$DATA_HOME/icons/hicolor"
SIZE_DIR="$ICON_ROOT/512x512/apps"

say() { printf '%s\n' "$*"; }
die() { printf 'install.sh: %s\n' "$*" >&2; exit 1; }

[ -f "$APP_DIR/CrimsonX" ] || die "CrimsonX was not found next to this script - run ./install.sh from the unpacked folder"

say "CrimsonX installer"
say "  from: $APP_DIR"
say "  into: $DEST"

missing=""
ldconfig_bin=""
for candidate in ldconfig /sbin/ldconfig /usr/sbin/ldconfig /usr/bin/ldconfig; do
    if command -v "$candidate" >/dev/null 2>&1; then ldconfig_bin="$candidate"; break; fi
done

have_lib() {
    lib=$1
    if [ -n "$ldconfig_bin" ] && "$ldconfig_bin" -p 2>/dev/null | grep -q -- "$lib"; then
        return 0
    fi
    for dir in /usr/lib /usr/lib64 /lib /lib64 /usr/local/lib \
               /usr/lib/x86_64-linux-gnu /usr/lib/aarch64-linux-gnu /usr/lib/arm-linux-gnueabihf; do
        [ -e "$dir/$lib" ] && return 0
        for match in "$dir/$lib".*; do
            [ -e "$match" ] && return 0
        done
    done
    return 1
}

for lib in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1 libicuuc.so; do
    have_lib "$lib" || missing="$missing $lib"
done
if [ -n "$missing" ] && [ -z "$ldconfig_bin" ]; then
    say "  note: ldconfig was not found, so the library check could not be trusted."
    say "        If the app fails to start, install:$missing"
    missing=""
fi
if [ -n "$missing" ]; then
    say ""
    say "Missing runtime libraries:$missing"
    say ""
    say "Install them, then run this script again:"
    if command -v apt-get >/dev/null 2>&1; then
        say "  sudo apt-get install -y libx11-6 libice6 libsm6 libfontconfig1 libicu-dev"
    elif command -v pacman >/dev/null 2>&1; then
        say "  sudo pacman -S --needed libx11 libice libsm fontconfig icu"
    fi
    say ""
    say "libicuuc is required by the .NET runtime itself, not by CrimsonX."
    exit 1
fi

# ── copy the program ─────────────────────────────────────────────────────────
say "  copying the program"
mkdir -p "$DEST"
for entry in "$APP_DIR"/*; do
    name=$(basename -- "$entry")
    case "$name" in
        install.sh|fonts|README-linux.txt) continue ;;
    esac
    rm -rf "${DEST:?}/$name"
    cp -a "$entry" "$DEST/"
done

chmod +x "$DEST/CrimsonX"
if [ -f "$DEST/Data/xray/xray" ]; then chmod +x "$DEST/Data/xray/xray"; fi
if [ -f "$DEST/Data/sing_box/sing-box" ]; then chmod +x "$DEST/Data/sing_box/sing-box"; fi
if [ -f "$DEST/Data/awg/amnezia-box" ]; then chmod +x "$DEST/Data/awg/amnezia-box"; fi

# Files unpacked from a Windows drive (or a zip made on Windows) arrive
# world-writable; give them ordinary permissions.
chmod -R u+rwX,go+rX "$DEST" 2>/dev/null || true
chmod +x "$DEST/CrimsonX"
if [ -f "$DEST/Data/xray/xray" ]; then chmod +x "$DEST/Data/xray/xray"; fi
if [ -f "$DEST/Data/sing_box/sing-box" ]; then chmod +x "$DEST/Data/sing_box/sing-box"; fi
if [ -f "$DEST/Data/awg/amnezia-box" ]; then chmod +x "$DEST/Data/awg/amnezia-box"; fi

# ── fonts ────────────────────────────────────────────────────────────────────
# Selawik has a real SemiBold face and fontconfig only finds it when the fonts
# are installed; without this step every SemiBold label falls back to Bold.
say "  installing fonts"
FONT_DIR="$DATA_HOME/fonts/crimsonx"
if [ -d "$APP_DIR/fonts" ]; then
    mkdir -p "$FONT_DIR"
    cp -f "$APP_DIR"/fonts/*.ttf "$FONT_DIR"/ 2>/dev/null || true
    if command -v fc-cache >/dev/null 2>&1; then
        fc-cache -f "$FONT_DIR" >/dev/null 2>&1 || true
    fi
fi

# ── launcher + menu entry ────────────────────────────────────────────────────
say "  adding the launcher"
mkdir -p "$BIN_DIR" "$APPS_DIR"
ln -sfn "$DEST/CrimsonX" "$BIN_DIR/crimsonx"

if [ -f "$APP_DIR/crimsonx.desktop" ]; then
    sed "s|^Exec=.*|Exec=$DEST/CrimsonX|" "$APP_DIR/crimsonx.desktop" > "$APPS_DIR/crimsonx.desktop"
    chmod 644 "$APPS_DIR/crimsonx.desktop"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi

# ── VPN Mode permission ──────────────────────────────────────────────────────
# Only sing-box creates the tunnel interface, so only sing-box needs the
# capability. Granting it here - in a terminal, where a password prompt always
# works - means VPN Mode never has to ask again, and it also works in sessions
# that have no polkit agent at all (Game Mode / VR).
SINGBOX="$DEST/Data/sing_box/sing-box"
if [ -f "$SINGBOX" ]; then
    if command -v setcap >/dev/null 2>&1; then
        say "  granting VPN Mode permission (sing-box)"
        if [ "$(id -u)" -eq 0 ]; then
            if setcap cap_net_admin,cap_net_raw+ep "$SINGBOX" 2>/dev/null; then
                say "    ok"
            else
                say "    could not set it; VPN Mode will ask for a password instead"
            fi
        elif command -v sudo >/dev/null 2>&1; then
            if sudo setcap cap_net_admin,cap_net_raw+ep "$SINGBOX" 2>/dev/null; then
                say "    ok"
            else
                say "    skipped; VPN Mode will ask for a password instead"
            fi
        else
            say "    no sudo available; VPN Mode will ask for a password instead"
        fi
    else
        say "  note: setcap is missing, VPN Mode will ask for a password instead"
    fi
fi

say ""
say "Done. CrimsonX is installed."
say "  start it from the menu, or run: crimsonx"
say "  diagnose a machine with:         crimsonx --doctor"
say ""
say "  settings : ${XDG_CONFIG_HOME:-$HOME/.config}/crimsonx"
say "  data     : $DATA_HOME/crimsonx"
say ""
say "VPN Mode needs one capability on sing-box; the step above grants it."
say "If it was skipped, the app asks for a password through polkit instead."
