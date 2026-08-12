"""GCP 部署設定對話框。"""

from __future__ import annotations

import tkinter as tk
from tkinter import messagebox, ttk

from ..gcp_config import GcpConfig

from .buttons import make_button

C = {"panel": "#f7f9fb"}


def show_gcp_settings(
    parent: tk.Tk | tk.Toplevel,
    initial: GcpConfig,
    *,
    allow_write_manifest: bool = True,
) -> tuple[GcpConfig, str] | None:
    dlg = tk.Toplevel(parent)
    dlg.title("GCP 部署設定")
    dlg.transient(parent)
    dlg.grab_set()
    dlg.configure(bg=C["panel"])
    dlg.resizable(False, False)
    result: dict[str, object] = {"value": None}

    frame = ttk.Frame(dlg, padding=16)
    frame.pack(fill=tk.BOTH, expand=True)
    ttk.Label(frame, text="部署前需設定目標專案與執行個體。").grid(
        row=0, column=0, columnspan=2, sticky="w", pady=(0, 10)
    )

    fields = [
        ("projectId", "GCP Project ID", initial.project_id),
        ("zone", "Zone", initial.zone),
        ("instance", "Instance", initial.instance),
        ("host", "Host（可選）", initial.host),
        ("workflow", "Workflow 檔名", initial.workflow or "deploy-gcp.yml"),
    ]
    vars_map: dict[str, tk.StringVar] = {}
    for i, (key, label, value) in enumerate(fields, start=1):
        ttk.Label(frame, text=label).grid(row=i, column=0, sticky="w", pady=4)
        var = tk.StringVar(value=value)
        vars_map[key] = var
        ttk.Entry(frame, textvariable=var, width=42).grid(row=i, column=1, sticky="we", pady=4, padx=(8, 0))

    save_target = tk.StringVar(value="local")
    opt = ttk.Frame(frame)
    opt.grid(row=len(fields) + 1, column=0, columnspan=2, sticky="w", pady=(12, 0))
    ttk.Radiobutton(opt, text="存到本機 settings", variable=save_target, value="local").pack(anchor=tk.W)
    if allow_write_manifest:
        ttk.Radiobutton(
            opt, text="寫入專案 ai-project.json", variable=save_target, value="manifest"
        ).pack(anchor=tk.W)

    def on_ok() -> None:
        cfg = GcpConfig(
            project_id=vars_map["projectId"].get().strip(),
            zone=vars_map["zone"].get().strip(),
            instance=vars_map["instance"].get().strip(),
            host=vars_map["host"].get().strip(),
            workflow=vars_map["workflow"].get().strip() or "deploy-gcp.yml",
        )
        if not cfg.is_complete():
            messagebox.showwarning("設定未齊全", "請填寫 projectId、zone、instance。", parent=dlg)
            return
        result["value"] = (cfg, save_target.get())
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
