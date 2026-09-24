#!/usr/bin/env bash
# Removes a per-user Wind Down installation. Preferences stay in ~/.local/share/Wind Down.
set -euo pipefail
[ -n "${HOME:-}" ] || { echo "HOME is not set." >&2; exit 1; }
DEST="$HOME/.local/lib/wind-down"
BIN="$HOME/.local/bin"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
if [ -x "$DEST/WindDown" ]; then
  set +e; "$DEST/WindDown" --status >/dev/null 2>&1; status=$?; set -e
  if [ "$status" -eq 3 ]; then echo "A Wind Down power schedule is still active. Cancel it first (wind-down cancel), then uninstall." >&2; exit 1; fi
fi
if pgrep -u "$(id -u)" -x WindDown >/dev/null 2>&1; then echo "Wind Down is still open. Choose Exit Wind Down from its menu, then uninstall." >&2; exit 1; fi
if [ -f "$BIN/wind-down" ] && grep -q "Wind Down command-line entry point" "$BIN/wind-down"; then rm -f "$BIN/wind-down"; fi
rm -f "$DATA/applications/wind-down.desktop" "$DATA/icons/hicolor/scalable/apps/wind-down.svg" "$DATA/icons/hicolor/256x256/apps/wind-down.png"
command -v update-desktop-database >/dev/null && update-desktop-database -q "$DATA/applications" || true
[ -x "$DEST/WindDown" ] && rm -rf "$DEST"
echo "Wind Down was removed. Preferences remain in $DATA/Wind Down."
