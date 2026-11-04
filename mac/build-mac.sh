#!/bin/bash
# Збирає «ClickUp Widget.app» і пакети Velopack (.pkg-інсталятор і портативний .zip) для однієї архітектури.
# Працює лише на Маку (codesign, iconutil, pkgbuild). У GitHub Actions — див. .github/workflows/macos.yml.
#
#   bash mac/build-mac.sh osx-arm64 [тека]   # Apple Silicon (M1 і новіші)
#   bash mac/build-mac.sh osx-x64   [тека]   # Intel
#
# Потрібні .NET 8 SDK і vpk (dotnet tool install -g vpk --version 1.2.161).
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${2:-$ROOT/out-mac}"
case "$OUT" in /*) ;; *) OUT="$ROOT/$OUT" ;; esac
# Версія одна на Windows і Mac — з src/ClickUpWidget.csproj.
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/ClickUpWidget.csproj" | head -1)
APP="$OUT/$RID/ClickUp Widget.app"

echo "== ClickUp Widget $VERSION для $RID"
rm -rf "${OUT:?}/$RID"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/mac/ClickUpWidget.Mac.csproj" -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -o "$APP/Contents/MacOS"
rm -f "$APP/Contents/MacOS/"*.pdb
sed "s/__VERSION__/$VERSION/g" "$ROOT/mac/Info.plist" > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist"

# Значок програми: PNG малює сама програма (--icons), .icns складає системна утиліта.
# Малювати може лише збірка під архітектуру цього Мака, тож готовий .icns використовуємо повторно.
ICNS="$OUT/AppIcon.icns"
if [ ! -f "$ICNS" ]; then
  ICONS="$OUT/icons"
  ICONSET="$OUT/AppIcon.iconset"
  rm -rf "$ICONS" "$ICONSET"
  "$APP/Contents/MacOS/ClickUpWidget" --icons "$ICONS"
  mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    cp "$ICONS/icon_$s.png" "$ICONSET/icon_${s}x${s}.png"
    cp "$ICONS/icon_$((s * 2)).png" "$ICONSET/icon_${s}x${s}@2x.png"
  done
  iconutil -c icns "$ICONSET" -o "$ICNS"
fi
cp "$ICNS" "$APP/Contents/Resources/AppIcon.icns"

# Без Apple Developer ID підписуємо «ad-hoc»: інакше на Apple Silicon програма взагалі не запуститься.
codesign --force --deep --sign - "$APP"
codesign --verify --deep --verbose=1 "$APP"

# Пакети Velopack: Setup.pkg (ставить у «Програми»), Portable.zip і файли для автооновлення.
# Канал = архітектура, щоб Mac з Intel не отримав збірку для Apple Silicon і навпаки.
vpk pack --packId ClickUpWidget --packVersion "$VERSION" --packDir "$APP" --mainExe ClickUpWidget \
  --packTitle "ClickUp Widget" --packAuthors "Dima" --runtime "$RID" --channel "$RID" \
  --icon "$ICNS" --outputDir "$OUT/$RID/Releases"

echo "== Готово: $OUT/$RID/Releases"
ls -la "$OUT/$RID/Releases"
