#!/usr/bin/env bash
#
# Removes what install.sh put in your home folder. Your settings and downloaded mods stay.
#
set -euo pipefail

data="${XDG_DATA_HOME:-$HOME/.local/share}"

rm -rf "$HOME/.local/lib/mfg-manager" "$HOME/.local/lib/mfg-manager.new"
rm -f "$HOME/.local/bin/mfg-manager"
rm -f "$data/applications/mfg-manager.desktop"
rm -f "$data/icons/hicolor/256x256/apps/mfg-manager.png"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true

echo "Removed MFG Manager."
echo "Your settings and mods are still in: $data/MFG Manager"
echo "Delete that folder too if you don't want them any more."
echo "Any mod you installed into a game stays there. Use Revert all in the app first if you want those gone."
