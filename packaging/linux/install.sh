#!/usr/bin/env bash
#
# Installs MFG Manager for your user only. Everything goes in your home folder, so it doesn't need root.
# Running it again updates the app. Your settings and mods stay, they're in a different folder.
#
set -euo pipefail

if [ "$(id -u)" -eq 0 ]; then
    echo "Please run this as your normal user, not as root." >&2
    exit 1
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
app="$HOME/.local/lib/mfg-manager"
bin="$HOME/.local/bin"

if [ ! -f "$here/app/MfgManager" ]; then
    echo "Can't find the app next to this script. Unpack the whole .tar.gz, or run tools/build-linux.sh first." >&2
    exit 1
fi

mkdir -p "$(dirname "$app")" "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"

# Copy first and swap at the end, so a failed copy can't break a working install.
rm -rf "$app.new"
cp -R "$here/app" "$app.new"
chmod +x "$app.new/MfgManager"
rm -rf "$app"
mv "$app.new" "$app"

ln -sf "$app/MfgManager" "$bin/mfg-manager"
cp "$here/mfg-manager.png" "$data/icons/hicolor/256x256/apps/mfg-manager.png"

# The menu entry gets the full path, because the desktop's own launcher doesn't always look in ~/.local/bin.
sed "s|^Exec=.*|Exec=\"$app/MfgManager\"|" "$here/mfg-manager.desktop" > "$data/applications/mfg-manager.desktop"

# Refreshing the menu and icons is only a convenience, so it's fine if these aren't there.
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$data/icons/hicolor" >/dev/null 2>&1 || true

echo "Installed. Find MFG Manager in your applications menu, or run: $bin/mfg-manager"
