"""選定專案根下的 .ai_project 執行期目錄，以及控制台本機 settings。"""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

RUNTIME_DIR_NAME = ".ai_project"
SETTINGS_DIR_NAME = "AI_Project_Console"
SETTINGS_FILE = "settings.json"


def console_settings_path() -> Path:
    if os.name == "nt":
        base = Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local")
        return base / SETTINGS_DIR_NAME / SETTINGS_FILE
    xdg = os.environ.get("XDG_CONFIG_HOME")
    base = Path(xdg) if xdg else Path.home() / ".config"
    return base / SETTINGS_DIR_NAME / SETTINGS_FILE


def load_console_settings() -> dict[str, Any]:
    path = console_settings_path()
    if not path.is_file():
        return {}
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return doc if isinstance(doc, dict) else {}


def save_console_settings(data: dict[str, Any]) -> None:
    path = console_settings_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def remember_project(root: Path) -> None:
    data = load_console_settings()
    root_s = str(root.resolve())
    recent = [r for r in (data.get("recentProjects") or []) if isinstance(r, str) and r != root_s]
    recent.insert(0, root_s)
    data["recentProjects"] = recent[:12]
    data["lastProject"] = root_s
    save_console_settings(data)


def recent_projects(*, limit: int = 12) -> list[Path]:
    """回傳仍存在的歷史專案目錄（新到舊）。"""
    data = load_console_settings()
    out: list[Path] = []
    seen: set[str] = set()
    for raw in data.get("recentProjects") or []:
        if not isinstance(raw, str):
            continue
        path = Path(raw)
        if not path.is_dir():
            continue
        key = str(path.resolve())
        if key in seen:
            continue
        seen.add(key)
        out.append(path.resolve())
        if len(out) >= limit:
            break
    return out


def last_project() -> Path | None:
    recent = recent_projects(limit=1)
    return recent[0] if recent else None


def get_open_with_cursor() -> bool:
    data = load_console_settings()
    val = data.get("openWithCursor")
    return True if val is None else bool(val)


def set_open_with_cursor(enabled: bool) -> None:
    data = load_console_settings()
    data["openWithCursor"] = bool(enabled)
    save_console_settings(data)


class ProjectRuntime:
    def __init__(self, root: Path) -> None:
        self.root = root.resolve()
        self.base = self.root / RUNTIME_DIR_NAME
        self.logs = self.base / "logs"
        self.pids = self.base / "pids"
        self.build_reports = self.base / "build-reports"
        self.uat_reports = self.base / "uat-reports"

    def ensure(self) -> None:
        self.logs.mkdir(parents=True, exist_ok=True)
        self.pids.mkdir(parents=True, exist_ok=True)
        self.build_reports.mkdir(parents=True, exist_ok=True)
        self.uat_reports.mkdir(parents=True, exist_ok=True)

    def log_path(self, stem: str) -> Path:
        self.ensure()
        return self.logs / f"{stem}.log"

    def pid_path(self, stem: str) -> Path:
        self.ensure()
        return self.pids / f"{stem}.pid"
