#!/usr/bin/env bash
# Per-user installation of a Wind Down Linux build. No root required.
# Run it from an extracted build folder; `wind-down repair` runs it again in place.
set -euo pipefail
SRC="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
[ -n "${HOME:-}" ] || { echo "HOME is not set." >&2; exit 1; }
DEST="$HOME/.local/lib/wind-down"
BIN="$HOME/.local/bin"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
[ -x "$SRC/WindDown" ] || { echo "WindDown is missing next to install.sh. Run this from an extracted Wind Down build, or run ./build.sh first." >&2; exit 1; }
command -v systemd-run >/dev/null || echo "Warning: systemd-run was not found. Wind Down schedules through systemd user timers." >&2
command -v notify-send >/dev/null || echo "Note: install libnotify (sudo pacman -S libnotify) for reminder notifications." >&2

if [ "$SRC" != "$DEST" ]; then
  # Stage beside the destination, then swap, so a failed copy never leaves a half-installed app.
  # The executable path stays the same, so an active schedule keeps working across an update.
  mkdir -p "$(dirname "$DEST")"
  rm -rf "$DEST.new"
  cp -a "$SRC/." "$DEST.new/"
  if [ -e "$DEST" ]; then
    [ -x "$DEST/WindDown" ] || { echo "Refusing to replace $DEST: it does not look like a Wind Down installation." >&2; rm -rf "$DEST.new"; exit 1; }
    rm -rf "$DEST.old"; mv "$DEST" "$DEST.old"
  fi
  mv "$DEST.new" "$DEST"
  rm -rf "$DEST.old"
fi
chmod +x "$DEST/WindDown" "$DEST/wind-down" "$DEST/install.sh" "$DEST/uninstall.sh"

install -Dm755 "$DEST/wind-down" "$BIN/wind-down"
mkdir -p "$DATA/applications"
sed "s|^Exec=.*|Exec=$BIN/wind-down|" "$DEST/wind-down.desktop" > "$DATA/applications/wind-down.desktop"
install -Dm644 "$DEST/wind-down.svg" "$DATA/icons/hicolor/scalable/apps/wind-down.svg"
install -Dm644 "$DEST/wind-down.png" "$DATA/icons/hicolor/256x256/apps/wind-down.png"
command -v update-desktop-database >/dev/null && update-desktop-database -q "$DATA/applications" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$DATA/icons/hicolor" 2>/dev/null || true

echo "Wind Down is installed in $DEST."
echo "Open it from your app menu or run: wind-down"
case ":$PATH:" in *":$BIN:"*) ;; *) echo "Add $BIN to your PATH to use the wind-down command (open a new terminal after updating ~/.bashrc or your shell's config)." ;; esac
