#!/usr/bin/env bash
# 不需 root、不呼叫 apt。確認架構不符或缺原生庫時不會宣稱已安裝。
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
INSTALL="$HERE/install.sh"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

host_rid() {
  case "$(uname -m)" in
    x86_64|amd64) echo linux-x64 ;;
    aarch64|arm64) echo linux-arm64 ;;
    *) echo "" ;;
  esac
}

write_elf() {
  local machine_dec="$1" dest="$2"
  local lo hi
  lo=$(printf '%02x' $((machine_dec & 255)))
  hi=$(printf '%02x' $((machine_dec >> 8)))
  {
    printf '\177ELF\002\001\001\000'
    printf '\000%.0s' {1..10}
    printf "\\x${lo}\\x${hi}"
    printf '\000%.0s' {1..44}
  } >"$dest"
}

opposite_machine() {
  case "$(uname -m)" in
    x86_64|amd64) echo 183 ;;
    aarch64|arm64) echo 62 ;;
    *) echo "" ;;
  esac
}

same_machine() {
  case "$(uname -m)" in
    x86_64|amd64) echo 62 ;;
    aarch64|arm64) echo 183 ;;
    *) echo "" ;;
  esac
}

RID="$(host_rid)"
[[ -n "$RID" ]] || fail "此測試只在 x86_64 或 aarch64 上執行（目前 $(uname -m)）"

SANDBOX="$(mktemp -d)"
trap 'rm -rf "$SANDBOX"' EXIT
export HOME="$SANDBOX/home"
export AI_PROJECT_CONSOLE_NO_START=1
mkdir -p "$HOME"

echo "CASE:wrong-arch"
DIR="$SANDBOX/AI_Project_Console-9.9.9-linux-arm64"
mkdir -p "$DIR"
cp "$INSTALL" "$DIR/install.sh"
write_elf "$(opposite_machine)" "$DIR/AI_Project_Console"
set +e
OUT="$(bash "$DIR/install.sh" 2>&1)"
STATUS=$?
set -e
[[ "$STATUS" -ne 0 ]] || fail "架構不符仍成功：$OUT"
printf '%s\n' "$OUT" | grep -q "安裝未完成" || fail "沒有說明安裝未完成：$OUT"
printf '%s\n' "$OUT" | grep -q "AI_Project_Console-9.9.9-${RID}.deb" || fail "沒有指出正確套件：$OUT"
printf '%s\n' "$OUT" | grep -q "可執行檔格式錯誤" || fail "沒有對上格式錯誤：$OUT"
printf '%s\n' "$OUT" | grep -q "已安裝到" && fail "架構不符卻宣稱已安裝：$OUT" || true
[[ ! -e "$HOME/.local/share/AI_Project_Console" ]] || fail "架構不符仍寫入安裝目錄"

echo "CASE:missing-native"
DIR="$SANDBOX/good"
mkdir -p "$DIR"
cp "$INSTALL" "$DIR/install.sh"
write_elf "$(same_machine)" "$DIR/AI_Project_Console"
set +e
OUT="$(bash "$DIR/install.sh" 2>&1)"
STATUS=$?
set -e
[[ "$STATUS" -ne 0 ]] || fail "缺少原生庫仍成功：$OUT"
printf '%s\n' "$OUT" | grep -q "Photino.Native.so" || fail "沒有指出 Photino.Native.so：$OUT"
printf '%s\n' "$OUT" | grep -q "已安裝到" && fail "缺少原生庫卻宣稱已安裝：$OUT" || true

echo "CASE:not-elf"
DIR="$SANDBOX/text"
mkdir -p "$DIR"
cp "$INSTALL" "$DIR/install.sh"
printf 'not a binary\n' >"$DIR/AI_Project_Console"
set +e
OUT="$(bash "$DIR/install.sh" 2>&1)"
STATUS=$?
set -e
[[ "$STATUS" -ne 0 ]] || fail "非 ELF 仍成功：$OUT"
printf '%s\n' "$OUT" | grep -q "不是 Linux 執行檔" || fail "沒有拒絕非 ELF：$OUT"

echo "OK install.sh"
