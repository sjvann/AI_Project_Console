#!/usr/bin/env bash
# 使用者目錄安裝。架構不符、找不到原生庫，或桌面依賴裝不上時以非 0 結束。
# 環境變數 AI_PROJECT_CONSOLE_NO_START=1 時不開啟視窗。
set -euo pipefail

APP_NAME="AI_Project 控制台"
EXE_NAME="AI_Project_Console"
NATIVE_NAME="Photino.Native.so"
MARKER="console-installed.marker"
PATH_MARKER="# ai-project-console user bin"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/AI_Project_Console"
BIN="${XDG_BIN_HOME:-$HOME/.local/bin}"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
ROOT="$(cd "$(dirname "$0")" && pwd)"
RELEASES="https://github.com/sjvann/AI_Project_Console/releases"

die() {
  echo "安裝未完成：$*" >&2
  exit 1
}

rid_for_machine() {
  case "$1" in
    x86_64|amd64) echo linux-x64 ;;
    aarch64|arm64) echo linux-arm64 ;;
    *) echo "" ;;
  esac
}

elf_rid() {
  local exe="$1"
  local magic class machine
  [[ -f "$exe" ]] || {
    echo "missing"
    return 0
  }
  magic="$(od -An -t x1 -N 4 -- "$exe" | tr -d ' \n')"
  if [[ "$magic" != "7f454c46" ]]; then
    echo "not-elf"
    return 0
  fi
  class="$(od -An -t u1 -j 4 -N 1 -- "$exe" | tr -d ' ')"
  if [[ "$class" != "2" ]]; then
    echo "not-64"
    return 0
  fi
  machine="$(od -An -t u2 -j 18 -N 2 -- "$exe" | tr -d ' ')"
  case "$machine" in
    62) echo "linux-x64" ;;
    183) echo "linux-arm64" ;;
    *) echo "elf-$machine" ;;
  esac
}

suggested_assets() {
  local host_rid="$1"
  local base ver
  base="$(basename "$ROOT")"
  if [[ "$base" =~ ^AI_Project_Console-(.+)-linux-(x64|arm64)$ ]]; then
    ver="${BASH_REMATCH[1]}"
    printf '%s\n' "AI_Project_Console-${ver}-${host_rid}.deb" "AI_Project_Console-${ver}-${host_rid}.zip"
  else
    printf '%s\n' "AI_Project_Console-*-${host_rid}.deb" "AI_Project_Console-*-${host_rid}.zip"
  fi
}

fail_arch() {
  local have="$1" host_rid="$2" host_machine="$3"
  local deb zip
  deb="$(suggested_assets "$host_rid" | sed -n '1p')"
  zip="$(suggested_assets "$host_rid" | sed -n '2p')"
  cat >&2 <<EOF
安裝未完成：這包是 ${have}，這台電腦是 ${host_machine}，需要 ${host_rid}。
直接執行 ./AI_Project_Console 會出現「無法執行二進位檔案：可執行檔格式錯誤」。

請改下載與這台電腦相符的套件（${RELEASES}）：
  ${deb}
  ${zip}

Ubuntu 請優先用 .deb，apt 會一併安裝 GTK 與 WebKit：
  sudo apt install ./${deb}

處理器對照：uname -m 顯示 x86_64 用 linux-x64；顯示 aarch64 用 linux-arm64。
EOF
  if [[ -f "$DEST/$EXE_NAME" ]]; then
    local installed
    installed="$(elf_rid "$DEST/$EXE_NAME")"
    if [[ "$installed" == linux-* && "$installed" != "$host_rid" ]]; then
      echo "先前裝到 ${DEST} 的也是 ${installed}，同樣無法執行。請改用 ${host_rid} 的安裝包再執行 ./install.sh。" >&2
    fi
  fi
  exit 1
}

