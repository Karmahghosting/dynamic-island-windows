#!/usr/bin/env bash
set -e

VER="${1:-1.0.0}"
PKG=pkg
rm -rf "$PKG"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/dynamic-island" "$PKG/usr/bin" "$PKG/usr/share/applications"

cp -r publish-linux/* "$PKG/opt/dynamic-island/"
chmod +x "$PKG/opt/dynamic-island/DynamicIsland"

cat > "$PKG/DEBIAN/control" <<EOF
Package: dynamic-island
Version: $VER
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Karmahghosting
Description: Dynamic Island for Linux (Avalonia port)
 A macOS-style Dynamic Island: media, timer, file shelf, battery.
EOF

cat > "$PKG/usr/bin/dynamic-island" <<'EOF'
#!/bin/sh
exec /opt/dynamic-island/DynamicIsland "$@"
EOF
chmod +x "$PKG/usr/bin/dynamic-island"

cat > "$PKG/usr/share/applications/dynamic-island.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Dynamic Island
Exec=/opt/dynamic-island/DynamicIsland
Categories=Utility;
Terminal=false
EOF

dpkg-deb --build "$PKG" "dynamic-island_${VER}_amd64.deb"
