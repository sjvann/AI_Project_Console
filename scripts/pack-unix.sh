#!/usr/bin/env bash
# 打包 macOS .app zip 或 Linux zip（可選 .deb）。
# 正式資產必須在對應 OS 的 runner 產出，才能保留 Unix 執行權限並做 ad-hoc 簽署。
set -euo pipefail

VERSION=""
RUNTIME=""
CONFIGURATION="Release"
ROOT=""

usage() {
  echo "用法: pack-unix.sh --version 0.6.16 --runtime osx-arm64|osx-x64|linux-x64|linux-arm64" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version|-v) VERSION="${2:-}"; shift 2 ;;
    --runtime|-r) RUNTIME="${2:-}"; shift 2 ;;
    --configuration|-c) CONFIGURATION="${2:-}"; shift 2 ;;
    --root) ROOT="${2:-}"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "未知參數：$1" >&2; usage ;;
  esac
done

if [[ -z "$VERSION" || -z "$RUNTIME" ]]; then
  usage
fi

case "$RUNTIME" in
  osx-arm64|osx-x64|linux-x64|linux-arm64) ;;
  *) echo "不支援的 RID：$RUNTIME" >&2; exit 1 ;;
esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ -z "$ROOT" ]]; then
  ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
fi
ROOT="$(cd "$ROOT" && pwd)"

PROJECT="$ROOT/src/AiProject.Console.App/AiProject.Console.App.csproj"
PUBLISH_DIR="$ROOT/dist/$RUNTIME"
DIST="$ROOT/dist"
mkdir -p "$DIST"

echo "PACK:publish"
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

R2R="false"
HOST="$(uname -s)"
ARCH="$(uname -m)"
if [[ "$HOST" == Darwin && "$RUNTIME" == osx-* ]]; then
  R2R="true"
fi
if [[ "$HOST" == Linux && "$RUNTIME" == linux-x64 && "$ARCH" == x86_64 ]]; then
  R2R="true"
fi
if [[ "$HOST" == Linux && "$RUNTIME" == linux-arm64 && ( "$ARCH" == aarch64 || "$ARCH" == arm64 ) ]]; then
  R2R="true"
fi

dotnet publish "$PROJECT" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishReadyToRun="$R2R" \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$PUBLISH_DIR"

find "$PUBLISH_DIR" -name '*.pdb' -delete

EXE="$PUBLISH_DIR/AI_Project_Console"
if [[ ! -f "$EXE" ]]; then
  echo "publish 後找不到 $EXE" >&2
  exit 1
fi
chmod +x "$EXE"

write_sha256() {
  local file="$1"
  local name hash
  name="$(basename "$file")"
  if command -v sha256sum >/dev/null 2>&1; then
    hash="$(sha256sum "$file" | awk '{print $1}')"
  else
    hash="$(shasum -a 256 "$file" | awk '{print $1}')"
  fi
  printf '%s  %s\n' "$hash" "$name" > "$file.sha256"
  echo "  ${name}.sha256"
}

find_native() {
  local name="$1"
  local found=""
  if [[ -f "$PUBLISH_DIR/$name" ]]; then
    echo "$PUBLISH_DIR/$name"
    return 0
  fi
  found="$(find "$PUBLISH_DIR" -name "$name" | head -n 1)"
  if [[ -n "$found" ]]; then
    echo "$found"
    return 0
  fi
  return 1
}

if [[ "$RUNTIME" == osx-* ]]; then
  NATIVE="$(find_native Photino.Native.dylib || true)"
  if [[ -z "$NATIVE" ]]; then
    echo "publish 後找不到 Photino.Native.dylib" >&2
    exit 1
  fi

  echo "PACK:app"
  APP_DIR="$DIST/macos-$RUNTIME/AI_Project_Console.app"
  rm -rf "$DIST/macos-$RUNTIME"
  mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
  cp -R "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
  chmod +x "$APP_DIR/Contents/MacOS/AI_Project_Console"

  PLIST_SRC="$ROOT/installer/macos/Info.plist"
  sed "s/__VERSION__/$VERSION/g" "$PLIST_SRC" > "$APP_DIR/Contents/Info.plist"

  ICON_PNG="$ROOT/assets/brand/logo.png"
  ICONSET="$DIST/macos-$RUNTIME/AppIcon.iconset"
  if [[ "$HOST" == Darwin && -f "$ICON_PNG" ]] && command -v sips >/dev/null && command -v iconutil >/dev/null; then
    rm -rf "$ICONSET"
    mkdir -p "$ICONSET"
    sips -z 16 16 "$ICON_PNG" --out "$ICONSET/icon_16x16.png" >/dev/null
    sips -z 32 32 "$ICON_PNG" --out "$ICONSET/icon_16x16@2x.png" >/dev/null
    sips -z 32 32 "$ICON_PNG" --out "$ICONSET/icon_32x32.png" >/dev/null
    sips -z 64 64 "$ICON_PNG" --out "$ICONSET/icon_32x32@2x.png" >/dev/null
    sips -z 128 128 "$ICON_PNG" --out "$ICONSET/icon_128x128.png" >/dev/null
    sips -z 256 256 "$ICON_PNG" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
    sips -z 256 256 "$ICON_PNG" --out "$ICONSET/icon_256x256.png" >/dev/null
    sips -z 512 512 "$ICON_PNG" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
    sips -z 512 512 "$ICON_PNG" --out "$ICONSET/icon_512x512.png" >/dev/null
    sips -z 1024 1024 "$ICON_PNG" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
    iconutil -c icns "$ICONSET" -o "$APP_DIR/Contents/Resources/AppIcon.icns"
    rm -rf "$ICONSET"
  elif [[ -f "$ICON_PNG" ]]; then
    cp "$ICON_PNG" "$APP_DIR/Contents/Resources/AppIcon.png"
    # 非 Darwin 無法產 icns；plist 改指 png，macOS 仍可顯示。
    python3 - "$APP_DIR/Contents/Info.plist" <<'PY' || true
