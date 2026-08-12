"""UAT 問題回報：說明 + 截圖，確認後建立 Cursor New Agent。"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from .buttons import make_button

C = {"panel": "#f7f9fb", "muted": "#5a6a7a", "text": "#1c2430", "accent": "#0b6e56"}
_IMAGE_TYPES = [
    ("圖片", "*.png *.jpg *.jpeg *.gif *.webp *.bmp"),
    ("全部檔案", "*.*"),
]


def _save_clipboard_image(dest: Path) -> str | None:
    """嘗試把剪貼簿圖片存成 dest。成功回傳 None，失敗回傳錯誤說明。"""
    if sys.platform == "win32":
        ps = f"""
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) {{ exit 2 }}
$img.Save('{str(dest).replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png)
exit 0
"""
        try:
            proc = subprocess.run(
                ["powershell", "-NoProfile", "-Command", ps],
                capture_output=True,
                text=True,
                timeout=20,
            )
        except (OSError, subprocess.TimeoutExpired) as exc:
            return f"無法讀取剪貼簿：{exc}"
        if proc.returncode == 2:
            return "剪貼簿沒有圖片（請先截圖：Win+Shift+S 後再貼上）"
        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip()
            return err or "無法從剪貼簿存圖"
        if not dest.is_file() or dest.stat().st_size <= 0:
            return "剪貼簿圖片寫入失敗"
        return None

    try:
        from PIL import ImageGrab  # type: ignore
    except ImportError:
        return "此平台貼上截圖需安裝 Pillow，或改用「加入檔案」"
    try:
        img = ImageGrab.grabclipboard()
    except Exception as exc:  # noqa: BLE001
        return f"無法讀取剪貼簿：{exc}"
    if img is None:
        return "剪貼簿沒有圖片"
    try:
        img.save(dest, format="PNG")
    except Exception as exc:  # noqa: BLE001
        return f"無法儲存圖片：{exc}"
    return None


def show_uat_help_dialog(parent: tk.Tk | tk.Toplevel) -> dict | None:
    """回傳 {title, description, images: list[Path]}，取消則 None。"""
    dlg = tk.Toplevel(parent)
    dlg.title("UAT 求救")
    dlg.transient(parent)
    dlg.grab_set()
    dlg.configure(bg=C["panel"])
    dlg.geometry("520x460")
    dlg.minsize(460, 400)

    result: dict[str, object] = {"value": None}
    images: list[Path] = []
    tmp_dir = Path(tempfile.mkdtemp(prefix="uat-help-"))

    frame = ttk.Frame(dlg, padding=16)
    frame.pack(fill=tk.BOTH, expand=True)
    frame.columnconfigure(0, weight=1)
    frame.rowconfigure(4, weight=1)

    ttk.Label(
        frame,
        text="說明問題並附上截圖。按確認後 Cursor 會跳出視窗，再確認即建立 New Agent（說明直接帶入，不會先寫求助檔）。",
        wraplength=460,
        justify=tk.LEFT,
    ).grid(row=0, column=0, sticky="w", pady=(0, 12))

    ttk.Label(frame, text="標題（可選）").grid(row=1, column=0, sticky="w")
    title_var = tk.StringVar(value="")
    ttk.Entry(frame, textvariable=title_var).grid(row=2, column=0, sticky="we", pady=(2, 8))

    ttk.Label(frame, text="問題說明").grid(row=3, column=0, sticky="nw")
    desc_wrap = ttk.Frame(frame)
    desc_wrap.grid(row=4, column=0, sticky="nsew", pady=(2, 8))
    desc_wrap.columnconfigure(0, weight=1)
    desc_wrap.rowconfigure(0, weight=1)
    desc = tk.Text(desc_wrap, height=8, wrap=tk.WORD, font=("Segoe UI", 11), relief=tk.SOLID, borderwidth=1)
    desc.grid(row=0, column=0, sticky="nsew")

    img_head = ttk.Frame(frame)
    img_head.grid(row=5, column=0, sticky="we")
    ttk.Label(img_head, text="截圖").pack(side=tk.LEFT)
    list_var = tk.StringVar(value="尚未加入（可用 Ctrl+V 貼上）")
    ttk.Label(img_head, textvariable=list_var, foreground=C["muted"]).pack(side=tk.LEFT, padx=(10, 0))

    def _refresh_list() -> None:
        if not images:
            list_var.set("尚未加入（可用 Ctrl+V 貼上）")
        else:
            list_var.set(f"{len(images)} 張：" + "、".join(p.name for p in images))

    def add_files() -> None:
        paths = filedialog.askopenfilenames(
            parent=dlg,
            title="選擇截圖",
            filetypes=_IMAGE_TYPES,
        )
        for raw in paths:
            p = Path(raw)
            if p.is_file() and p not in images:
                images.append(p)
        _refresh_list()

    def paste_image() -> None:
        dest = tmp_dir / f"paste-{len(images) + 1:02d}.png"
        err = _save_clipboard_image(dest)
        if err:
            messagebox.showwarning("貼上截圖", err, parent=dlg)
            return
        images.append(dest)
        _refresh_list()

    def clear_images() -> None:
        images.clear()
        _refresh_list()

    btns_img = ttk.Frame(frame)
    btns_img.grid(row=6, column=0, sticky="w", pady=(6, 0))
    make_button(btns_img, "從剪貼簿貼上", paste_image, variant="secondary", small=True).pack(side=tk.LEFT)
    make_button(btns_img, "加入檔案…", add_files, variant="secondary", small=True).pack(
        side=tk.LEFT, padx=(6, 0)
    )
    make_button(btns_img, "清空", clear_images, variant="secondary", small=True).pack(
        side=tk.LEFT, padx=(6, 0)
    )

    def on_ok() -> None:
        description = desc.get("1.0", tk.END).strip()
        title = title_var.get().strip()
        if not description and not images:
            messagebox.showwarning("內容不足", "請至少填寫說明或加入一張截圖。", parent=dlg)
            return
        result["value"] = {
            "title": title or "UAT 問題",
            "description": description,
            "images": list(images),
        }
        dlg.destroy()

    btns = ttk.Frame(frame)
    btns.grid(row=7, column=0, sticky="e", pady=(16, 0))
    make_button(btns, "取消", dlg.destroy, variant="secondary", small=True).pack(
        side=tk.RIGHT, padx=(6, 0)
    )
    make_button(btns, "確認，開啟 New Agent", on_ok, variant="primary", small=True).pack(side=tk.RIGHT)

    dlg.bind("<Control-v>", lambda _e: paste_image())
    dlg.bind("<Control-V>", lambda _e: paste_image())
    dlg.protocol("WM_DELETE_WINDOW", dlg.destroy)
    desc.focus_set()
    parent.wait_window(dlg)
    value = result["value"]
    return value if isinstance(value, dict) else None
