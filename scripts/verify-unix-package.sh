#!/usr/bin/env bash
# 驗證 macOS／Linux 安裝包結構。在對應 OS 上會再跑 --mcp 煙霧測試。
set -euo pipefail

VERSION=""
RUNTIME=""
ROOT=""
ZIP=""

usage() {
  echo "用法: verify-unix-package.sh --version 0.6.18 --runtime osx-arm64 [--zip path]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version|-v) VERSION="${2:-}"; shift 2 ;;
    --runtime|-r) RUNTIME="${2:-}"; shift 2 ;;
    --root) ROOT="${2:-}"; shift 2 ;;
    --zip) ZIP="${2:-}"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "未知參數：$1" >&2; usage ;;
  esac
done

if [[ -z "$VERSION" || -z "$RUNTIME" ]]; then
  usage
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ -z "$ROOT" ]]; then
  ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
fi
ROOT="$(cd "$ROOT" && pwd)"
if [[ -z "$ZIP" ]]; then
  ZIP="$ROOT/dist/AI_Project_Console-$VERSION-$RUNTIME.zip"
fi
if [[ ! -f "$ZIP" ]]; then
  echo "找不到 $ZIP" >&2
  exit 1
fi

SIDE="$ZIP.sha256"
if [[ ! -f "$SIDE" ]]; then
  echo "找不到 SHA256 旁檔 $SIDE" >&2
  exit 1
fi

HASH_LINE="$(tr -d '\r' < "$SIDE")"
EXPECTED="$(echo "$HASH_LINE" | awk '{print $1}')"
if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL="$(sha256sum "$ZIP" | awk '{print $1}')"
else
  ACTUAL="$(shasum -a 256 "$ZIP" | awk '{print $1}')"
fi
if [[ "$EXPECTED" != "$ACTUAL" ]]; then
  echo "SHA256 不符：旁檔 $EXPECTED 實際 $ACTUAL" >&2
  exit 1
fi
echo "OK checksum $ACTUAL"

TMP="$(mktemp -d)"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT
unzip -q "$ZIP" -d "$TMP"

fail() { echo "FAIL: $*" >&2; exit 1; }

HOST="$(uname -s)"
ARCH="$(uname -m)"

if [[ "$RUNTIME" == osx-* ]]; then
  APP="$TMP/AI_Project_Console.app"
  [[ -d "$APP" ]] || fail "zip 根目錄沒有 AI_Project_Console.app"
  PLIST="$APP/Contents/Info.plist"
  EXE="$APP/Contents/MacOS/AI_Project_Console"
  [[ -f "$PLIST" ]] || fail "缺少 Info.plist"
  [[ -f "$EXE" ]] || fail "缺少 Contents/MacOS/AI_Project_Console"
  grep -q "sjvann.AIProjectConsole" "$PLIST" || fail "Info.plist 沒有 CFBundleIdentifier"
  grep -q "$VERSION" "$PLIST" || fail "Info.plist 沒有版號 $VERSION"
  grep -q "AI_Project_Console" "$PLIST" || fail "Info.plist 沒有 CFBundleExecutable"
  if find "$APP/Contents/MacOS" -name 'Photino.Native.dylib' | grep -q .; then
    echo "OK Photino.Native.dylib"
  else
    fail "缺少 Photino.Native.dylib"
  fi
  [[ -f "$APP/Contents/Resources/AppIcon.icns" || -f "$APP/Contents/Resources/AppIcon.png" ]] || fail "缺少 AppIcon"
  if [[ "$HOST" == Darwin ]]; then
    [[ -x "$EXE" ]] || fail "主程式沒有執行權限（zip 必須在 macOS 打包）"
    file "$EXE" | grep -qi "Mach-O" || fail "主程式不是 Mach-O：$EXE"
    if [[ "$RUNTIME" == osx-arm64 ]]; then
      file "$EXE" | grep -qi "arm64" || fail "osx-arm64 資產不是 arm64"
    else
      file "$EXE" | grep -qi "x86_64" || fail "osx-x64 資產不是 x86_64"
    fi
    if command -v plutil >/dev/null; then
      plutil -lint "$PLIST"
    fi
    if command -v codesign >/dev/null; then
      codesign -v --deep "$APP" || echo "WARN: codesign 驗證失敗（未公證仍可能發生）"
    fi
    NATIVE="$(find "$APP/Contents/MacOS" -name 'Photino.Native.dylib' | head -n 1)"
    otool -L "$NATIVE"
    MATCHING=0
    if [[ "$RUNTIME" == osx-arm64 && ( "$ARCH" == arm64 || "$ARCH" == aarch64 ) ]]; then
      MATCHING=1
    fi
    if [[ "$RUNTIME" == osx-x64 && "$ARCH" == x86_64 ]]; then
      MATCHING=1
    fi
    if [[ "$MATCHING" == 1 ]]; then
      echo "SMOKE:mcp"
      "$EXE" --mcp --list-tools --root "$ROOT" | head -n 20
    else
      echo "SKIP mcp：runner 架構與 RID 不同"
    fi
  else
    echo "SKIP Darwin 專屬檢查（目前是 $HOST）。官方 Mac 包請在 macos-latest 驗證。"
  fi
