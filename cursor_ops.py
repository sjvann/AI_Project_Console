"""透過 Cursor CLI 開啟專案，並以 prompt deeplink 建立 New Agent。"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from urllib.parse import urlencode

_ERROR_LINE = re.compile(r"\berror\b", re.IGNORECASE)
_MAX_LOG_CHARS = 8_000
_MAX_ERROR_LINES = 80
_DEEPLINK_BASE = "cursor://anysphere.cursor-deeplink/prompt"
# Windows ShellExecute 對自訂協定較嚴；Cursor 文件上限 8000
_DEEPLINK_MAX_CHARS = 2048


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
    return os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")


def is_cursor_running() -> bool:
    try:
        if sys.platform == "win32":
            proc = subprocess.run(
                ["tasklist", "/FI", "IMAGENAME eq Cursor.exe", "/NH"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=10,
            )
            return "cursor.exe" in (proc.stdout or "").lower()
        if sys.platform == "darwin":
            proc = subprocess.run(["pgrep", "-x", "Cursor"], capture_output=True, timeout=10)
            return proc.returncode == 0
        proc = subprocess.run(["pgrep", "-f", "cursor"], capture_output=True, timeout=10)
        return proc.returncode == 0
    except (OSError, subprocess.TimeoutExpired):
        return False


def new_agent_launch_delay_ms() -> int:
    """Cursor 已在跑時稍等即可；剛啟動則多等一會再送 deeplink。"""
    return 400 if is_cursor_running() else 2200


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
    return lines[-min(40, len(lines)) :]


def build_agent_prompt(*, root: Path, target: str, exit_code: int, log_text: str) -> str:
    """建置求救：錯誤內容直接寫進 New Agent 提示，不經過求助檔。"""
    errors = extract_build_errors(log_text)
    log_trim = log_text
    if len(log_trim) > _MAX_LOG_CHARS:
        log_trim = "…（前略）…\n" + log_trim[-_MAX_LOG_CHARS:]
    error_block = "\n".join(f"- {e}" for e in errors) if errors else "- （未能解析具體 error 行，請見下方輸出）"
    return (
        "建置失敗，請找出根因並直接修改程式碼讓建置通過。優先處理 error，不要只做說明。"
        "改完後用同樣目標再確認能建置成功。\n\n"
        f"專案根目錄：{root}\n"
        f"建置目標：{target}\n"
        f"結束碼：{exit_code}\n\n"
        f"錯誤摘要：\n{error_block}\n\n"
        f"完整建置輸出：\n```text\n{log_trim.rstrip()}\n```\n"
    )


_IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"}


def stage_uat_screenshots(
    reports_dir: Path,
    *,
    title: str,
    source_images: list[Path],
) -> tuple[Path, list[Path]]:
    """只保存截圖（給 Agent 讀圖用），不寫求助 md。回傳 (report_dir, copied_images)。"""
    from datetime import datetime

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
    return report_dir, copied


def build_uat_agent_prompt(
    *,
    root: Path,
    title: str,
    description: str,
    image_paths: list[Path],
) -> str:
    """UAT 求救：說明直接寫進 New Agent 提示。"""
    desc = description.strip() or "（未填寫說明）"
    lines = [
        "UAT 測試發現問題。請依下列說明與截圖找出根因並直接修改程式碼；優先修復，不要只做說明。",
        "改完後簡短說明改了什麼、如何驗證。",
        "",
        f"專案根目錄：{root}",
        f"標題：{title.strip() or '（未命名）'}",
        "",
        "問題說明：",
        desc,
    ]
    if image_paths:
        lines.append("")
        lines.append("請用 Read 查看這些截圖：")
        for path in image_paths[:8]:
            lines.append(f"- {path.resolve().as_posix()}")
    return "\n".join(lines) + "\n"


def prompt_deeplink_url(prompt_text: str) -> str:
    return f"{_DEEPLINK_BASE}?{urlencode({'text': prompt_text})}"


def fit_prompt_for_deeplink(prompt: str, *, limit: int = _DEEPLINK_MAX_CHARS) -> str:
    """截到 deeplink URL 長度限制以內。"""
    suffix = "\n\n…（內容過長，已截斷；完整內容在剪貼簿，可於 New Agent 貼上）"
    if len(prompt_deeplink_url(prompt)) <= limit:
        return prompt
    lo, hi = 0, len(prompt)
    best = suffix
    while lo <= hi:
        mid = (lo + hi) // 2
        cand = prompt[:mid].rstrip() + suffix
        if len(prompt_deeplink_url(cand)) <= limit:
            best = cand
            lo = mid + 1
        else:
            hi = mid - 1
    return best


def open_prompt_deeplink(prompt: str) -> str | None:
    """開啟 Cursor prompt deeplink（會跳出確認視窗，確認後建立 New Agent）。"""
    url = prompt_deeplink_url(fit_prompt_for_deeplink(prompt))
    try:
        if sys.platform == "win32":
            os.startfile(url)  # type: ignore[attr-defined]
        elif sys.platform == "darwin":
            subprocess.Popen(["open", url])
        else:
            subprocess.Popen(["xdg-open", url])
    except OSError as exc:
        return f"無法開啟 Cursor New Agent：{exc}"
    return None


def open_project_for_new_agent(root: Path) -> str | None:
    """先把專案開在 Cursor（Agents 視窗若已開，CLI 會再建一個 New Agent）。"""
    return open_in_cursor(root, reuse_window=True)
