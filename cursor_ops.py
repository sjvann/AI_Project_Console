"""透過 Cursor CLI 開啟專案／帶建置錯誤求助。"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys
from pathlib import Path

_ERROR_LINE = re.compile(r"\berror\b", re.IGNORECASE)
_MAX_LOG_CHARS = 24_000
_MAX_ERROR_LINES = 80


def resolve_cursor_cli() -> str | None:
    found = shutil.which("cursor")
    if found:
        return found
    if sys.platform == "win32":
        local = Path(os_localappdata()) / "Programs" / "cursor" / "resources" / "app" / "bin" / "cursor.cmd"
        if local.is_file():
            return str(local)
    return None


def os_localappdata() -> str:
    import os

    return os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")


def open_in_cursor(path: Path, *, reuse_window: bool = True, extra_paths: list[Path] | None = None) -> str | None:
    """開啟資料夾／檔案於 Cursor。成功回傳 None，失敗回傳錯誤說明。"""
    cli = resolve_cursor_cli()
    if not cli:
        return "找不到 Cursor CLI（請確認已安裝並把 cursor 加到 PATH）"
    target = path.resolve()
    if not target.exists():
        return f"路徑不存在：{target}"
    cmd = [cli]
    if reuse_window:
        cmd.append("--reuse-window")
    cmd.append(str(target if target.is_dir() else target.parent))
    for extra in extra_paths or []:
        if extra.is_file():
            cmd.append(str(extra.resolve()))
    try:
        subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except OSError as exc:
        return f"無法啟動 Cursor：{exc}"
    return None


def close_cursor() -> str | None:
    """關閉本機 Cursor 行程。成功回傳 None；無法執行時回傳錯誤說明。"""
    try:
        if sys.platform == "win32":
            # /T 一併結束子行程；找不到行程時 taskkill 結束碼非 0，可視為已關閉
            names = ("Cursor.exe", "cursor.exe")
            any_killed = False
            last_err = ""
            for name in names:
                proc = subprocess.run(
                    ["taskkill", "/IM", name, "/F", "/T"],
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    timeout=30,
                )
                out = ((proc.stdout or "") + (proc.stderr or "")).lower()
                if proc.returncode == 0:
                    any_killed = True
                elif "not found" not in out and "找不到" not in out:
                    last_err = (proc.stderr or proc.stdout or "").strip()
            if any_killed or not last_err:
                return None
            return last_err or "無法關閉 Cursor"
        if sys.platform == "darwin":
            proc = subprocess.run(
                ["osascript", "-e", 'tell application "Cursor" to quit'],
                capture_output=True,
                text=True,
                timeout=30,
            )
            if proc.returncode == 0:
                return None
            # 後備
            subprocess.run(["killall", "Cursor"], capture_output=True, timeout=15)
            return None
        subprocess.run(["pkill", "-f", "cursor"], capture_output=True, timeout=15)
        return None
    except (OSError, subprocess.TimeoutExpired) as exc:
        return f"關閉 Cursor 失敗：{exc}"


def extract_build_errors(log_text: str) -> list[str]:
    lines = [ln.rstrip() for ln in log_text.splitlines() if ln.strip()]
    errors = [ln for ln in lines if _ERROR_LINE.search(ln) and "error(s)" not in ln.lower()]
    if errors:
        return errors[:_MAX_ERROR_LINES]
    # 沒有明確 error 行時，取結尾摘要
    return lines[-min(40, len(lines)) :]


def build_help_markdown(*, root: Path, target: str, exit_code: int, log_text: str) -> str:
    errors = extract_build_errors(log_text)
    log_trim = log_text
    if len(log_trim) > _MAX_LOG_CHARS:
        log_trim = "…（前略）…\n" + log_trim[-_MAX_LOG_CHARS:]
    error_block = "\n".join(f"- `{e}`" for e in errors) if errors else "- （未能解析具體 error 行，請見下方完整輸出）"
    return (
        f"# 建置失敗 — 請 Cursor 協助修復\n\n"
        f"請閱讀以下 `dotnet build` 錯誤，找出根因並直接修改程式碼讓建置通過。"
        f"優先處理 error，不要只做說明。\n\n"
        f"## 環境\n\n"
        f"- 專案根目錄：`{root}`\n"
        f"- 建置目標：`{target}`\n"
        f"- 結束碼：`{exit_code}`\n\n"
        f"## 錯誤摘要\n\n"
        f"{error_block}\n\n"
        f"## 完整建置輸出\n\n"
        f"```text\n{log_trim.rstrip()}\n```\n"
    )


def write_build_help(
    reports_dir: Path,
    *,
    root: Path,
    target: str,
    exit_code: int,
    log_text: str,
) -> Path:
    reports_dir.mkdir(parents=True, exist_ok=True)
    help_path = reports_dir / "cursor-help.md"
    help_path.write_text(
        build_help_markdown(root=root, target=target, exit_code=exit_code, log_text=log_text),
        encoding="utf-8",
    )
    (reports_dir / "last-build.log").write_text(log_text, encoding="utf-8")
    return help_path


def agent_prompt_from_help(help_path: Path) -> str:
    return (
        f"建置失敗了，請依 `{help_path.as_posix()}` 的錯誤摘要與完整輸出修復，"
        f"改完後用同樣目標再確認能建置成功。"
    )


def ask_cursor_for_build_help(
    root: Path,
    help_path: Path,
) -> str | None:
    """開啟專案與求助檔；成功回傳 None。"""
    return open_in_cursor(root, reuse_window=True, extra_paths=[help_path])


_IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"}


def uat_help_markdown(
    *,
    root: Path,
    title: str,
    description: str,
    image_paths: list[Path],
    report_dir: Path,
) -> str:
    desc = description.strip() or "（未填寫說明）"
    if image_paths:
        img_lines = []
        for i, path in enumerate(image_paths, start=1):
            rel = path.name
            img_lines.append(f"### 截圖 {i}：`{rel}`\n\n![{rel}]({rel})\n")
        images_block = "\n".join(img_lines)
    else:
        images_block = "（未附截圖）\n"
    return (
        f"# UAT 問題 — 請 Cursor 協助排查／修復\n\n"
        f"這是使用者在 UAT 測試時回報的問題。請依說明與截圖找出根因並直接修改程式碼；"
        f"優先修復，不要只做說明。\n\n"
        f"## 環境\n\n"
        f"- 專案根目錄：`{root}`\n"
        f"- 回報目錄：`{report_dir}`\n"
        f"- 標題：{title.strip() or '（未命名）'}\n\n"
        f"## 問題說明\n\n"
        f"{desc}\n\n"
        f"## 截圖\n\n"
        f"{images_block}\n"
        f"## 請你做的事\n\n"
        f"1. 閱讀上方說明與截圖（圖檔與本 md 同目錄）。\n"
        f"2. 定位相關程式並修復。\n"
        f"3. 簡短說明改了什麼、如何驗證。\n"
    )


def write_uat_help(
    reports_dir: Path,
    *,
    root: Path,
    title: str,
    description: str,
    source_images: list[Path],
) -> tuple[Path, Path, list[Path]]:
    """寫入 UAT 求助報告。回傳 (help_md, report_dir, copied_images)。"""
    from datetime import datetime
    import shutil

    reports_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    safe_title = re.sub(r"[^\w\u4e00-\u9fff\-]+", "-", (title or "uat").strip())[:40].strip("-") or "uat"
    report_dir = reports_dir / f"{stamp}-{safe_title}"
    report_dir.mkdir(parents=True, exist_ok=True)

    copied: list[Path] = []
    for i, src in enumerate(source_images, start=1):
        src = Path(src)
        if not src.is_file():
            continue
        ext = src.suffix.lower() if src.suffix.lower() in _IMAGE_EXTS else ".png"
        dest = report_dir / f"screenshot-{i:02d}{ext}"
        shutil.copy2(src, dest)
        copied.append(dest)

    help_path = report_dir / "cursor-uat-help.md"
    help_path.write_text(
        uat_help_markdown(
            root=root,
            title=title,
            description=description,
            image_paths=copied,
            report_dir=report_dir,
        ),
        encoding="utf-8",
    )
    return help_path, report_dir, copied


def agent_prompt_from_uat_help(help_path: Path, image_paths: list[Path]) -> str:
    """給 Cursor IDE Agent 手動貼上用的短提示。"""
    img_hint = ""
    if image_paths:
        listed = "、".join(f"`{p.as_posix()}`" for p in image_paths[:8])
        img_hint = f" 請一併查看截圖：{listed}。"
    return (
        f"UAT 測試發現問題，請依 `{help_path.as_posix()}` 的說明與截圖排查並修復。"
        f"{img_hint}"
        f"改完後簡短說明如何驗證。"
    )


def ask_cursor_for_uat_help(
    root: Path,
    help_path: Path,
    image_paths: list[Path] | None = None,
) -> str | None:
    """開啟專案、求助 md 與截圖檔；成功回傳 None。"""
    extras = [help_path, *(image_paths or [])]
    return open_in_cursor(root, reuse_window=True, extra_paths=extras)