require_arch() {
  local exe="$1" label="$2"
  local have host_machine host_rid
  have="$(elf_rid "$exe")"
  host_machine="$(uname -m)"
  host_rid="$(rid_for_machine "$host_machine")"
  if [[ -z "$host_rid" ]]; then
    die "不支援的處理器 ${host_machine}。控制台只提供 linux-x64 與 linux-arm64。"
  fi
  case "$have" in
    missing) die "找不到 ${label}。" ;;
    not-elf)
      die "${label} 不是 Linux 執行檔。請重新解壓 zip，不要在壓縮檔瀏覽器裡直接開啟。"
      ;;
    not-64) die "${label} 不是 64 位元 Linux 執行檔。" ;;
    linux-x64|linux-arm64)
      if [[ "$have" != "$host_rid" ]]; then
        fail_arch "$have" "$host_rid" "$host_machine"
      fi
      ;;
    *) die "${label} 的架構無法辨識（${have}）。" ;;
  esac
}

find_native() {
  local root="$1" found=""
  if [[ -f "$root/$NATIVE_NAME" ]]; then
    echo "$root/$NATIVE_NAME"
    return 0
  fi
  found="$(find "$root" -name "$NATIVE_NAME" -type f -print -quit)"
  printf '%s' "$found"
}

apt_has() {
  command -v apt-cache >/dev/null 2>&1 && apt-cache show "$1" >/dev/null 2>&1
}

package_for_soname() {
  case "$1" in
    libgtk-3.so.0)
      if apt_has libgtk-3-0t64; then echo libgtk-3-0t64
      elif apt_has libgtk-3-0; then echo libgtk-3-0
      else echo libgtk-3-0t64
      fi
      ;;
    libwebkit2gtk-4.1.so.0) echo libwebkit2gtk-4.1-0 ;;
    libjavascriptcoregtk-4.1.so.0) echo libjavascriptcoregtk-4.1-0 ;;
    libnotify.so.4) echo libnotify4 ;;
    libglib-2.0.so.0|libgobject-2.0.so.0|libgio-2.0.so.0)
      if apt_has libglib2.0-0t64; then echo libglib2.0-0t64
      else echo libglib2.0-0
      fi
      ;;
    libsoup-3.0.so.0) echo libsoup-3.0-0 ;;
    libstdc++.so.6) echo libstdc++6 ;;
    libgcc_s.so.1)
      if apt_has libgcc-s1; then echo libgcc-s1
      else echo libgcc1
      fi
      ;;
    libayatana-appindicator3.so.1) echo libayatana-appindicator3-1 ;;
    *) return 1 ;;
  esac
}

ldd_text() {
  ldd "$1" 2>&1 || true
}

missing_sonames() {
  local text="$1"
  printf '%s\n' "$text" | awk '/=> not found$/ { print $1 }'
}

run_apt() {
  if [[ "$(id -u)" -eq 0 ]]; then
    env DEBIAN_FRONTEND=noninteractive apt-get "$@"
  else
    sudo env DEBIAN_FRONTEND=noninteractive apt-get "$@"
  fi
}

