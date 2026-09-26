#!/usr/bin/env bash
# 把目前目錄的控制台裝到使用者目錄（不必 root）。
set -euo pipefail

APP_NAME="AI_Project 控制台"
EXE_NAME="AI_Project_Console"
MARKER="console-installed.marker"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/AI_Project_Console"
BIN="${XDG_BIN_HOME:-$HOME/.local/bin}"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
ROOT="$(cd "$(dirname "$0")" && pwd)"

if [[ ! -f "$ROOT/$EXE_NAME" ]]; then
  echo "找不到 $EXE_NAME。請先解壓 linux zip，再在該目錄執行 install.sh。" >&2
  exit 1
fi

mkdir -p "$DEST" "$BIN" "$APPS" "$ICON_DIR"
# 不覆蓋使用者資料夾裡既有的 settings；只覆寫程式檔。
cp -R "$ROOT"/. "$DEST"/
chmod +x "$DEST/$EXE_NAME"
printf 'installed\n' > "$DEST/$MARKER"
ln -sfn "$DEST/$EXE_NAME" "$BIN/ai-project-console"

ICON_SRC=""
for cand in \
  "$DEST/wwwroot/img/logo.png" \
  "$DEST/wwwroot/favicon.ico" \
  "$DEST/Assets/app.ico"
do
  if [[ -f "$cand" ]]; then
    ICON_SRC="$cand"
    break
  fi
done
ICON_PATH="$DEST/$EXE_NAME"
if [[ -n "$ICON_SRC" && "$ICON_SRC" == *.png ]]; then
  cp "$ICON_SRC" "$ICON_DIR/ai-project-console.png"
  ICON_PATH="$ICON_DIR/ai-project-console.png"
fi

DESKTOP="$APPS/ai-project-console.desktop"
cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=$APP_NAME
Comment=本機堆疊控制台：掃描、編譯、一鍵啟動
Exec=$DEST/$EXE_NAME
Icon=$ICON_PATH
Terminal=false
Categories=Development;
StartupWMClass=$EXE_NAME
EOF
chmod +x "$DESKTOP"

echo "已安裝到 $DEST"
echo "命令：ai-project-console（請確認 $BIN 在 PATH）"
echo "應用程式選單：AI_Project 控制台"
echo "Linux 桌面需要 GTK 3 與 WebKitGTK（Ubuntu：sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 或 libwebkit2gtk-4.0-0）"