import pathlib, sys
p = pathlib.Path(sys.argv[1])
text = p.read_text(encoding="utf-8")
p.write_text(text.replace("<string>AppIcon</string>", "<string>AppIcon.png</string>", 1), encoding="utf-8")
PY
  fi

  if [[ "$HOST" == Darwin ]] && command -v codesign >/dev/null; then
    echo "PACK:codesign"
    # ad-hoc：沒有 Apple Developer ID 時至少讓本機 Gatekeeper 較不挑剔；仍未公證。
    codesign --force --deep --sign - "$APP_DIR"
  fi

  echo "PACK:zip"
  ZIP="$DIST/AI_Project_Console-$VERSION-$RUNTIME.zip"
  rm -f "$ZIP"
  (
    cd "$DIST/macos-$RUNTIME"
    zip -qry "$ZIP" AI_Project_Console.app
  )
  write_sha256 "$ZIP"
else
  NATIVE="$(find_native Photino.Native.so || true)"
  if [[ -z "$NATIVE" ]]; then
    echo "publish 後找不到 Photino.Native.so" >&2
    exit 1
  fi

  echo "PACK:linux"
  STAGE="$DIST/linux-$RUNTIME"
  rm -rf "$STAGE"
  mkdir -p "$STAGE"
  cp -R "$PUBLISH_DIR"/. "$STAGE/"
  chmod +x "$STAGE/AI_Project_Console"
  cp "$ROOT/installer/linux/install.sh" "$STAGE/install.sh"
  chmod +x "$STAGE/install.sh"
  cat > "$STAGE/README-UNIX.txt" <<EOF
AI_Project 控制台 $VERSION（$RUNTIME）

免安裝：在本目錄執行 ./AI_Project_Console
安裝到使用者目錄：./install.sh

需要 GTK 3 與 WebKitGTK。Ubuntu／Debian：
  sudo apt install libgtk-3-0 libnotify4 libwebkit2gtk-4.1-0
若套件不存在，改裝 libwebkit2gtk-4.0-0。
EOF

  echo "PACK:zip"
  ZIP="$DIST/AI_Project_Console-$VERSION-$RUNTIME.zip"
  rm -f "$ZIP"
  (
    cd "$STAGE"
    zip -qry "$ZIP" .
  )
  write_sha256 "$ZIP"

  if [[ "$HOST" == Linux ]] && command -v dpkg-deb >/dev/null; then
    echo "PACK:deb"
    DEB_ROOT="$DIST/deb-$RUNTIME"
    rm -rf "$DEB_ROOT"
    case "$RUNTIME" in
      linux-x64) DEB_ARCH="amd64" ;;
      linux-arm64) DEB_ARCH="arm64" ;;
      *) DEB_ARCH="amd64" ;;
    esac
    PAYLOAD="$DEB_ROOT/opt/AI_Project_Console"
    mkdir -p "$PAYLOAD" "$DEB_ROOT/usr/bin" "$DEB_ROOT/usr/share/applications" \
      "$DEB_ROOT/usr/share/icons/hicolor/256x256/apps" "$DEB_ROOT/DEBIAN"
    cp -R "$PUBLISH_DIR"/. "$PAYLOAD/"
    chmod +x "$PAYLOAD/AI_Project_Console"
    printf 'installed\n' > "$PAYLOAD/console-installed.marker"
    ln -s /opt/AI_Project_Console/AI_Project_Console "$DEB_ROOT/usr/bin/ai-project-console"
    if [[ -f "$ROOT/assets/brand/logo.png" ]]; then
      cp "$ROOT/assets/brand/logo.png" "$DEB_ROOT/usr/share/icons/hicolor/256x256/apps/ai-project-console.png"
      ICON="/usr/share/icons/hicolor/256x256/apps/ai-project-console.png"
    else
      ICON="/opt/AI_Project_Console/AI_Project_Console"
    fi
    sed \
      -e "s|__EXEC__|/opt/AI_Project_Console/AI_Project_Console|g" \
      -e "s|__ICON__|$ICON|g" \
      "$ROOT/installer/linux/ai-project-console.desktop" \
      > "$DEB_ROOT/usr/share/applications/ai-project-console.desktop"
    SIZE_KB="$(du -sk "$PAYLOAD" | awk '{print $1}')"
    sed \
      -e "s/__VERSION__/$VERSION/g" \
      -e "s/__ARCH__/$DEB_ARCH/g" \
      -e "s/__SIZE__/$SIZE_KB/g" \
      "$ROOT/installer/linux/control" \
      > "$DEB_ROOT/DEBIAN/control"
    DEB="$DIST/AI_Project_Console-$VERSION-$RUNTIME.deb"
    rm -f "$DEB"
    dpkg-deb --root-owner-group --build "$DEB_ROOT" "$DEB"
    write_sha256 "$DEB"
  fi
fi

echo "PACK:done"
ls -lh "$DIST"/AI_Project_Console-"$VERSION"-"$RUNTIME"*