ensure_shared_libs() {
  local so="$1" exe="$2"
  local so_text exe_text missing soname pkg seen=" " p cand
  local -a pkgs=() unique=()

  if ! command -v ldd >/dev/null 2>&1; then
    die "找不到 ldd，無法確認 GTK／WebKit 是否已安裝。請安裝 libc-bin 後再執行 ./install.sh。"
  fi

  so_text="$(ldd_text "$so")"
  exe_text="$(ldd_text "$exe")"
  if ! printf '%s\n' "$so_text" | grep -q '=>'; then
    die "無法讀取 ${NATIVE_NAME} 的共享庫。ldd 輸出：${so_text}"
  fi

  missing="$(printf '%s\n%s\n' "$(missing_sonames "$so_text")" "$(missing_sonames "$exe_text")" | awk 'NF && !seen[$0]++')"
  if [[ -z "$missing" ]]; then
    return 0
  fi

  if ! command -v apt-get >/dev/null 2>&1; then
    die "缺少共享庫：$(echo "$missing" | tr '\n' ' ')。請安裝 GTK 3、libnotify 與 libwebkit2gtk-4.1.so.0 後再執行 ./install.sh。"
  fi

  while IFS= read -r soname; do
    [[ -z "$soname" ]] && continue
    if pkg="$(package_for_soname "$soname")"; then
      pkgs+=("$pkg")
    fi
  done <<<"$missing"

  if [[ ${#pkgs[@]} -eq 0 ]]; then
    die "缺少共享庫：$(echo "$missing" | tr '\n' ' ')。請用套件管理員安裝後再執行 ./install.sh。"
  fi

  for p in "${pkgs[@]}"; do
    if [[ "$seen" != *" $p "* ]]; then
      unique+=("$p")
      seen+=" $p "
    fi
  done

  echo "缺少桌面元件：${unique[*]}"
  echo "先更新套件清單，再安裝此發行版套件庫中的最新版（不鎖定舊版號）。"
  echo "若系統詢問密碼，那是 apt 安裝 GTK／WebKit，不是控制台的帳號。"
  if ! run_apt update; then
    die "無法更新套件清單，已停止，避免裝到過期套件。請檢查網路後再執行 ./install.sh。"
  fi
  for p in "${unique[@]}"; do
    cand="$(apt-cache policy "$p" 2>/dev/null | awk '/Candidate:/ { print $2; exit }')"
    if [[ -n "$cand" && "$cand" != "(none)" ]]; then
      echo "  ${p} ${cand}"
    fi
  done
  if ! run_apt install -y "${unique[@]}"; then
    die "無法安裝：${unique[*]}。可手動執行： sudo apt-get update && sudo apt-get install -y ${unique[*]}"
  fi
  if command -v ldconfig >/dev/null 2>&1; then
    if [[ "$(id -u)" -eq 0 ]]; then
      ldconfig || true
    else
      sudo ldconfig || true
    fi
  fi

  so_text="$(ldd_text "$so")"
  exe_text="$(ldd_text "$exe")"
  missing="$(printf '%s\n%s\n' "$(missing_sonames "$so_text")" "$(missing_sonames "$exe_text")" | awk 'NF && !seen[$0]++')"
  if [[ -n "$missing" ]]; then
    die "套件安裝後仍缺少共享庫：$(echo "$missing" | tr '\n' ' ')"
  fi
}

ensure_user_path() {
  local rc snippet
  local -a rcs=("$HOME/.bashrc" "$HOME/.profile")
  snippet="$(cat <<'EOF'
# ai-project-console user bin
case ":$PATH:" in
  *":$HOME/.local/bin:"*) ;;
  *) PATH="$HOME/.local/bin:$PATH" ;;
esac
EOF
)"
  if [[ "$(basename "${SHELL:-bash}")" == "zsh" ]]; then
    rcs+=("$HOME/.zshrc")
  fi
  for rc in "${rcs[@]}"; do
    if [[ -f "$rc" ]] && grep -qF "$PATH_MARKER" "$rc"; then
      continue
    fi
    if [[ -e "$rc" && ! -w "$rc" ]]; then
      echo "無法寫入 $rc。這個終端機請執行： export PATH=\"$BIN:\$PATH\"" >&2
      continue
    fi
    touch "$rc"
    printf '\n%s\n' "$snippet" >> "$rc"
  done
}

place_desktop_launcher() {
  local src="$1" desktop_dir file
  desktop_dir=""
  if [[ -f "$HOME/.config/user-dirs.dirs" ]]; then
    desktop_dir="$(sed -n 's/^XDG_DESKTOP_DIR="\(.*\)"/\1/p' "$HOME/.config/user-dirs.dirs" | head -n 1)"
    desktop_dir="${desktop_dir//\$HOME/$HOME}"
    desktop_dir="${desktop_dir//\"/}"
  fi
  if [[ -z "$desktop_dir" ]]; then
    if [[ -d "$HOME/桌面" ]]; then
      desktop_dir="$HOME/桌面"
    else
      desktop_dir="$HOME/Desktop"
    fi
  fi
  mkdir -p "$desktop_dir"
  file="$desktop_dir/${APP_NAME}.desktop"
  cp "$src" "$file"
  chmod 755 "$file"
  if command -v gio >/dev/null 2>&1; then
    gio set "$file" metadata::trusted true >/dev/null 2>&1 || true
  fi
  printf '%s\n' "$file"
}

