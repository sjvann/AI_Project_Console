"""部署目標設定對話框：先選發佈類型，再填對應屬性。"""

from __future__ import annotations

import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from ..deploy_config import (
    DEPLOY_TARGETS,
    ONPREM_PROTOCOLS,
    PROTOCOL_DEFAULT_PORT,
    TARGET_AZURE,
    TARGET_GCP,
    TARGET_NONE,
    TARGET_ONPREM,
    AzureConfig,
    DeployConfig,
    OnpremConfig,
    protocol_key_from_label,
    protocol_label,
    target_key_from_label,
    target_label,
)
from ..gcp_config import GcpConfig

from .buttons import make_button

C = {"panel": "#f7f9fb", "muted": "#5a6a7a"}

_HINTS = {
    TARGET_NONE: "此專案只在本機執行，不發佈到雲端或遠端伺服器。",
    TARGET_GCP: "部署到 GCP Compute Engine。需填 Project ID、Zone、Instance。",
    TARGET_ONPREM: "發佈到公司或自家機房的另一台伺服器（SSH、WinRM、IIS 或網路磁碟）。",
    TARGET_AZURE: "部署到 Azure App Service／同類資源。需填訂閱、資源群組與應用程式名稱。",
}


def _add_entry(parent: ttk.Frame, row: int, label: str, var: tk.StringVar, width: int = 42) -> None:
    ttk.Label(parent, text=label).grid(row=row, column=0, sticky="w", pady=4)
    ttk.Entry(parent, textvariable=var, width=width).grid(
        row=row, column=1, sticky="we", pady=4, padx=(8, 0)
    )


