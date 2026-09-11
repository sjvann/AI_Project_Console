"""AI_Project 控制台（Tk 桌面版）。"""

from __future__ import annotations

import json
import subprocess
import sys
import threading
import time
import tkinter as tk
import webbrowser
from pathlib import Path
from tkinter import messagebox, ttk

from . import __version__
from .build_state import all_project_build_states, all_service_build_states
from .deploy_config import (
    TARGET_NONE,
    ci_hint,
    open_host,
    resolve_deploy,
    save_deploy_local,
    status_report as deploy_status_report,
    target_label,
    write_deploy_to_manifest,
)
from .github_config import (
    apply_remote,
    resolve_github,
    save_github_local,
    write_github_to_manifest,
)
from . import github_ops
from .cursor_ops import (
    build_agent_prompt,
    build_uat_agent_prompt,
    close_cursor,
    new_agent_launch_delay_ms,
    open_in_cursor,
    open_project_for_new_agent,
    open_prompt_deeplink,
    stage_uat_screenshots,
)
from .process_ops import (
    command_exists,
    doctor_report,
    probe_health,
    restart_service,
    start_all,
    stop_all,
    stop_service,
)
from .runtime import (
    ProjectRuntime,
    get_open_with_cursor,
    remember_project,
    set_open_with_cursor,
)
from .service_catalog import (
    ProjectCatalog,
    ServiceEntry,
    build_catalog,
    host_service,
    service_by_id,
)
from .ui.buttons import make_button, make_menubutton, set_button_enabled
from .ui.deploy_settings_dialog import show_deploy_settings
from .ui.github_settings_dialog import show_github_settings
from .ui.new_agent_dialog import show_new_agent_confirm_dialog
from .ui.project_bar import ProjectBar
from .ui.uat_help_dialog import show_uat_help_dialog

PACKAGE_DIR = Path(__file__).resolve().parent
ACTIONS_PATH = PACKAGE_DIR / "actions.json"
LOG_TAIL_LINES = 500
HEALTH_INTERVAL_MS = 2000
LOG_INTERVAL_MS = 800
BUILD_BADGE = {"stale": "需重編", "unbuilt": "未建置"}

C = {
    "bg": "#e8ecf0",
    "panel": "#f7f9fb",
    "border": "#c5ced8",
    "text": "#1c2430",
    "muted": "#5a6a7a",
    "accent": "#0b6e56",
    "accent_hi": "#0a5c48",
    "danger": "#9b2c2c",
    "danger_hi": "#7f2424",
    "danger_bg": "#fff5f5",
    "danger_bg_hi": "#fde8e8",
    "ok": "#1a7f4b",
    "wait": "#b86e00",
    "down": "#8a95a3",
    "log_bg": "#141a22",
    "log_fg": "#d5dde8",
    "sel": "#d4e8e1",
    "row_alt": "#eef2f6",
}
_UI = "Segoe UI"
_MONO = "Cascadia Mono" if sys.platform == "win32" else "Consolas"
F = {
    "title": (_UI, 20, "bold"),
    "hint": (_UI, 12),
    "ui": (_UI, 12),
    "ui_sm": (_UI, 11),
    "ui_bold": (_UI, 13, "bold"),
    "section": (_UI, 13, "bold"),
    "row": (_UI, 12),
    "row_sm": (_UI, 11),
    "btn": (_UI, 12, "bold"),
    "btn_sm": (_UI, 11, "bold"),
    "btn_sec": (_UI, 12, "bold"),
    "log": (_MONO, 12),
}


def load_actions(group: str) -> list[dict]:
    try:
        with open(ACTIONS_PATH, encoding="utf-8") as fh:
            doc = json.load(fh)
    except (OSError, json.JSONDecodeError):
        return []
    return [a for a in doc.get("actions") or [] if a.get("group") == group]


class SegmentedTabs(tk.Frame):
    def __init__(self, parent: tk.Widget, body: tk.Widget) -> None:
        super().__init__(parent, bg=C["border"], highlightbackground=C["border"], highlightthickness=1)
        self._body = body
        self._tabs: dict[str, dict] = {}
        self._order: list[str] = []
        self.current: str | None = None

    def add(self, key: str, text: str, page: tk.Widget) -> None:
        col = len(self._order)
        cell = tk.Frame(self, bg=C["row_alt"], cursor="hand2")
        cell.grid(row=0, column=col, sticky="nsew", padx=(1 if col else 0, 0))
        self.grid_columnconfigure(col, weight=1, uniform="seg")
        inner = tk.Frame(cell, bg=C["row_alt"])
        inner.pack(pady=(9, 6))
        label = tk.Label(inner, text=text, bg=C["row_alt"], fg=C["muted"], font=F["ui"])
        label.pack(side=tk.LEFT)
        badge = tk.Label(inner, text="", bg=C["row_alt"], fg=C["muted"], font=F["row_sm"])
        rule = tk.Frame(cell, height=3, bg=C["row_alt"])
        rule.pack(fill=tk.X, side=tk.BOTTOM)
        self._tabs[key] = {
            "cell": cell, "inner": inner, "label": label, "badge": badge, "rule": rule, "page": page,
        }
        self._order.append(key)
        for w in (cell, inner, label, badge):
            w.bind("<Button-1>", lambda _e, k=key: self.select(k))
        page.pack_forget()
        if self.current is None:
            self.select(key)

    def set_badge(self, key: str, text: str, *, alert: bool = False) -> None:
        tab = self._tabs.get(key)
        if not tab:
            return
        tab["badge"].configure(text=("  " + text) if text else "", fg=C["wait"] if alert else C["muted"])
        if text:
            tab["badge"].pack(side=tk.LEFT)

    def select(self, key: str) -> None:
        if key not in self._tabs:
            return
        self.current = key
        for k, tab in self._tabs.items():
            active = k == key
            bg = C["panel"] if active else C["row_alt"]
            fg = C["text"] if active else C["muted"]
            font = F["ui_bold"] if active else F["ui"]
            for w in (tab["cell"], tab["inner"], tab["label"], tab["badge"]):
                w.configure(bg=bg)
            tab["label"].configure(fg=fg, font=font)
            tab["rule"].configure(bg=C["accent"] if active else bg)
            if active:
                tab["page"].pack(fill=tk.BOTH, expand=True)
            else:
                tab["page"].pack_forget()


class ConsoleApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("AI_Project 控制台")
        self.geometry("1280x820")
        self.minsize(960, 640)
        self.configure(bg=C["bg"])
        self._apply_window_icon()

        self.catalog: ProjectCatalog | None = None
        self.runtime: ProjectRuntime | None = None
        self._health: dict[str, bool] = {}
        self._selected_service_id: str | None = None
        self._job_busy = False
        self._follow_log = tk.BooleanVar(value=True)
        self._log_offset = 0
        self._service_rows: dict[str, dict] = {}
        self._project_cache: list[dict] = []
        self._last_build_failure: dict | None = None
        self._compile_help_btn: tk.Button | None = None

        self._setup_style()
        self._build_ui()
        self.after(HEALTH_INTERVAL_MS, self._poll_health)
        self.after(LOG_INTERVAL_MS, self._poll_log)

    def _apply_window_icon(self) -> None:
        root = Path(__file__).resolve().parent
        candidates = [
            root / "src" / "AiProject.Console.App" / "Assets" / "app.ico",
            root / "assets" / "brand" / "app.ico",
        ]
        for ico in candidates:
            if not ico.is_file():
                continue
            try:
                self.iconbitmap(default=str(ico))
                return
            except tk.TclError:
                continue

    def _setup_style(self) -> None:
        style = ttk.Style(self)
        try:
            style.theme_use("clam")
        except tk.TclError:
            pass
        style.configure("TFrame", background=C["bg"])
        style.configure("TLabel", background=C["bg"], foreground=C["text"], font=F["ui"])
        style.configure("Title.TLabel", background=C["bg"], foreground=C["text"], font=F["title"])
        style.configure("Hint.TLabel", background=C["bg"], foreground=C["muted"], font=F["hint"])

    def _make_link(self, parent: tk.Widget, text: str, command, *, bg: str = C["panel"]) -> tk.Label:
        font = (F["row_sm"][0], F["row_sm"][1], "underline")
        lbl = tk.Label(parent, text=text, bg=bg, fg=C["accent"], font=font, cursor="hand2")
        lbl.pack(side=tk.LEFT, padx=(0, 10))
        lbl.bind("<Button-1>", lambda _e: command())
        lbl.bind("<Enter>", lambda _e, w=lbl: w.configure(fg=C["accent_hi"]))
        lbl.bind("<Leave>", lambda _e, w=lbl: w.configure(fg=C["accent"]))
        return lbl

    def _build_ui(self) -> None:
        outer = ttk.Frame(self, padding=(16, 14))
        outer.pack(fill=tk.BOTH, expand=True)

        head = ttk.Frame(outer)
        head.pack(fill=tk.X)
        ttk.Label(head, text="AI_Project 控制台", style="Title.TLabel").pack(side=tk.LEFT)
        ttk.Label(
            head, text=f"通用本機堆疊 · GitHub／部署 · v{__version__}", style="Hint.TLabel",
        ).pack(side=tk.LEFT, padx=(14, 0), pady=(8, 0))
        make_button(
            head, "離開", self._on_exit, variant="danger", colors=C, fonts=F,
        ).pack(side=tk.RIGHT)

        self._build_toolbar(outer)
        self._build_summary(outer)

        paned = tk.PanedWindow(
            outer, orient=tk.HORIZONTAL, sashwidth=8, sashrelief=tk.FLAT, bg=C["border"], bd=0,
        )
        paned.pack(fill=tk.BOTH, expand=True, pady=(12, 0))
        left = tk.Frame(paned, bg=C["panel"])
        right = tk.Frame(paned, bg=C["panel"])
        paned.add(left, minsize=280, stretch="always")
        paned.add(right, minsize=280, stretch="always")
        self._paned = paned
        self._build_left(left)
        self._build_right(right)
        self.after_idle(self._balance_panes)

        foot = ttk.Frame(outer)
        foot.pack(fill=tk.X, pady=(10, 0))
        ttk.Label(
            foot,
            text="關閉控制台不會停止服務 · 狀態寫入選定專案的 .ai_project/",
            style="Hint.TLabel",
        ).pack(side=tk.LEFT)

    def _build_toolbar(self, parent: ttk.Frame) -> None:
        # 專案區
        project_card = tk.Frame(
            parent, bg=C["panel"], highlightbackground=C["border"], highlightthickness=1,
        )
        project_card.pack(fill=tk.X, pady=(12, 0))
        project_inner = tk.Frame(project_card, bg=C["panel"], padx=14, pady=10)
        project_inner.pack(fill=tk.X)
        self.project_bar = ProjectBar(
            project_inner,
            on_pick=self._on_pick_project,
            open_with_cursor=get_open_with_cursor(),
            on_open_with_cursor_change=set_open_with_cursor,
            colors=C,
            fonts=F,
        )
        self.project_bar.pack(fill=tk.X)

        # 操作區：分區排列
        action_card = tk.Frame(
            parent, bg=C["panel"], highlightbackground=C["border"], highlightthickness=1,
        )
        action_card.pack(fill=tk.X, pady=(8, 0))
        action_inner = tk.Frame(action_card, bg=C["panel"], padx=14, pady=10)
        action_inner.pack(fill=tk.X)

        def _sep() -> None:
            tk.Frame(action_inner, bg=C["border"], width=1).pack(
                side=tk.LEFT, fill=tk.Y, padx=12, pady=2,
            )

        # 區 1：啟動／停止
        g1 = tk.Frame(action_inner, bg=C["panel"])
        g1.pack(side=tk.LEFT)
        self.btn_start = make_button(
            g1, "▶  啟動", self._on_start, variant="primary", colors=C, fonts=F,
        )
        self.btn_start.pack(side=tk.LEFT, padx=(0, 8))
        self.btn_stop = make_button(
            g1, "■  停止全部", self._on_stop, variant="danger", colors=C, fonts=F,
        )
        self.btn_stop.pack(side=tk.LEFT)

        _sep()

        # 區 2：開啟前端／UAT
        g2 = tk.Frame(action_inner, bg=C["panel"])
        g2.pack(side=tk.LEFT)
        self.btn_urls = make_menubutton(g2, "開啟前端", colors=C, fonts=F)
        self.btn_urls.menu.add_command(label="開啟前端", command=lambda: self._on_open_urls(True))  # type: ignore[attr-defined]
        self.btn_urls.menu.add_command(label="開啟全部 URL", command=lambda: self._on_open_urls(False))  # type: ignore[attr-defined]
        self.btn_urls.pack(side=tk.LEFT, padx=(0, 8))
        self.btn_uat = make_button(
            g2, "UAT 求救", self._on_ask_cursor_uat_help, variant="primary", colors=C, fonts=F,
        )
        self.btn_uat.pack(side=tk.LEFT)

        _sep()

        # 區 3：環境體檢
        g3 = tk.Frame(action_inner, bg=C["panel"])
        g3.pack(side=tk.LEFT)
        self.btn_doctor = make_button(
            g3, "環境體檢", self._on_doctor, variant="secondary", colors=C, fonts=F,
        )
        self.btn_doctor.pack(side=tk.LEFT)

        _sep()

        # 區 4：建置／GitHub／部署
        g4 = tk.Frame(action_inner, bg=C["panel"])
        g4.pack(side=tk.LEFT)
        self._build_action_menu(g4, "build", "建置")
        self._build_action_menu(g4, "github", "GitHub")
        self._build_action_menu(g4, "deploy", "部署")

    def _build_action_menu(self, parent: tk.Frame, group: str, title: str) -> None:
        actions = load_actions(group)
        if not actions:
            return
        btn = make_menubutton(parent, title, colors=C, fonts=F)
        for action in actions:
            btn.menu.add_command(  # type: ignore[attr-defined]
                label=str(action.get("label") or action["id"]),
                command=lambda a=action: self._on_action(a),
            )
        btn.pack(side=tk.LEFT, padx=(0, 8))

    def _build_summary(self, parent: ttk.Frame) -> None:
        wrap = tk.Frame(parent, bg=C["bg"])
        wrap.pack(fill=tk.X, pady=(10, 0))
        self.ready_var = tk.StringVar(value="就緒 0 / 0")
        self.job_var = tk.StringVar(value="待命")
        tk.Label(wrap, textvariable=self.ready_var, bg=C["bg"], fg=C["text"], font=F["ui_bold"]).pack(
            side=tk.LEFT
        )
        tk.Label(wrap, textvariable=self.job_var, bg=C["bg"], fg=C["muted"], font=F["ui"]).pack(
            side=tk.LEFT, padx=(12, 0)
        )

    def _build_left(self, parent: tk.Frame) -> None:
        body = tk.Frame(parent, bg=C["panel"])
        tabs = SegmentedTabs(parent, body)
        tabs.pack(fill=tk.X)
        body.pack(fill=tk.BOTH, expand=True, pady=(8, 0))
        svc_tab = tk.Frame(body, bg=C["panel"])
        prj_tab = tk.Frame(body, bg=C["panel"])
        self._build_service_panel(svc_tab)
        self._build_project_panel(prj_tab)
        tabs.add("svc", "服務", svc_tab)
        tabs.add("prj", "專案", prj_tab)
        self._left_tabs = tabs

    def _build_service_panel(self, parent: tk.Frame) -> None:
        self.warn_var = tk.StringVar(value="")
        tk.Label(
            parent, textvariable=self.warn_var, bg=C["panel"], fg=C["wait"], font=F["ui_sm"], anchor="w",
        ).pack(fill=tk.X, padx=10, pady=(8, 0))
        header = tk.Frame(parent, bg=C["row_alt"])
        header.pack(fill=tk.X, padx=8, pady=(8, 0))
        for text, w in (("狀態", 8), ("服務", 22), ("埠", 8), ("操作", 22)):
            tk.Label(
                header, text=text, bg=C["row_alt"], fg=C["muted"], font=F["ui_sm"], width=w, anchor="w",
            ).pack(side=tk.LEFT, padx=4, pady=6)
        canvas = tk.Canvas(parent, bg=C["panel"], highlightthickness=0)
        scroll = ttk.Scrollbar(parent, orient=tk.VERTICAL, command=canvas.yview)
        self.service_list = tk.Frame(canvas, bg=C["panel"])
        self.service_list.bind("<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=self.service_list, anchor="nw")
        canvas.configure(yscrollcommand=scroll.set)
        canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(8, 0), pady=8)
        scroll.pack(side=tk.RIGHT, fill=tk.Y, pady=8, padx=(0, 8))

    def _build_project_panel(self, parent: tk.Frame) -> None:
        tools = tk.Frame(parent, bg=C["panel"])
        tools.pack(fill=tk.X, padx=10, pady=(8, 0))
        self.stale_only = tk.BooleanVar(value=False)
        ttk.Checkbutton(
            tools, text="只看需重編", variable=self.stale_only, command=self._render_projects
        ).pack(side=tk.LEFT)
        make_button(
            tools, "刷新", self._refresh_build_states, variant="secondary", small=True, colors=C, fonts=F,
        ).pack(side=tk.LEFT, padx=8)
        canvas = tk.Canvas(parent, bg=C["panel"], highlightthickness=0)
        scroll = ttk.Scrollbar(parent, orient=tk.VERTICAL, command=canvas.yview)
        self.project_list = tk.Frame(canvas, bg=C["panel"])
        self.project_list.bind("<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=self.project_list, anchor="nw")
        canvas.configure(yscrollcommand=scroll.set)
        canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(8, 0), pady=8)
        scroll.pack(side=tk.RIGHT, fill=tk.Y, pady=8, padx=(0, 8))

    def _build_right(self, parent: tk.Frame) -> None:
        body = tk.Frame(parent, bg=C["panel"])
        tabs = SegmentedTabs(parent, body)
        tabs.pack(fill=tk.X)
        body.pack(fill=tk.BOTH, expand=True, pady=(8, 0))
        log_tab = tk.Frame(body, bg=C["panel"])
        build_tab = tk.Frame(body, bg=C["panel"])
        self._build_log_panel(log_tab)
        self._build_build_panel(build_tab)
        tabs.add("log", "服務 Log", log_tab)
        tabs.add("build", "建置輸出", build_tab)
        self._right_tabs = tabs

    def _build_log_panel(self, parent: tk.Frame) -> None:
        tools = tk.Frame(parent, bg=C["panel"])
        tools.pack(fill=tk.X, padx=10, pady=8)
        self.log_title = tk.StringVar(value="Log · （未選服務）")
        tk.Label(tools, textvariable=self.log_title, bg=C["panel"], fg=C["text"], font=F["ui_bold"]).pack(
            side=tk.LEFT
        )
        ttk.Checkbutton(tools, text="跟隨最新", variable=self._follow_log).pack(side=tk.LEFT, padx=12)
        make_button(
            tools, "重新載入", self._reload_log, variant="secondary", small=True, colors=C, fonts=F,
        ).pack(side=tk.LEFT)
        make_button(
            tools, "清空顯示", self._clear_log_view, variant="secondary", small=True, colors=C, fonts=F,
        ).pack(side=tk.LEFT, padx=6)
        make_button(
            tools, "開啟資料夾", self._open_log_dir, variant="secondary", small=True, colors=C, fonts=F,
        ).pack(side=tk.LEFT)
        self.log_text = tk.Text(
            parent, bg=C["log_bg"], fg=C["log_fg"], insertbackground=C["log_fg"], font=F["log"],
            wrap=tk.NONE, relief=tk.FLAT, padx=10, pady=10,
        )
        self.log_text.pack(fill=tk.BOTH, expand=True, padx=8, pady=(0, 8))
        self.log_text.configure(state=tk.DISABLED)

    def _build_build_panel(self, parent: tk.Frame) -> None:
        tools = tk.Frame(parent, bg=C["panel"])
        tools.pack(fill=tk.X, padx=10, pady=8)
        tk.Label(tools, text="建置輸出", bg=C["panel"], fg=C["text"], font=F["ui_bold"]).pack(side=tk.LEFT)
        self._compile_help_btn = make_button(
            tools,
            "編譯求救",
            self._on_ask_cursor_build_help,
            variant="primary",
            small=True,
            colors=C,
            fonts=F,
        )
        self._compile_help_btn.pack(side=tk.LEFT, padx=(12, 0))
        set_button_enabled(self._compile_help_btn, False, variant="primary", colors=C)
        self.build_text = tk.Text(
            parent, bg=C["log_bg"], fg=C["log_fg"], font=F["log"], wrap=tk.NONE, relief=tk.FLAT,
            padx=10, pady=10,
        )
        self.build_text.pack(fill=tk.BOTH, expand=True, padx=8, pady=(0, 8))
        self.build_text.configure(state=tk.DISABLED)

    def _set_cursor_help_enabled(self, enabled: bool) -> None:
        btn = self._compile_help_btn
        if not btn:
            return
        set_button_enabled(btn, enabled, variant="primary", colors=C)

    def _on_pick_project(self, path: Path) -> None:
        open_cursor = bool(self.project_bar.open_with_cursor.get())
        set_open_with_cursor(open_cursor)
        self.load_project(path, open_cursor=open_cursor)

    def load_project(self, root: Path, *, open_cursor: bool = False) -> None:
        try:
            catalog = build_catalog(root)
        except Exception as exc:  # noqa: BLE001
            messagebox.showerror("掃描失敗", str(exc), parent=self)
            return
        self.catalog = catalog
        self.runtime = ProjectRuntime(catalog.root)
        self.runtime.ensure()
        remember_project(catalog.root)
        self.project_bar.set_catalog(catalog)
        self._selected_service_id = catalog.services[0].id if catalog.services else None
        self._last_build_failure = None
        self._set_cursor_help_enabled(False)
        self._render_services()
        self._refresh_build_states()
        self._update_ready_summary()
        self.job_var.set("已載入專案")
        self._reload_log()
        if open_cursor:
            err = open_in_cursor(catalog.root)
            if err:
                self.job_var.set(f"已載入專案（Cursor 未開啟：{err}）")
            else:
                self.job_var.set("已載入專案，並在 Cursor 開啟")

    def _on_ask_cursor_build_help(self) -> None:
        if not self._require_catalog():
            return
        assert self.catalog
        failure = self._last_build_failure
        if not failure:
            messagebox.showinfo("編譯求救", "目前沒有建置錯誤可送出。", parent=self)
            return
        prompt = build_agent_prompt(
            root=self.catalog.root,
            target=str(failure.get("target") or ""),
            exit_code=int(failure.get("exit_code") or 1),
            log_text=str(failure.get("log") or ""),
        )
        ok = show_new_agent_confirm_dialog(
            self,
            title="編譯求救",
            intro=(
                "確認後會開啟 Cursor 並跳出確認視窗；再按確認即建立 New Agent。"
                "錯誤內容會直接帶入提示，不會先寫求助檔。"
            ),
            prompt=prompt,
        )
        if not ok:
            return
        self._launch_cursor_new_agent(prompt, status="編譯求救已送出，請在 Cursor 跳出視窗按確認")

    def _on_ask_cursor_uat_help(self) -> None:
        if not self._require_catalog():
            return
        assert self.catalog and self.runtime
        payload = show_uat_help_dialog(self)
        if not payload:
            return
        images: list[Path] = []
        source_images = list(payload.get("images") or [])
        if source_images:
            _report_dir, images = stage_uat_screenshots(
                self.runtime.uat_reports,
                title=str(payload.get("title") or "UAT 問題"),
                source_images=source_images,
            )
        prompt = build_uat_agent_prompt(
            root=self.catalog.root,
            title=str(payload.get("title") or "UAT 問題"),
            description=str(payload.get("description") or ""),
            image_paths=images,
        )
        self._launch_cursor_new_agent(prompt, status="UAT 求救已送出，請在 Cursor 跳出視窗按確認")

    def _launch_cursor_new_agent(self, prompt: str, *, status: str) -> None:
        if not self.catalog:
            return
        try:
            self.clipboard_clear()
            self.clipboard_append(prompt)
        except tk.TclError:
            pass
        err = open_project_for_new_agent(self.catalog.root)
        if err:
            messagebox.showwarning("Cursor", err, parent=self)
            return

        def fire() -> None:
            err2 = open_prompt_deeplink(prompt)
            if err2:
                messagebox.showwarning("Cursor", err2, parent=self)
                return
            self.job_var.set(status)

        self.after(new_agent_launch_delay_ms(), fire)

    def _remember_build_failure(self, *, target: str, exit_code: int, log_text: str) -> None:
        self._last_build_failure = {
            "target": target,
            "exit_code": exit_code,
            "log": log_text,
        }
        self._set_cursor_help_enabled(True)
        self._append_build("建置失敗 — 可點「編譯求救」開啟 New Agent")

    def _render_services(self) -> None:
        for child in self.service_list.winfo_children():
            child.destroy()
        self._service_rows.clear()
        if not self.catalog or not self.catalog.services:
            tk.Label(
                self.service_list,
                text="沒有對外服務。可放置 ai-project.json，或確認 launchSettings 有 applicationUrl。",
                bg=C["panel"], fg=C["muted"], font=F["ui"], wraplength=400, justify=tk.LEFT,
            ).pack(anchor=tk.W, padx=12, pady=16)
            self._left_tabs.set_badge("svc", "0/0")
            return
        groups: dict[str, list[ServiceEntry]] = {}
        for svc in self.catalog.services:
            groups.setdefault(svc.group or "其他", []).append(svc)
        for group, items in groups.items():
            tk.Label(
                self.service_list, text=group, bg=C["panel"], fg=C["muted"], font=F["section"], anchor="w",
            ).pack(fill=tk.X, padx=12, pady=(10, 2))
            for svc in items:
                self._add_service_row(svc)
        ready = sum(1 for s in self.catalog.services if self._health.get(s.id))
        self._left_tabs.set_badge("svc", f"{ready}/{len(self.catalog.services)} 就緒")

    def _add_service_row(self, svc: ServiceEntry) -> None:
        row = tk.Frame(self.service_list, bg=C["panel"], cursor="hand2")
        row.pack(fill=tk.X, padx=8, pady=1)
        status = tk.Label(row, text="離線", bg=C["panel"], fg=C["down"], font=F["row_sm"], width=8, anchor="w")
        status.pack(side=tk.LEFT, padx=4)
        name = tk.Label(row, text=svc.label, bg=C["panel"], fg=C["text"], font=F["row"], width=22, anchor="w")
        name.pack(side=tk.LEFT, padx=4)
        port = tk.Label(
            row, text=str(svc.port) if svc.port is not None else "-", bg=C["panel"], fg=C["muted"],
            font=F["row_sm"], width=8, anchor="w",
        )
        port.pack(side=tk.LEFT, padx=4)
        ops = tk.Frame(row, bg=C["panel"])
        ops.pack(side=tk.LEFT, padx=4)
        links: list[tk.Label] = []
        if svc.open_url:
            links.append(
                self._make_link(ops, "開啟", lambda s=svc: webbrowser.open(s.open_url))
            )
        if svc.hosted_by:
            tk.Label(
                ops, text=f"隨 {svc.hosted_by}", bg=C["panel"], fg=C["muted"], font=F["row_sm"],
            ).pack(side=tk.LEFT, padx=4)
        else:
            links.append(self._make_link(ops, "重啟", lambda s=svc: self._on_restart(s)))
            links.append(self._make_link(ops, "停止", lambda s=svc: self._on_stop_one(s)))

        def select(_e=None, sid=svc.id) -> None:
            self._selected_service_id = sid
            self._highlight_selection()
            self._reload_log()

        for w in (row, status, name, port):
            w.bind("<Button-1>", select)
        self._service_rows[svc.id] = {
            "row": row, "status": status, "name": name, "port": port, "ops": ops, "links": links,
        }

    def _highlight_selection(self) -> None:
        for sid, widgets in self._service_rows.items():
            bg = C["sel"] if sid == self._selected_service_id else C["panel"]
            for key in ("row", "status", "name", "port", "ops"):
                widgets[key].configure(bg=bg)
            for link in widgets.get("links", []):
                link.configure(bg=bg)

    def _refresh_build_states(self) -> None:
        if not self.catalog:
            return

        def work() -> None:
            states = {s["id"]: s for s in all_service_build_states(self.catalog)}  # type: ignore[arg-type]
            proj = all_project_build_states(self.catalog)  # type: ignore[arg-type]
            self.after(0, lambda: self._apply_build_states(states, proj))

        threading.Thread(target=work, daemon=True).start()

    def _apply_build_states(self, service_states: dict, projects: list[dict]) -> None:
        stale_svc = sum(1 for s in service_states.values() if s["status"] in ("stale", "unbuilt"))
        stale_prj = sum(1 for p in projects if p["status"] in ("stale", "unbuilt"))
        if stale_svc or stale_prj:
            self.warn_var.set(
                f"{stale_svc} 個服務、{stale_prj} 個專案需重編（到「專案」頁編譯）  拖曳中間分隔條可調寬"
            )
        else:
            self.warn_var.set("拖曳中間分隔條可調寬")
        self._project_cache = projects
        self._left_tabs.set_badge(
            "prj", f"{stale_prj} 需重編" if stale_prj else f"{len(projects)}", alert=bool(stale_prj)
        )
        self._render_projects()

    def _render_projects(self) -> None:
        for child in self.project_list.winfo_children():
            child.destroy()
        projects = list(self._project_cache)
        if self.stale_only.get():
            projects = [p for p in projects if p["status"] in ("stale", "unbuilt")]
        if not projects:
            tk.Label(
                self.project_list,
                text="沒有專案。" if self.catalog else "請先選擇專案目錄。",
                bg=C["panel"], fg=C["muted"], font=F["ui"],
            ).pack(anchor=tk.W, padx=8, pady=12)
            return
        for state in projects:
            row = tk.Frame(self.project_list, bg=C["panel"])
            row.pack(fill=tk.X, pady=2)
            badge = BUILD_BADGE.get(state["status"], "")
            tk.Label(
                row, text=state["name"], bg=C["panel"], fg=C["text"], font=F["row"], width=28, anchor="w",
            ).pack(side=tk.LEFT)
            tk.Label(
                row, text=f"{state['system']} · {state['kind']}", bg=C["panel"], fg=C["muted"],
                font=F["row_sm"], width=18, anchor="w",
            ).pack(side=tk.LEFT)
            tk.Label(
                row, text=badge, bg=C["panel"], fg=C["wait"] if badge else C["ok"],
                font=F["row_sm"], width=8, anchor="w",
            ).pack(side=tk.LEFT)
            self._make_link(row, "編譯", lambda p=state["path"]: self._build_one(p))

    def _balance_panes(self) -> None:
        self.update_idletasks()
        total = self._paned.winfo_width()
        if total > 1:
            self._paned.sash_place(0, total // 2, 0)

    def _poll_health(self) -> None:
        catalog = self.catalog
        if catalog:
            def work() -> None:
                health = {svc.id: probe_health(svc) for svc in catalog.services}
                self.after(0, lambda: self._apply_health(health))

            threading.Thread(target=work, daemon=True).start()
        self.after(HEALTH_INTERVAL_MS, self._poll_health)

    def _apply_health(self, health: dict[str, bool]) -> None:
        self._health = health
        for sid, ok in health.items():
            widgets = self._service_rows.get(sid)
            if not widgets:
                continue
            widgets["status"].configure(text="線上" if ok else "離線", fg=C["ok"] if ok else C["down"])
        self._update_ready_summary()
        if self.catalog:
            ready = sum(1 for s in self.catalog.services if health.get(s.id))
            self._left_tabs.set_badge("svc", f"{ready}/{len(self.catalog.services)} 就緒")

    def _update_ready_summary(self) -> None:
        total = len(self.catalog.services) if self.catalog else 0
        ready = sum(1 for ok in self._health.values() if ok)
        self.ready_var.set(f"就緒 {ready} / {total}")
        if not self._job_busy:
            self.job_var.set("待命")

    def _poll_log(self) -> None:
        if self._follow_log.get():
            self._append_log_tail()
        self.after(LOG_INTERVAL_MS, self._poll_log)

    def _selected_service(self) -> ServiceEntry | None:
        if not self.catalog or not self._selected_service_id:
            return None
        return service_by_id(self.catalog, self._selected_service_id)

    def _log_path_for_selected(self) -> Path | None:
        if not self.catalog or not self.runtime:
            return None
        svc = self._selected_service()
        if not svc:
            return None
        return self.runtime.log_path(host_service(self.catalog, svc).stem)

    def _reload_log(self) -> None:
        self._log_offset = 0
        self.log_text.configure(state=tk.NORMAL)
        self.log_text.delete("1.0", tk.END)
        self.log_text.configure(state=tk.DISABLED)
        svc = self._selected_service()
        self.log_title.set(f"Log · {svc.label}" if svc else "Log · （未選服務）")
        self._append_log_tail(full=True)

    def _clear_log_view(self) -> None:
        self.log_text.configure(state=tk.NORMAL)
        self.log_text.delete("1.0", tk.END)
        self.log_text.configure(state=tk.DISABLED)

    def _append_log_tail(self, *, full: bool = False) -> None:
        path = self._log_path_for_selected()
        if not path or not path.is_file():
            return
        try:
            data = path.read_bytes()
        except OSError:
            return
        if full:
            lines = data.decode("utf-8", errors="replace").splitlines()[-LOG_TAIL_LINES:]
            self._log_offset = len(data)
            chunk = "\n".join(lines) + ("\n" if lines else "")
        else:
            if len(data) <= self._log_offset:
                return
            chunk = data[self._log_offset :].decode("utf-8", errors="replace")
            self._log_offset = len(data)
        if not chunk:
            return
        self.log_text.configure(state=tk.NORMAL)
        self.log_text.insert(tk.END, chunk)
        if self._follow_log.get():
            self.log_text.see(tk.END)
        self.log_text.configure(state=tk.DISABLED)

    def _open_log_dir(self) -> None:
        if not self.runtime:
            return
        self.runtime.ensure()
        path = self.runtime.logs
        if sys.platform == "win32":
            subprocess.Popen(["explorer", str(path)])
        else:
            subprocess.Popen(["xdg-open", str(path)])

    def _append_build(self, line: str) -> None:
        self.build_text.configure(state=tk.NORMAL)
        self.build_text.insert(tk.END, line + "\n")
        self.build_text.see(tk.END)
        self.build_text.configure(state=tk.DISABLED)

    def _require_catalog(self) -> bool:
        if self.catalog and self.runtime:
            return True
        messagebox.showinfo("尚未選擇專案", "請先選擇專案目錄。", parent=self)
        return False

    def _run_job(self, title: str, fn) -> None:
        if self._job_busy:
            messagebox.showinfo("忙碌中", "請等待目前工作完成。", parent=self)
            return
        self._job_busy = True
        self.job_var.set(title)

        def work() -> None:
            err: str | None = None
            msg: str | None = None
            try:
                result = fn()
                if isinstance(result, str) and result.strip():
                    msg = result
            except Exception as exc:  # noqa: BLE001
                err = str(exc)

            def done() -> None:
                self._job_busy = False
                self.job_var.set("錯誤" if err else "完成")
                if err:
                    messagebox.showerror(title, err, parent=self)
                elif msg:
                    messagebox.showinfo(title, msg, parent=self)
                self._update_ready_summary()
                self._refresh_build_states()

            self.after(0, done)

        threading.Thread(target=work, daemon=True).start()

    def _on_start(self) -> None:
        if not self._require_catalog():
            return
        if not command_exists("dotnet"):
            messagebox.showerror("缺少工具", "找不到 dotnet。", parent=self)
            return
        catalog, runtime = self.catalog, self.runtime
        assert catalog and runtime

        def job() -> None:
            stop_all(catalog, runtime)
            time.sleep(0.5)
            start_all(catalog, runtime)

        self._run_job("啟動中…", job)

    def _on_stop(self) -> None:
        if not self._require_catalog():
            return
        catalog, runtime = self.catalog, self.runtime
        assert catalog and runtime
        self._run_job("停止中…", lambda: stop_all(catalog, runtime))

    def _on_stop_one(self, svc: ServiceEntry) -> None:
        if not self._require_catalog():
            return
        catalog, runtime = self.catalog, self.runtime
        assert catalog and runtime
        self._run_job(f"停止 {svc.label}…", lambda: stop_service(catalog, runtime, svc))

    def _on_restart(self, svc: ServiceEntry) -> None:
        if not self._require_catalog():
            return
        catalog, runtime = self.catalog, self.runtime
        assert catalog and runtime
        self._run_job(f"重啟 {svc.label}…", lambda: restart_service(catalog, runtime, svc))

    def _on_open_urls(self, frontends_only: bool) -> None:
        if not self._require_catalog():
            return
        assert self.catalog
        opened = 0
        if frontends_only:
            svc = service_by_id(self.catalog, self.catalog.frontend) if self.catalog.frontend else None
            targets = [svc] if svc and svc.open_url else [s for s in self.catalog.services if s.open_url][:1]
        else:
            targets = [s for s in self.catalog.services if s.open_url]
        for svc in targets:
            if svc and svc.open_url:
                webbrowser.open(svc.open_url)
                opened += 1
        if not opened:
            messagebox.showinfo("無 URL", "沒有可開啟的 openUrl。", parent=self)

    def _on_doctor(self) -> None:
        messagebox.showinfo("環境體檢", doctor_report(self.catalog), parent=self)

    def _running_service_labels(self) -> list[str]:
        if not self.catalog:
            return []
        return [
            svc.label
            for svc in self.catalog.services
            if self._health.get(svc.id)
        ]

    def _on_exit(self) -> None:
        running = self._running_service_labels()
        stop_services = False
        if running:
            listed = "\n".join(f"· {name}" for name in running[:10])
            more = f"\n…另有 {len(running) - 10} 個" if len(running) > 10 else ""
            ans = messagebox.askyesnocancel(
                "離開",
                f"警告：目前仍有 {len(running)} 個服務在執行中：\n"
                f"{listed}{more}\n\n"
                "離開前要一併停止這些服務嗎？\n\n"
                "「是」＝停止服務後離開\n"
                "「否」＝保留服務繼續執行，仍離開\n"
                "「取消」＝不離開",
                parent=self,
            )
            if ans is None:
                return
            stop_services = bool(ans)
        else:
            if not messagebox.askyesno(
                "離開",
                "確定離開控制台？",
                parent=self,
            ):
                return

        close_ide = bool(self.catalog) and messagebox.askyesno(
            "關閉 Cursor",
            "要一併關閉這個專案的 Cursor 視窗嗎？其他專案的視窗不會關。",
            parent=self,
        )

        if stop_services and self.catalog and self.runtime:
            try:
                stop_all(self.catalog, self.runtime)
            except Exception as exc:  # noqa: BLE001
                if not messagebox.askyesno(
                    "停止服務失敗",
                    f"停止服務時發生問題：\n{exc}\n\n仍要離開嗎？",
                    parent=self,
                ):
                    return

        if close_ide:
            err = close_cursor(self.catalog.root if self.catalog else None)
            if err:
                if not messagebox.askyesno(
                    "關閉 Cursor",
                    f"關閉 Cursor 時發生問題：\n{err}\n\n仍要離開控制台嗎？",
                    parent=self,
                ):
                    return
        self.destroy()

    def _ensure_deploy(self) -> bool:
        if not self._require_catalog():
            return False
        if resolve_deploy(self.catalog).is_complete():
            return True
        return self._edit_deploy_settings()

    def _edit_deploy_settings(self) -> bool:
        if not self.catalog:
            return False
        result = show_deploy_settings(self, resolve_deploy(self.catalog))
        if not result:
            return False
        cfg, target = result
        if target == "manifest":
            write_deploy_to_manifest(self.catalog, cfg)
            self.catalog = build_catalog(self.catalog.root)
            self.project_bar.set_catalog(self.catalog)
        else:
            save_deploy_local(self.catalog.root, cfg)
        self.job_var.set(f"已儲存部署設定（{target_label(cfg.normalized_target())}）")
        return True

    def _ensure_github(self) -> bool:
        if not self._require_catalog():
            return False
        if resolve_github(self.catalog).is_complete():
            return True
        return self._edit_github_settings()

    def _edit_github_settings(self) -> bool:
        if not self.catalog:
            return False
        result = show_github_settings(self, resolve_github(self.catalog))
        if not result:
            return False
        cfg, target = result
        apply = "+apply" in target
        base = target.replace("+apply", "")
        if base == "manifest":
            write_github_to_manifest(self.catalog, cfg)
            self.catalog = build_catalog(self.catalog.root)
            self.project_bar.set_catalog(self.catalog)
        else:
            save_github_local(self.catalog.root, cfg)
        msg = "已儲存 GitHub 設定"
        if apply:
            msg = apply_remote(self.catalog, cfg)
        self.job_var.set("已儲存 GitHub 設定")
        messagebox.showinfo("GitHub 設定", msg, parent=self)
        return True

    def _on_action(self, action: dict) -> None:
        handler = str(action.get("handler") or "")
        if action.get("requiresDeploy") and handler not in {"deploy_settings", "gcp_settings"}:
            if not self._ensure_deploy():
                return
        if action.get("requiresGithub") and handler != "github_settings":
            if not self._ensure_github():
                return

        confirm = action.get("confirm")
        if confirm and not messagebox.askyesno("確認", str(confirm), parent=self):
            return

        if handler in {"deploy_settings", "gcp_settings"}:
            self._edit_deploy_settings()
            return
        if handler in {"deploy_status", "gcp_status"}:
            messagebox.showinfo("部署狀態", deploy_status_report(self.catalog), parent=self)
            return
        if handler in {"deploy_open", "gcp_open"}:
            cfg = resolve_deploy(self.catalog)
            if cfg.normalized_target() == TARGET_NONE:
                messagebox.showinfo("不下發", "目前選擇不下發（僅本機）。若要開啟線上，請先改選發佈目標。", parent=self)
                return
            if not open_host(cfg):
                messagebox.showinfo("未設定網址", "請在部署設定中填寫對外網址或主機。", parent=self)
            return
        if handler in {"deploy_ci_hint", "gcp_ci_hint"}:
            messagebox.showinfo("部署說明", ci_hint(self.catalog), parent=self)
            return

        if handler == "github_settings":
            self._edit_github_settings()
            return
        if handler == "github_status":
            messagebox.showinfo("GitHub 狀態", github_ops.status_report(self.catalog), parent=self)
            return
        if handler == "github_open":
            if not self.catalog or not github_ops.open_on_github(self.catalog):
                messagebox.showinfo("無法開啟", "請先完成 GitHub 設定（owner/repo）。", parent=self)
            return
        if handler == "github_sync":
            assert self.catalog
            self._run_job("同步中…", lambda: github_ops.sync_from_remote(self.catalog))  # type: ignore[arg-type]
            return
        if handler == "github_publish":
            assert self.catalog
            self._run_job("發布中…", lambda: github_ops.publish_branch(self.catalog))  # type: ignore[arg-type]
            return
        if handler == "github_pr":
            assert self.catalog
            self._run_job("PR…", lambda: github_ops.create_pull_request(self.catalog))  # type: ignore[arg-type]
            return
        if handler == "github_actions":
            assert self.catalog
            self._run_job("Actions…", lambda: github_ops.watch_actions(self.catalog))  # type: ignore[arg-type]
            return

        if handler in {"build_stale", "build_services", "build_projects"}:
            self._run_build_action(handler)

    def _run_build_action(self, handler: str) -> None:
        if not self._require_catalog():
            return
        catalog = self.catalog
        assert catalog
        self._right_tabs.select("build")
        self.build_text.configure(state=tk.NORMAL)
        self.build_text.delete("1.0", tk.END)
        self.build_text.configure(state=tk.DISABLED)
        self._last_build_failure = None
        self._set_cursor_help_enabled(False)

        def job() -> None:
            targets: list[Path] = []
            if handler == "build_services":
                seen: set[str] = set()
                for svc in catalog.services:
                    host = host_service(catalog, svc)
                    if host.id in seen:
                        continue
                    seen.add(host.id)
                    targets.append(catalog.root / host.project)
            elif handler == "build_stale":
                for state in all_service_build_states(catalog):
                    if state["status"] in ("stale", "unbuilt"):
                        targets.append(catalog.root / state["path"])
                for state in all_project_build_states(catalog):
                    if state["status"] in ("stale", "unbuilt"):
                        path = catalog.root / state["path"]
                        if path not in targets:
                            targets.append(path)
            else:
                targets = [catalog.root / p.rel_dir for p in catalog.projects]
            if not targets:
                self.after(0, lambda: self._append_build("沒有需要編譯的項目。"))
                return
            all_lines: list[str] = []
            failed: tuple[str, int] | None = None
            for target in targets:
                header = f"=== build {target} ==="
                all_lines.append(header)
                self.after(0, lambda t=header: self._append_build(t))
                proc = subprocess.Popen(
                    ["dotnet", "build", str(target), "-v", "minimal", "--nologo"],
                    cwd=str(catalog.root), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    text=True, encoding="utf-8", errors="replace",
                )
                assert proc.stdout is not None
                for line in proc.stdout:
                    text = line.rstrip("\n")
                    all_lines.append(text)
                    self.after(0, lambda L=text: self._append_build(L))
                code = proc.wait()
                footer = f"exit {code}  ({target})"
                all_lines.append(footer)
                self.after(0, lambda f=footer: self._append_build(f))
                if code != 0 and failed is None:
                    failed = (str(target), code)
            if failed:
                target_s, code = failed
                log_text = "\n".join(all_lines)
                self.after(
                    0,
                    lambda t=target_s, c=code, log=log_text: self._remember_build_failure(
                        target=t, exit_code=c, log_text=log,
                    ),
                )
            else:
                self.after(0, lambda: self._set_cursor_help_enabled(False))

        self._run_job("建置中…", job)

    def _build_one(self, rel_path: str) -> None:
        if not self._require_catalog():
            return
        catalog = self.catalog
        assert catalog
        target = catalog.root / rel_path
        self._right_tabs.select("build")
        self._last_build_failure = None
        self._set_cursor_help_enabled(False)

        def job() -> None:
            lines: list[str] = [f"=== build {rel_path} ==="]
            self.after(0, lambda: self._append_build(lines[0]))
            proc = subprocess.Popen(
                ["dotnet", "build", str(target), "-v", "minimal", "--nologo"],
                cwd=str(catalog.root), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace",
            )
            assert proc.stdout is not None
            for line in proc.stdout:
                text = line.rstrip("\n")
                lines.append(text)
                self.after(0, lambda L=text: self._append_build(L))
            code = proc.wait()
            lines.append(f"exit {code}")
            self.after(0, lambda: self._append_build(f"exit {code}"))
            if code != 0:
                log_text = "\n".join(lines)
                self.after(
                    0,
                    lambda: self._remember_build_failure(
                        target=rel_path, exit_code=code, log_text=log_text,
                    ),
                )

        self._run_job(f"編譯 {Path(rel_path).name}…", job)


def main(argv: list[str] | None = None) -> int:
    import signal

    _ = argv
    app = ConsoleApp()

    def _request_quit(_signum=None, _frame=None) -> None:
        try:
            app.after(0, app.destroy)
        except tk.TclError:
            pass

    try:
        signal.signal(signal.SIGINT, _request_quit)
    except (ValueError, OSError):
        pass

    try:
        app.mainloop()
    except KeyboardInterrupt:
        try:
            app.destroy()
        except tk.TclError:
            pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