start_app() {
  local log pid
  if [[ "${AI_PROJECT_CONSOLE_NO_START:-}" == "1" ]]; then
    return 0
  fi
  if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    echo "這次沒有圖形桌面。請在桌面工作階段執行："
    echo "  $DEST/$EXE_NAME"
    return 0
  fi
  echo "正在開啟 ${APP_NAME}…"
  log="$(mktemp)"
  nohup "$DEST/$EXE_NAME" >"$log" 2>&1 &
  pid=$!
  sleep 2
  if ! kill -0 "$pid" 2>/dev/null; then
    echo "安裝未完成：程式啟動後立刻結束。" >&2
    if [[ -s "$log" ]]; then
      echo "----- 程式輸出 -----" >&2
      cat "$log" >&2
    fi
    rm -f "$log"
    exit 1
  fi
  rm -f "$log"
  echo "已開啟（行程 ${pid}）。"
}

main() {
  local exe native icon_src icon_path desktop_icon=""
  exe="$ROOT/$EXE_NAME"
  if [[ ! -f "$exe" ]]; then
    die "找不到 ${EXE_NAME}。請先解壓 linux zip，再在該目錄執行 ./install.sh。"
  fi

  require_arch "$exe" "$EXE_NAME"
  native="$(find_native "$ROOT")"
  if [[ -z "$native" || ! -f "$native" ]]; then
    die "找不到 ${NATIVE_NAME}。請重新解壓官方 zip，不要只複製主程式。"
  fi
  require_arch "$native" "$NATIVE_NAME"
  ensure_shared_libs "$native" "$exe"

  mkdir -p "$DEST" "$BIN" "$APPS" "$ICON_DIR"
  cp -R "$ROOT"/. "$DEST"/
  chmod +x "$DEST/$EXE_NAME" "$exe"
  printf 'installed\n' > "$DEST/$MARKER"
  ln -sfn "$DEST/$EXE_NAME" "$BIN/ai-project-console"

  icon_src=""
  for cand in \
    "$DEST/wwwroot/img/logo.png" \
    "$DEST/wwwroot/favicon.ico" \
    "$DEST/Assets/app.ico"
  do
    if [[ -f "$cand" ]]; then
      icon_src="$cand"
      break
    fi
  done
  icon_path="$DEST/$EXE_NAME"
  if [[ -n "$icon_src" && "$icon_src" == *.png ]]; then
    cp "$icon_src" "$ICON_DIR/ai-project-console.png"
    icon_path="$ICON_DIR/ai-project-console.png"
  fi

  cat > "$APPS/ai-project-console.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=$APP_NAME
Comment=本機堆疊控制台：掃描、編譯、一鍵啟動
Exec=$DEST/$EXE_NAME
Icon=$icon_path
Terminal=false
Categories=Development;
Keywords=AI;Project;控制台;Console;
StartupNotify=true
StartupWMClass=$EXE_NAME
EOF
  chmod +x "$APPS/ai-project-console.desktop"
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$APPS" >/dev/null 2>&1 || true
  fi
  desktop_icon="$(place_desktop_launcher "$APPS/ai-project-console.desktop")"

  ensure_user_path
  path_ready=0
  case ":${PATH:-}:" in
    *":$BIN:"*) path_ready=1 ;;
  esac

  start_app

  echo "已安裝到 $DEST"
  echo "應用程式選單：${APP_NAME}"
  if [[ -n "$desktop_icon" ]]; then
    echo "桌面圖示：$desktop_icon"
  fi
  if [[ "$path_ready" -eq 1 ]]; then
    echo "命令：ai-project-console"
  else
    echo "新開一個終端機後可執行 ai-project-console。"
    echo "這個終端機請先執行： export PATH=\"$BIN:\$PATH\""
    echo "或直接執行： $DEST/$EXE_NAME"
  fi
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
