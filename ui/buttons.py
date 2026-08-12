"""統一按鈕樣式。"""

from __future__ import annotations

import tkinter as tk
from collections.abc import Callable
from typing import Any

# 與 app.C / F 對齊的預設；呼叫端可覆寫
DEFAULT_COLORS = {
    "panel": "#f7f9fb",
    "border": "#c5ced8",
    "text": "#1c2430",
    "muted": "#5a6a7a",
    "accent": "#0b6e56",
    "accent_hi": "#0a5c48",
    "danger": "#9b2c2c",
    "danger_hi": "#7f2424",
    "row_alt": "#eef2f6",
    "danger_bg": "#fff5f5",
    "danger_bg_hi": "#fde8e8",
}
_UI = "Segoe UI"
DEFAULT_FONTS = {
    "btn": (_UI, 12, "bold"),
    "btn_sm": (_UI, 11, "bold"),
}

# 統一高度感：相同 padding
_PAD = {"padx": 14, "pady": 7}
_PAD_SM = {"padx": 10, "pady": 5}


def _base_kwargs(colors: dict[str, str], fonts: dict, *, small: bool) -> dict[str, Any]:
    pad = _PAD_SM if small else _PAD
    return {
        "relief": tk.SOLID,
        "borderwidth": 1,
        "cursor": "hand2",
        "font": fonts["btn_sm"] if small else fonts["btn"],
        **pad,
    }


def make_button(
    parent: tk.Widget,
    text: str,
    command: Callable[[], None] | None,
    *,
    variant: str = "secondary",
    small: bool = False,
    colors: dict[str, str] | None = None,
    fonts: dict | None = None,
    **extra: Any,
) -> tk.Button:
    """
    variant:
      primary   — 實心強調（啟動、UAT、主要送出）
      danger    — 危險操作（停止、離開）
      secondary — 有邊框的一般按鈕（選擇目錄、選單列、工具）
    """
    colors = {**DEFAULT_COLORS, **(colors or {})}
    fonts = {**DEFAULT_FONTS, **(fonts or {})}
    kw = _base_kwargs(colors, fonts, small=small)

    if variant == "primary":
        kw.update(
            bg=colors["accent"],
            fg="white",
            activebackground=colors["accent_hi"],
            activeforeground="white",
            highlightthickness=0,
            bd=0,
            borderwidth=0,
            relief=tk.FLAT,
        )
    elif variant == "danger":
        kw.update(
            bg=colors["danger_bg"],
            fg=colors["danger"],
            activebackground=colors["danger_bg_hi"],
            activeforeground=colors["danger_hi"],
            highlightthickness=0,
            bd=1,
            borderwidth=1,
            relief=tk.SOLID,
        )
    else:
        kw.update(
            bg=colors["panel"],
            fg=colors["text"],
            activebackground=colors["row_alt"],
            activeforeground=colors["text"],
            highlightthickness=0,
            bd=1,
            borderwidth=1,
            relief=tk.SOLID,
        )

    kw.update(extra)
    btn = tk.Button(parent, text=text, command=command, **kw)
    return btn


def make_menubutton(
    parent: tk.Widget,
    text: str,
    *,
    small: bool = False,
    colors: dict[str, str] | None = None,
    fonts: dict | None = None,
) -> tk.Menubutton:
    """外觀與 secondary 按鈕一致的下拉按鈕。"""
    colors = {**DEFAULT_COLORS, **(colors or {})}
    fonts = {**DEFAULT_FONTS, **(fonts or {})}
    pad = _PAD_SM if small else _PAD
    label = text if text.endswith("▾") or text.endswith("▼") else f"{text} ▾"
    mb = tk.Menubutton(
        parent,
        text=label,
        bg=colors["panel"],
        fg=colors["text"],
        activebackground=colors["row_alt"],
        activeforeground=colors["text"],
        relief=tk.SOLID,
        borderwidth=1,
        bd=1,
        highlightthickness=0,
        font=fonts["btn_sm"] if small else fonts["btn"],
        cursor="hand2",
        direction="below",
        **pad,
    )
    menu = tk.Menu(mb, tearoff=0, font=fonts["btn_sm"] if small else fonts["btn"])
    mb["menu"] = menu
    mb.menu = menu  # type: ignore[attr-defined]
    return mb


def set_button_enabled(btn: tk.Button, enabled: bool, *, variant: str = "secondary", colors: dict[str, str] | None = None) -> None:
    """依 variant 切換啟用／停用外觀。"""
    colors = {**DEFAULT_COLORS, **(colors or {})}
    if not enabled:
        btn.configure(
            state=tk.DISABLED,
            bg=colors["row_alt"],
            fg=colors["muted"],
            disabledforeground=colors["muted"],
            relief=tk.SOLID,
            bd=1,
            borderwidth=1,
            highlightthickness=0,
            cursor="arrow",
        )
        return
    btn.configure(state=tk.NORMAL, cursor="hand2")
    if variant == "primary":
        btn.configure(
            bg=colors["accent"],
            fg="white",
            activebackground=colors["accent_hi"],
            activeforeground="white",
            relief=tk.FLAT,
            bd=0,
            borderwidth=0,
            highlightthickness=0,
        )
    elif variant == "danger":
        btn.configure(
            bg=colors["danger_bg"],
            fg=colors["danger"],
            activebackground=colors["danger_bg_hi"],
            activeforeground=colors["danger_hi"],
            relief=tk.SOLID,
            bd=1,
            borderwidth=1,
            highlightthickness=0,
        )
    else:
        btn.configure(
            bg=colors["panel"],
            fg=colors["text"],
            activebackground=colors["row_alt"],
            activeforeground=colors["text"],
            relief=tk.SOLID,
            bd=1,
            borderwidth=1,
            highlightthickness=0,
        )
