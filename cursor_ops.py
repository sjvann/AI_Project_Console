"""透過 Cursor CLI 開啟專案，並以 prompt deeplink 建立 New Agent。"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import time
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


def open_in_cursor(path: Path, *, reuse_window: bool = False, extra_paths: list[Path] | None = None) -> str | None:
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


def close_cursor(workspace_root: Path | str | None = None) -> str | None:
    """關閉此方案的 Cursor 視窗。未指定工作區時才結束整份 Cursor。"""
    root = Path(workspace_root) if workspace_root else None
    try:
        if root is not None:
            return _close_workspace_window(root)
        if sys.platform == "win32":
            return _close_cursor_windows()
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


def _title_matches(title: str, workspace_root: Path) -> bool:
    name = workspace_root.name
    if not title or not name:
        return False
    full = str(workspace_root.resolve())
    if full.lower() in title.lower():
        return True
    parts = [p.strip().lstrip("●* ").strip() for p in title.split(" - ")]
    return any(p.lower() == name.lower() or p.lower() == full.lower() for p in parts if p)


def _close_workspace_window(workspace_root: Path) -> str | None:
    """只關這個方案的 Cursor 視窗，不結束其他專案。"""
    if sys.platform == "win32":
        _close_matching_windows_win32(workspace_root)
        return None
    if sys.platform == "darwin":
        name = workspace_root.name.replace("\\", "\\\\").replace('"', '\\"')
        script = (
            'tell application "System Events"\n'
            '  if not (exists process "Cursor") then return\n'
            '  tell process "Cursor"\n'
            "    repeat with w in (get windows)\n"
            "      set wName to name of w as text\n"
            f'      if (wName contains " - {name} - ") or (wName starts with "{name} - ") then\n'
            "        try\n"
            '          click (first button of w whose subrole is "AXCloseButton")\n'
            "        end try\n"
            "      end if\n"
            "    end repeat\n"
            "  end tell\n"
            "end tell\n"
        )
        subprocess.run(["osascript"], input=script, capture_output=True, text=True, timeout=15)
        return None
    try:
        listed = subprocess.run(["wmctrl", "-l"], capture_output=True, text=True, timeout=8)
        for line in (listed.stdout or "").splitlines():
            parts = line.split(None, 3)
            if len(parts) < 4:
                continue
            if _title_matches(parts[3], workspace_root):
                subprocess.run(["wmctrl", "-ic", parts[0]], capture_output=True, timeout=5)
    except (OSError, subprocess.TimeoutExpired):
        pass
    return None


def _close_matching_windows_win32(workspace_root: Path) -> None:
    import ctypes
    from ctypes import wintypes

    user32 = ctypes.windll.user32  # type: ignore[attr-defined]
    EnumWindows = user32.EnumWindows
    GetWindowTextW = user32.GetWindowTextW
    GetWindowTextLengthW = user32.GetWindowTextLengthW
    IsWindowVisible = user32.IsWindowVisible
    GetWindowThreadProcessId = user32.GetWindowThreadProcessId
    PostMessageW = user32.PostMessageW
    GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    GetWindowTextLengthW.argtypes = [wintypes.HWND]
    IsWindowVisible.argtypes = [wintypes.HWND]
    PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def _enum(hwnd, _lparam):
        if not IsWindowVisible(hwnd):
            return True
        length = GetWindowTextLengthW(hwnd)
        if length <= 0:
            return True
        buf = ctypes.create_unicode_buffer(length + 1)
        GetWindowTextW(hwnd, buf, length + 1)
        title = buf.value or ""
        if not _title_matches(title, workspace_root):
            return True
        pid = wintypes.DWORD()
        GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        try:
            proc = subprocess.run(
                ["tasklist", "/FI", f"PID eq {pid.value}", "/NH", "/FO", "CSV"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=5,
            )
            blob = (proc.stdout or "").lower()
            if "cursor.exe" not in blob:
                return True
        except (OSError, subprocess.TimeoutExpired):
            return True
        PostMessageW(hwnd, 0x0010, 0, 0)  # WM_CLOSE
        return True

    cb = WNDENUMPROC(_enum)
    EnumWindows(cb, 0)


def _close_cursor_windows() -> str | None:
    """以 WM_CLOSE 正常關閉 Cursor。不可 taskkill /F：強制結束渲染行程會讓主行程
    跳出「The window terminated unexpectedly (reason: 'crashed', code: '-1')」。"""
    subprocess.run(
        ["taskkill", "/IM", "Cursor.exe"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=15,
    )
    _wait_cursor_exit(8.0)
    return None


def _wait_cursor_exit(seconds: float) -> bool:
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if not is_cursor_running():
            return True
        time.sleep(0.25)
    return not is_cursor_running()


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
    """先把此方案開在／聚焦到 Cursor 視窗，再送 New Agent deeplink。"""
    return open_in_cursor(root)
