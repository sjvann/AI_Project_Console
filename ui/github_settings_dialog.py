"""GitHub 倉庫設定對話框。"""

from __future__ import annotations

import tkinter as tk
from tkinter import messagebox, ttk

from ..github_config import GithubConfig

from .buttons import make_button

C = {"panel": "#f7f9fb"}


def show_github_settings(
    parent: tk.Tk | tk.Toplevel,
    initial: GithubConfig,
    *,
    allow_write_manifest: bool = True,
) -> tuple[GithubConfig, str] | None:
    """回傳 (設定, 目標)；目標為 local / manifest / local+apply / manifest+apply。"""
    dlg = tk.Toplevel(parent)
    dlg.title("GitHub 設定")
    dlg.transient(parent)
    dlg.grab_set()
    dlg.configure(bg=C["panel"])
    dlg.resizable(False, False)
    result: dict[str, object] = {"value": None}

    frame = ttk.Frame(dlg, padding=16)
    frame.pack(fill=tk.BOTH, expand=True)
    ttk.Label(
        frame,
        text="設定遠端倉庫後可同步（pull）與發布（push）。可從 git remote 預填。",
    ).grid(row=0, column=0, columnspan=2, sticky="w", pady=(0, 10))

    fields = [
        ("owner", "Owner（使用者／組織）", initial.owner),
        ("repo", "Repository", initial.repo),
        ("remote", "Remote 名稱", initial.remote or "origin"),
        ("defaultBranch", "預設分支（PR base）", initial.default_branch or "main"),
        ("url", "Remote URL（可選，覆寫）", initial.url),
    ]
    vars_map: dict[str, tk.StringVar] = {}
    for i, (key, label, value) in enumerate(fields, start=1):
        ttk.Label(frame, text=label).grid(row=i, column=0, sticky="w", pady=4)
        var = tk.StringVar(value=value)
        vars_map[key] = var
        ttk.Entry(frame, textvariable=var, width=46).grid(
            row=i, column=1, sticky="we", pady=4, padx=(8, 0)
        )

    save_target = tk.StringVar(value="local")
    apply_remote_var = tk.BooleanVar(value=True)
    opt = ttk.Frame(frame)
    opt.grid(row=len(fields) + 1, column=0, columnspan=2, sticky="w", pady=(12, 0))
    ttk.Radiobutton(opt, text="存到本機 settings", variable=save_target, value="local").pack(
        anchor=tk.W
    )
    if allow_write_manifest:
        ttk.Radiobutton(
            opt, text="寫入專案 ai-project.json", variable=save_target, value="manifest"
        ).pack(anchor=tk.W)
    ttk.Checkbutton(
        opt, text="儲存後立即套用到 git remote", variable=apply_remote_var
    ).pack(anchor=tk.W, pady=(6, 0))

    def on_ok() -> None:
        cfg = GithubConfig(
            owner=vars_map["owner"].get().strip(),
            repo=vars_map["repo"].get().strip(),
            remote=vars_map["remote"].get().strip() or "origin",
            default_branch=vars_map["defaultBranch"].get().strip() or "main",
            url=vars_map["url"].get().strip(),
        )
        if not cfg.is_complete():
            messagebox.showwarning(
                "設定未齊全",
                "請填寫 owner + repo，或提供 Remote URL。",
                parent=dlg,
            )
            return
        target = save_target.get()
        if apply_remote_var.get():
            target = f"{target}+apply"
        result["value"] = (cfg, target)
        dlg.destroy()

    btns = ttk.Frame(frame)
    btns.grid(row=len(fields) + 2, column=0, columnspan=2, sticky="e", pady=(16, 0))
    make_button(btns, "取消", dlg.destroy, variant="secondary", small=True).pack(
        side=tk.RIGHT, padx=(6, 0)
    )
    make_button(btns, "儲存", on_ok, variant="primary", small=True).pack(side=tk.RIGHT)
    dlg.protocol("WM_DELETE_WINDOW", dlg.destroy)
    parent.wait_window(dlg)
    value = result["value"]
    return value if isinstance(value, tuple) else None  # type: ignore[return-value]
