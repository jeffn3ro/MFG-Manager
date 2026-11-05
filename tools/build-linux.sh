#!/usr/bin/env bash
#
# Builds MFG Manager for Linux. Run it on CachyOS, Mint or any other 64-bit Linux:
#
#   bash tools/build-linux.sh              builds it and makes a .tar.gz in artifacts/
#   bash tools/build-linux.sh --install    the same, then installs it for your user (no root needed)
#   bash tools/build-linux.sh --package    the same, then makes a system package:
#                                          .pkg.tar.zst on CachyOS and Arch, .deb on Mint and Ubuntu
#
# It needs no accounts, keys or secrets. The result carries its own copy of .NET, so the computer
# that runs it doesn't need .NET installed, only the computer that builds it.
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts="$root/artifacts"
stage="$artifacts/linux/mfg-manager"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)"
short_version="${version%.0}"
archive="$artifacts/MFG-Manager-$short_version-linux-x64.tar.gz"

usage() {
    sed -n '3,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

check_computer() {
    if [ "$(id -u)" -eq 0 ]; then
        echo "Please run this as your normal user, not as root." >&2
        exit 1
    fi
    if [ "$(uname -m)" != "x86_64" ]; then
        echo "This builds for 64-bit Intel and AMD processors, and this computer is $(uname -m)." >&2
        exit 1
    fi
}

check_dotnet() {
    if command -v dotnet >/dev/null 2>&1 && [ "$(dotnet --version | cut -d. -f1)" -ge 10 ]; then
        return
    fi
    cat >&2 <<'HELP'
Building needs the .NET 10 SDK. Install it with one of these, then run this script again:

  CachyOS, Arch:          sudo pacman -S --needed dotnet-sdk
  Mint 22, Ubuntu 24.04:  sudo apt install dotnet-sdk-10.0
  Mint 21, Ubuntu 22.04:  sudo add-apt-repository ppa:dotnet/backports
                          sudo apt install dotnet-sdk-10.0
HELP
    exit 1
}

build_app() {
    check_dotnet
    echo "==> Building MFG Manager $version for Linux"
    rm -rf "$stage"
    mkdir -p "$stage"

    # The two opt-outs stop .NET and Avalonia from reporting anything home during the build.
    DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 dotnet publish "$root/src/MfgManager.App" \
        -c Release -r linux-x64 --self-contained true \
        -p:DebugType=none -p:DebugSymbols=false \
        --nologo -v quiet -o "$stage/app"
    chmod +x "$stage/app/MfgManager"

    cp "$root/packaging/linux/install.sh" "$root/packaging/linux/uninstall.sh" "$root/packaging/linux/mfg-manager.desktop" "$stage/"
    cp "$root/src/MfgManager.App/Assets/logo.png" "$stage/mfg-manager.png"
    chmod +x "$stage/install.sh" "$stage/uninstall.sh"

    echo "==> Packing $(basename "$archive")"
    tar -czf "$archive" -C "$artifacts/linux" mfg-manager
}

make_deb() {
    local tree="$artifacts/linux/deb"
    local deb="$artifacts/mfg-manager_${version}_amd64.deb"
    echo "==> Making $(basename "$deb")"

    rm -rf "$tree"
    mkdir -p "$tree/DEBIAN" "$tree/opt/mfg-manager" "$tree/usr/bin" "$tree/usr/share/applications" "$tree/usr/share/icons/hicolor/256x256/apps" "$tree/usr/share/doc/mfg-manager"
    cp "$root/LICENSE" "$tree/usr/share/doc/mfg-manager/copyright"
    cp -R "$stage/app/." "$tree/opt/mfg-manager/"
    ln -s /opt/mfg-manager/MfgManager "$tree/usr/bin/mfg-manager"
    cp "$stage/mfg-manager.desktop" "$tree/usr/share/applications/"
    cp "$stage/mfg-manager.png" "$tree/usr/share/icons/hicolor/256x256/apps/"
    sed "s/@VERSION@/$version/" "$root/packaging/linux/deb-control" > "$tree/DEBIAN/control"

    chmod -R u=rwX,go=rX "$tree"
    chmod 755 "$tree/opt/mfg-manager/MfgManager"
    dpkg-deb --build --root-owner-group "$tree" "$deb"

    echo
    echo "Install it with:  sudo apt install $deb"
}

make_arch_package() {
    echo "==> Making the CachyOS and Arch package"
    # makepkg runs this script again to build the app, see packaging/linux/PKGBUILD.
    (cd "$root/packaging/linux" && PKGDEST="$artifacts" BUILDDIR="$artifacts/linux/makepkg" makepkg --force --cleanbuild)

    echo
    echo "Install it with:  sudo pacman -U $artifacts/mfg-manager-$version-1-x86_64.pkg.tar.zst"
}

mode="${1:-}"
case "$mode" in
    "") check_computer; build_app ;;
    --install)
        check_computer
        build_app
        bash "$stage/install.sh"
        ;;
    --package)
        check_computer
        if command -v makepkg >/dev/null 2>&1; then
            make_arch_package
        elif command -v dpkg-deb >/dev/null 2>&1; then
            build_app
            make_deb
        else
            echo "Found neither makepkg (CachyOS, Arch) nor dpkg-deb (Mint, Ubuntu). Use --install or the .tar.gz instead." >&2
            exit 1
        fi
        ;;
    -h|--help) usage; exit 0 ;;
    *)
        echo "Unknown option: $mode" >&2
        usage >&2
        exit 1
        ;;
esac

echo
echo "Done. Everything is in $artifacts"