def show_deploy_settings(
    parent: tk.Tk | tk.Toplevel,
    initial: DeployConfig,
    *,
    allow_write_manifest: bool = True,
) -> tuple[DeployConfig, str] | None:
    dlg = tk.Toplevel(parent)
    dlg.title("部署設定")
    dlg.transient(parent)
    dlg.grab_set()
    dlg.configure(bg=C["panel"])
    dlg.resizable(False, False)
    result: dict[str, object] = {"value": None}

    frame = ttk.Frame(dlg, padding=16)
    frame.pack(fill=tk.BOTH, expand=True)
    frame.columnconfigure(1, weight=1)

    ttk.Label(frame, text="請先選擇發佈目標，再填寫該目標所需的屬性。").grid(
        row=0, column=0, columnspan=2, sticky="w", pady=(0, 8)
    )

    ttk.Label(frame, text="發佈目標").grid(row=1, column=0, sticky="w", pady=4)
    target_var = tk.StringVar(value=target_label(initial.normalized_target()))
    target_combo = ttk.Combobox(
        frame,
        textvariable=target_var,
        values=[label for _, label in DEPLOY_TARGETS],
        state="readonly",
        width=40,
    )
    target_combo.grid(row=1, column=1, sticky="we", pady=4, padx=(8, 0))

    hint_var = tk.StringVar(value=_HINTS.get(initial.normalized_target(), ""))
    ttk.Label(frame, textvariable=hint_var, foreground=C["muted"], wraplength=420).grid(
        row=2, column=0, columnspan=2, sticky="w", pady=(0, 8)
    )

    body = ttk.Frame(frame)
    body.grid(row=3, column=0, columnspan=2, sticky="we")
    body.columnconfigure(0, weight=1)

    none_panel = ttk.Frame(body)
    ttk.Label(
        none_panel,
        text="儲存後，控制台不會要求雲端或遠端主機設定。之後若要發佈，再改選其他目標即可。",
        wraplength=420,
        foreground=C["muted"],
    ).pack(anchor=tk.W)

    gcp_panel = ttk.Frame(body)
    gcp_panel.columnconfigure(1, weight=1)
    gcp_vars = {
        "projectId": tk.StringVar(value=initial.gcp.project_id),
        "zone": tk.StringVar(value=initial.gcp.zone or "asia-east1-b"),
        "instance": tk.StringVar(value=initial.gcp.instance),
        "host": tk.StringVar(value=initial.gcp.host),
        "workflow": tk.StringVar(value=initial.gcp.workflow or "deploy-gcp.yml"),
    }
    _add_entry(gcp_panel, 0, "GCP Project ID", gcp_vars["projectId"])
    _add_entry(gcp_panel, 1, "Zone", gcp_vars["zone"])
    _add_entry(gcp_panel, 2, "Instance", gcp_vars["instance"])
    _add_entry(gcp_panel, 3, "對外網址（可選）", gcp_vars["host"])
    _add_entry(gcp_panel, 4, "Workflow 檔名", gcp_vars["workflow"])

    onprem_panel = ttk.Frame(body)
    onprem_panel.columnconfigure(1, weight=1)
    proto_var = tk.StringVar(value=protocol_label(initial.onprem.protocol or "ssh"))
    onprem_vars = {
        "host": tk.StringVar(value=initial.onprem.host),
        "port": tk.StringVar(value=initial.onprem.port or "22"),
        "user": tk.StringVar(value=initial.onprem.user),
        "remotePath": tk.StringVar(value=initial.onprem.remote_path),
        "openUrl": tk.StringVar(value=initial.onprem.open_url),
        "keyPath": tk.StringVar(value=initial.onprem.key_path),
    }
    ttk.Label(onprem_panel, text="連線協定").grid(row=0, column=0, sticky="w", pady=4)
    proto_combo = ttk.Combobox(
        onprem_panel,
        textvariable=proto_var,
        values=[label for _, label in ONPREM_PROTOCOLS],
        state="readonly",
        width=40,
    )
    proto_combo.grid(row=0, column=1, sticky="we", pady=4, padx=(8, 0))
    _add_entry(onprem_panel, 1, "主機（IP 或名稱）", onprem_vars["host"])
    _add_entry(onprem_panel, 2, "連接埠", onprem_vars["port"])
    _add_entry(onprem_panel, 3, "帳號", onprem_vars["user"])
    _add_entry(onprem_panel, 4, "遠端路徑／網站目錄", onprem_vars["remotePath"])
    _add_entry(onprem_panel, 5, "對外網址（可選）", onprem_vars["openUrl"])

    ttk.Label(onprem_panel, text="SSH 金鑰（可選）").grid(row=6, column=0, sticky="w", pady=4)
    key_row = ttk.Frame(onprem_panel)
    key_row.grid(row=6, column=1, sticky="we", pady=4, padx=(8, 0))
    key_row.columnconfigure(0, weight=1)
    ttk.Entry(key_row, textvariable=onprem_vars["keyPath"]).grid(row=0, column=0, sticky="we")

    def browse_key() -> None:
        path = filedialog.askopenfilename(
            parent=dlg,
            title="選擇 SSH 私鑰",
            filetypes=[("金鑰檔", "*"), ("全部檔案", "*.*")],
        )
        if path:
            onprem_vars["keyPath"].set(path)

    make_button(key_row, "瀏覽…", browse_key, variant="secondary", small=True).grid(
        row=0, column=1, padx=(6, 0)
    )
    ttk.Label(
        onprem_panel,
        text="密碼不會寫入設定檔；請用 SSH 金鑰或本機已登入的認證。",
        foreground=C["muted"],
        wraplength=420,
    ).grid(row=7, column=0, columnspan=2, sticky="w", pady=(4, 0))

    azure_panel = ttk.Frame(body)
    azure_panel.columnconfigure(1, weight=1)
    azure_vars = {
        "subscriptionId": tk.StringVar(value=initial.azure.subscription_id),
        "resourceGroup": tk.StringVar(value=initial.azure.resource_group),
        "appName": tk.StringVar(value=initial.azure.app_name),
        "region": tk.StringVar(value=initial.azure.region or "eastasia"),
        "openUrl": tk.StringVar(value=initial.azure.open_url),
        "workflow": tk.StringVar(value=initial.azure.workflow or "deploy-azure.yml"),
    }
    _add_entry(azure_panel, 0, "Subscription ID", azure_vars["subscriptionId"])
    _add_entry(azure_panel, 1, "Resource Group", azure_vars["resourceGroup"])
    _add_entry(azure_panel, 2, "App / 資源名稱", azure_vars["appName"])
    _add_entry(azure_panel, 3, "Region", azure_vars["region"])
    _add_entry(azure_panel, 4, "對外網址（可選）", azure_vars["openUrl"])
    _add_entry(azure_panel, 5, "Workflow 檔名", azure_vars["workflow"])

    panels = {
        TARGET_NONE: none_panel,
        TARGET_GCP: gcp_panel,
        TARGET_ONPREM: onprem_panel,
        TARGET_AZURE: azure_panel,
    }

    def current_target() -> str:
        return target_key_from_label(target_var.get())

    def show_panel() -> None:
        key = current_target()
        hint_var.set(_HINTS.get(key, ""))
        for panel in panels.values():
            panel.grid_forget()
        panels[key].grid(row=0, column=0, sticky="we")
        dlg.update_idletasks()
        dlg.geometry("")

    def on_protocol_change(_event: object | None = None) -> None:
        proto = protocol_key_from_label(proto_var.get())
        current_port = onprem_vars["port"].get().strip()
        known = set(PROTOCOL_DEFAULT_PORT.values())
        if not current_port or current_port in known:
            onprem_vars["port"].set(PROTOCOL_DEFAULT_PORT.get(proto, "22"))

    target_combo.bind("<<ComboboxSelected>>", lambda _e: show_panel())
    proto_combo.bind("<<ComboboxSelected>>", on_protocol_change)
    show_panel()

    save_target = tk.StringVar(value="local")
    opt = ttk.Frame(frame)
    opt.grid(row=4, column=0, columnspan=2, sticky="w", pady=(12, 0))
    ttk.Radiobutton(opt, text="存到本機 settings", variable=save_target, value="local").pack(anchor=tk.W)
    if allow_write_manifest:
        ttk.Radiobutton(
            opt, text="寫入專案 ai-project.json", variable=save_target, value="manifest"
        ).pack(anchor=tk.W)

    def on_ok() -> None:
        target = current_target()
        proto = protocol_key_from_label(proto_var.get())
        cfg = DeployConfig(
            target=target,
            gcp=GcpConfig(
                project_id=gcp_vars["projectId"].get().strip(),
                zone=gcp_vars["zone"].get().strip(),
                instance=gcp_vars["instance"].get().strip(),
                host=gcp_vars["host"].get().strip(),
                workflow=gcp_vars["workflow"].get().strip() or "deploy-gcp.yml",
            ),
            onprem=OnpremConfig(
                protocol=proto,
                host=onprem_vars["host"].get().strip(),
                port=onprem_vars["port"].get().strip() or PROTOCOL_DEFAULT_PORT.get(proto, "22"),
                user=onprem_vars["user"].get().strip(),
                remote_path=onprem_vars["remotePath"].get().strip(),
                open_url=onprem_vars["openUrl"].get().strip(),
                key_path=onprem_vars["keyPath"].get().strip(),
            ),
            azure=AzureConfig(
                subscription_id=azure_vars["subscriptionId"].get().strip(),
                resource_group=azure_vars["resourceGroup"].get().strip(),
                app_name=azure_vars["appName"].get().strip(),
                region=azure_vars["region"].get().strip(),
                open_url=azure_vars["openUrl"].get().strip(),
                workflow=azure_vars["workflow"].get().strip() or "deploy-azure.yml",
            ),
        )
        if not cfg.is_complete():
            if target == TARGET_GCP:
                msg = "請填寫 GCP Project ID、Zone、Instance。"
            elif target == TARGET_ONPREM:
                msg = "請填寫主機與帳號（SMB 則填主機或遠端路徑）。"
            elif target == TARGET_AZURE:
                msg = "請填寫 Subscription ID、Resource Group、App 名稱。"
            else:
                msg = "設定未齊全。"
            messagebox.showwarning("設定未齊全", msg, parent=dlg)
            return
        result["value"] = (cfg, save_target.get())
        dlg.destroy()

    btns = ttk.Frame(frame)
    btns.grid(row=5, column=0, columnspan=2, sticky="e", pady=(16, 0))
    make_button(btns, "取消", dlg.destroy, variant="secondary", small=True).pack(
        side=tk.RIGHT, padx=(6, 0)
    )
    make_button(btns, "儲存", on_ok, variant="primary", small=True).pack(side=tk.RIGHT)
    dlg.protocol("WM_DELETE_WINDOW", dlg.destroy)
    parent.wait_window(dlg)
    value = result["value"]
    return value if isinstance(value, tuple) else None  # type: ignore[return-value]
