"""工具列：專案資訊（左）與選擇目錄（右）。"""

from __future__ import annotations

import tkinter as tk
from collections.abc import Callable
from pathlib import Path
from tkinter import filedialog

from ..runtime import recent_projects
from ..service_catalog import ProjectCatalog
from .buttons import make_button, make_menubutton

C = {
    "panel": "#f7f9fb",
    "text": "#1c2430",
    "muted": "#5a6a7a",
    "border": "#c5ced8",
    "accent": "#0b6e56",
    "accent_hi": "#0a5c48",
    "row_alt": "#eef2f6",
    "danger": "#9b2c2c",
    "danger_hi": "#7f2424",
    "danger_bg": "#fff5f5",
    "danger_bg_hi": "#fde8e8",
}
_UI = "Segoe UI"
F = {
    "ui": (_UI, 12),
    "ui_sm": (_UI, 11),
    "ui_bold": (_UI, 13, "bold"),
    "section": (_UI, 11, "bold"),
    "btn": (_UI, 12, "bold"),
    "btn_sm": (_UI, 11, "bold"),
}
_HISTORY_EMPTY = "（尚無歷史專案）"


def _history_label(path: Path) -> str:
    name = path.name or str(path)
    parent = str(path.parent)
    if len(parent) > 48:
        parent = "…" + parent[-46:]
    return f"{name}  —  {parent}"


class ProjectBar(tk.Frame):
    def __init__(
        self,
        parent: tk.Widget,
        *,
        on_pick: Callable[[Path], None],
        open_with_cursor: bool = True,
        on_open_with_cursor_change: Callable[[bool], None] | None = None,
        colors: dict[str, str] | None = None,
        fonts: dict | None = None,
    ) -> None:
        colors = {**C, **(colors or {})}
        fonts = {**F, **(fonts or {})}
        super().__init__(parent, bg=colors["panel"])
        self._on_pick = on_pick
        self._on_open_with_cursor_change = on_open_with_cursor_change
        self._colors = colors
        self._fonts = fonts
        self._history_paths: list[Path] = []
        self.name_var = tk.StringVar(value="尚未選擇專案")
        self.path_var = tk.StringVar(value="請從右側選擇或載入專案目錄")
        self.summary_var = tk.StringVar(value="")
        self.open_with_cursor = tk.BooleanVar(value=open_with_cursor)

        self.columnconfigure(0, weight=1, uniform="proj")
        self.columnconfigure(1, weight=0)
        self.columnconfigure(2, weight=1, uniform="proj")

        # —— 左：兩行資訊 ——
        left = tk.Frame(self, bg=colors["panel"])
        left.grid(row=0, column=0, sticky="nsew", padx=(0, 12))

        row1 = tk.Frame(left, bg=colors["panel"])
        row1.pack(anchor=tk.W, fill=tk.X)
        tk.Label(
            row1, text="目前專案", bg=colors["panel"], fg=colors["muted"],
            font=fonts["section"],
        ).pack(side=tk.LEFT)
        tk.Label(
            row1, textvariable=self.name_var, bg=colors["panel"], fg=colors["text"],
            font=fonts["ui_bold"],
        ).pack(side=tk.LEFT, padx=(8, 0))

        row2 = tk.Frame(left, bg=colors["panel"])
        row2.pack(anchor=tk.W, fill=tk.X, pady=(4, 0))
        tk.Label(
            row2, textvariable=self.path_var, bg=colors["panel"], fg=colors["muted"],
            font=fonts["ui_sm"],
        ).pack(side=tk.LEFT)
        tk.Label(
            row2, textvariable=self.summary_var, bg=colors["panel"], fg=colors["muted"],
            font=fonts["ui_sm"],
        ).pack(side=tk.LEFT, padx=(12, 0))

        # —— 分隔線 ——
        tk.Frame(self, bg=colors["border"], width=1).grid(row=0, column=1, sticky="ns", padx=8)

        # —— 右：選擇專案（同一列） ——
        right = tk.Frame(self, bg=colors["panel"])
        right.grid(row=0, column=2, sticky="nsew", padx=(14, 0))

        actions = tk.Frame(right, bg=colors["panel"])
        actions.pack(anchor=tk.W, fill=tk.X)

        make_button(
            actions,
            "選擇專案目錄…",
            self._pick,
            variant="primary",
            colors=colors,
            fonts=fonts,
        ).pack(side=tk.LEFT)

        self._history_btn = make_menubutton(
            actions, "歷史專案", colors=colors, fonts=fonts,
        )
        self._history_btn.pack(side=tk.LEFT, padx=(8, 0))

        tk.Checkbutton(
            actions,
            text="同時開啟 Cursor",
            variable=self.open_with_cursor,
            command=self._on_toggle_cursor,
            bg=colors["panel"],
            fg=colors["text"],
            activebackground=colors["panel"],
            activeforeground=colors["text"],
            selectcolor=colors["panel"],
            font=fonts["ui_sm"],
            highlightthickness=0,
        ).pack(side=tk.LEFT, padx=(16, 0))

        self.refresh_history()

    def _on_toggle_cursor(self) -> None:
        if self._on_open_with_cursor_change:
            self._on_open_with_cursor_change(bool(self.open_with_cursor.get()))

    def _pick(self) -> None:
        chosen = filedialog.askdirectory(title="選擇專案目錄")
        if chosen:
            self._on_pick(Path(chosen))

    def _pick_history(self, path: Path) -> None:
        self._on_pick(path)

    def refresh_history(self, *, current: Path | None = None) -> None:
        paths = recent_projects()
        self._history_paths = paths
        menu = self._history_btn.menu  # type: ignore[attr-defined]
        menu.delete(0, tk.END)
        if not paths:
            menu.add_command(label=_HISTORY_EMPTY, state=tk.DISABLED)
            return
        for path in paths:
            label = _history_label(path)
            menu.add_command(label=label, command=lambda p=path: self._pick_history(p))
        _ = current

    def set_catalog(self, catalog: ProjectCatalog | None) -> None:
        if catalog is None:
            self.name_var.set("尚未選擇專案")
            self.path_var.set("請從右側選擇或載入專案目錄")
            self.summary_var.set("")
            self.refresh_history()
            return
        self.name_var.set(catalog.name)
        self.path_var.set(str(catalog.root))
        self.summary_var.set(catalog.summary)
        self.refresh_history(current=catalog.root)