else
  EXE=""
  if [[ -f "$TMP/AI_Project_Console" ]]; then
    EXE="$TMP/AI_Project_Console"
  else
    EXE="$(find "$TMP" -maxdepth 3 -type f -name 'AI_Project_Console' | head -n 1)"
  fi
  [[ -n "$EXE" && -f "$EXE" ]] || fail "zip 裡沒有 AI_Project_Console"
  if find "$TMP" -name 'Photino.Native.so' | grep -q .; then
    echo "OK Photino.Native.so"
  else
    fail "缺少 Photino.Native.so"
  fi
  [[ -f "$TMP/install.sh" || -f "$(dirname "$EXE")/install.sh" ]] || fail "缺少 install.sh"
  if [[ "$HOST" == Linux ]]; then
    [[ -x "$EXE" ]] || fail "主程式沒有執行權限（zip 必須在 Linux 打包）"
    file "$EXE" | grep -qi "ELF" || fail "主程式不是 ELF"
    if [[ "$RUNTIME" == linux-x64 ]]; then
      file "$EXE" | grep -Eqi "x86-64|x86_64|AMD64" || fail "linux-x64 資產不是 x86-64"
    else
      file "$EXE" | grep -Eqi "ARM aarch64|aarch64" || fail "linux-arm64 資產不是 aarch64"
    fi
    NATIVE="$(find "$TMP" -name 'Photino.Native.so' | head -n 1)"
    ldd "$NATIVE" || true
    MATCHING=0
    if [[ "$RUNTIME" == linux-x64 && "$ARCH" == x86_64 ]]; then
      MATCHING=1
    fi
    if [[ "$RUNTIME" == linux-arm64 && ( "$ARCH" == aarch64 || "$ARCH" == arm64 ) ]]; then
      MATCHING=1
    fi
    if [[ "$MATCHING" == 1 ]]; then
      echo "SMOKE:mcp"
      "$EXE" --mcp --list-tools --root "$ROOT" | head -n 20
    else
      echo "SKIP mcp：runner 架構與 RID 不同"
    fi
  else
    echo "SKIP Linux 專屬檢查（目前是 $HOST）。官方 Linux 包請在 ubuntu-latest 驗證。"
  fi

  DEB="$ROOT/dist/AI_Project_Console-$VERSION-$RUNTIME.deb"
  if [[ -f "$DEB" && "$HOST" == Linux ]] && command -v dpkg-deb >/dev/null; then
    echo "CHECK:deb"
    dpkg-deb --info "$DEB"
    # 不要把 --contents 直接管到 grep -q：檔案很多時 grep 提前關閉會讓 tar SIGPIPE，pipefail 整段失敗。
    dpkg-deb --contents "$DEB" > "$TMP/deb-contents.txt"
    grep -F "opt/AI_Project_Console/AI_Project_Console" "$TMP/deb-contents.txt" >/dev/null \
      || fail ".deb 沒有 /opt/AI_Project_Console/AI_Project_Console"
    grep -F "usr/share/applications/ai-project-console.desktop" "$TMP/deb-contents.txt" >/dev/null \
      || fail ".deb 沒有 desktop 檔"
  fi
fi

echo "VERIFY:ok $RUNTIME"
