"""確認後以 Cursor prompt deeplink 建立 New Agent。"""

from __future__ import annotations

import tkinter as tk
from tkinter import ttk

from .buttons import make_button

C = {"panel": "#f7f9fb", "muted": "#5a6a7a", "text": "#1c2430"}


def show_new_agent_confirm_dialog(
    parent: tk.Tk | tk.Toplevel,
    *,
    title: str,
    intro: str,
    prompt: str,
) -> bool:
    """預覽將送進 New Agent 的提示。確認回傳 True，取消回傳 False。"""
    dlg = tk.Toplevel(parent)
    dlg.title(title)
    dlg.transient(parent)
    dlg.grab_set()
    dlg.configure(bg=C["panel"])
    dlg.geometry("640x480")
    dlg.minsize(520, 400)

    result: dict[str, bool] = {"ok": False}

    frame = ttk.Frame(dlg, padding=16)
    frame.pack(fill=tk.BOTH, expand=True)
    frame.columnconfigure(0, weight=1)
    frame.rowconfigure(1, weight=1)

    ttk.Label(frame, text=intro, wraplength=580, justify=tk.LEFT).grid(
        row=0, column=0, sticky="w", pady=(0, 10)
    )

    preview = tk.Text(
        frame,
        wrap=tk.WORD,
        font=("Segoe UI", 10),
        relief=tk.SOLID,
        borderwidth=1,
        padx=8,
        pady=8,
    )
    preview.grid(row=1, column=0, sticky="nsew")
    preview.insert("1.0", prompt)
    preview.configure(state=tk.DISABLED)

    def on_ok() -> None:
        result["ok"] = True
        dlg.destroy()

    btns = ttk.Frame(frame)
    btns.grid(row=2, column=0, sticky="e", pady=(14, 0))
    make_button(btns, "取消", dlg.destroy, variant="secondary", small=True).pack(
        side=tk.RIGHT, padx=(6, 0)
    )
    make_button(btns, "確認，開啟 New Agent", on_ok, variant="primary", small=True).pack(
        side=tk.RIGHT
    )

    dlg.protocol("WM_DELETE_WINDOW", dlg.destroy)
    dlg.bind("<Escape>", lambda _e: dlg.destroy())
    parent.wait_window(dlg)
    return bool(result["ok"])
