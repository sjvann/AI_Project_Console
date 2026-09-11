"""通用 headless 啟動／停止／健康檢查。"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from .runtime import ProjectRuntime
from .service_catalog import ProjectCatalog, ServiceEntry, host_service, ordered_runnable_services


def command_exists(name: str) -> bool:
    return shutil.which(name) is not None


def http_ok(url: str, *, timeout: float = 2.0) -> bool:
    if not url:
        return False
    try:
        req = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return 200 <= getattr(resp, "status", 200) < 500
    except (urllib.error.URLError, TimeoutError, OSError, ValueError):
        return False


def probe_health(svc: ServiceEntry) -> bool:
    url = svc.health
    if not url:
        return bool(svc.port) and http_ok(f"http://127.0.0.1:{svc.port}/")
    if http_ok(url):
        return True
    if url.rstrip("/").endswith("/health"):
        root = url.rstrip("/")[: -len("/health")] or url
        if not root.endswith("/"):
            root += "/"
        return http_ok(root)
    return False


def _pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    if sys.platform == "win32":
        try:
            result = subprocess.run(
                ["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"],
                capture_output=True,
                text=True,
                timeout=10,
            )
        except (OSError, subprocess.SubprocessError):
            return False
        return str(pid) in result.stdout and "INFO:" not in result.stdout.upper()
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def _kill_pid_tree(pid: int) -> None:
    if sys.platform == "win32":
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)], capture_output=True, check=False)
        return
    try:
        os.killpg(pid, 9)
    except OSError:
        try:
            os.kill(pid, 9)
        except OSError:
            pass


def write_pid(rt: ProjectRuntime, stem: str, pid: int) -> None:
    rt.pid_path(stem).write_text(f"{pid}\n", encoding="utf-8")


def clear_pid(rt: ProjectRuntime, stem: str) -> None:
    path = rt.pid_path(stem)
    if path.is_file():
        path.unlink(missing_ok=True)


def kill_pid_file(rt: ProjectRuntime, stem: str) -> bool:
    path = rt.pid_path(stem)
    if not path.is_file():
        return False
    try:
        pid = int(path.read_text(encoding="utf-8").strip().split()[0])
    except (ValueError, OSError, IndexError):
        clear_pid(rt, stem)
        return False
    killed = False
    if _pid_alive(pid):
        _kill_pid_tree(pid)
        killed = True
    clear_pid(rt, stem)
    return killed


def kill_all_pids(rt: ProjectRuntime) -> None:
    rt.ensure()
    for path in sorted(rt.pids.glob("*.pid")):
        kill_pid_file(rt, path.stem)


def _pids_on_port_windows(port: int) -> set[int]:
    pids: set[int] = set()
    try:
        result = subprocess.run(
            ["netstat", "-ano", "-p", "tcp"], capture_output=True, text=True, timeout=15
        )
    except (OSError, subprocess.SubprocessError):
        return pids
    needle = f":{port} "
    for line in result.stdout.splitlines():
        if "LISTENING" not in line.upper() or needle not in line:
            continue
        parts = line.split()
        if parts and parts[-1].isdigit():
            pids.add(int(parts[-1]))
    return pids


def release_port(port: int | None) -> None:
    if not port:
        return
    if sys.platform == "win32":
        for pid in _pids_on_port_windows(port):
            _kill_pid_tree(pid)
        return
    try:
        result = subprocess.run(
            ["lsof", "-ti", f"tcp:{port}"], capture_output=True, text=True, timeout=10
        )
    except (OSError, subprocess.SubprocessError):
        return
    for part in result.stdout.split():
        if part.isdigit():
            _kill_pid_tree(int(part))


def project_path_for(catalog: ProjectCatalog, svc: ServiceEntry) -> Path:
    rel = svc.project.replace("\\", "/")
    path = catalog.root / rel
    if path.is_dir():
        csprojs = sorted(path.glob("*.csproj"))
        return csprojs[0] if csprojs else path
    if path.suffix.lower() != ".csproj":
        candidate = path.with_suffix(".csproj")
        if candidate.is_file():
            return candidate
    return path


def python_launcher() -> list[str]:
    if sys.platform == "win32":
        py = shutil.which("py")
        if py:
            return [py, "-3"]
    for name in ("python", "python3"):
        found = shutil.which(name)
        if found:
            return [found]
    raise FileNotFoundError("找不到 Python（py / python / python3）。")


def resolve_pre_start_path(root: Path, pre_start: str | None) -> Path | None:
    if not pre_start or not str(pre_start).strip():
        return None
    root_full = root.resolve()
    full = (root_full / str(pre_start).replace("/", os.sep)).resolve()
    try:
        full.relative_to(root_full)
    except ValueError:
        return None
    return full if full.is_file() else None


def pre_start_command(script: Path) -> tuple[list[str], dict[str, str] | None]:
    ext = script.suffix.lower()
    if ext == ".py":
        env = {"PYTHONUNBUFFERED": "1"}
        return [*python_launcher(), str(script)], env
    if ext == ".sh":
        bash = shutil.which("bash")
        if not bash:
            raise FileNotFoundError("找不到 bash，無法執行 .sh preStart。")
        return [bash, str(script)], None
    if ext == ".ps1":
        shell = "powershell" if sys.platform == "win32" else "pwsh"
        return [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script)], None
    raise RuntimeError(f"不支援的 preStart 副檔名（請用 .py／.ps1／.sh）：{script.name}")


def run_pre_start(catalog: ProjectCatalog, host: ServiceEntry, log_fh) -> None:
    if not host.pre_start:
        return
    script = resolve_pre_start_path(catalog.root, host.pre_start)
    if script is None:
        raise FileNotFoundError(f"找不到 preStart（必須位於專案目錄內）：{host.pre_start}")
    log_fh.write(f"=== preStart {host.pre_start} ===\n")
    log_fh.flush()
    cmd, extra_env = pre_start_command(script)
    env = os.environ.copy()
    if extra_env:
        env.update(extra_env)
    proc = subprocess.run(
        cmd,
        cwd=str(catalog.root),
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=300,
    )
    text = f"{proc.stdout or ''}{proc.stderr or ''}"
    if text:
        log_fh.write(text if text.endswith("\n") else text + "\n")
    if proc.returncode != 0:
        raise RuntimeError(f"{host.label} 前置檢查失敗（exit {proc.returncode}）。見 Log。")
    log_fh.write("=== preStart ok ===\n")
    log_fh.flush()


def start_service(catalog: ProjectCatalog, rt: ProjectRuntime, svc: ServiceEntry) -> int | None:
    host = host_service(catalog, svc)
    stem = host.stem
    kill_pid_file(rt, stem)
    proj = project_path_for(catalog, host)
    if not proj.exists():
        raise FileNotFoundError(f"找不到專案：{proj}")

    env = os.environ.copy()
    env["ASPNETCORE_ENVIRONMENT"] = "Development"
    if host.aspnet_urls:
        env["ASPNETCORE_URLS"] = host.aspnet_urls

    log_file = rt.log_path(stem)
    log_fh = open(log_file, "a", encoding="utf-8", errors="replace")
    log_fh.write(f"\n=== {host.label} start {time.strftime('%Y-%m-%d %H:%M:%S')} ===\n")
    log_fh.flush()
    run_pre_start(catalog, host, log_fh)

    rel_proj = proj.resolve().relative_to(catalog.root.resolve())
    kwargs: dict = {
        "cwd": str(catalog.root),
        "env": env,
        "stdout": log_fh,
        "stderr": subprocess.STDOUT,
        "stdin": subprocess.DEVNULL,
    }
    if sys.platform == "win32":
        kwargs["creationflags"] = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        kwargs["close_fds"] = False
    else:
        kwargs["start_new_session"] = True

    proc = subprocess.Popen(
        ["dotnet", "run", "--project", str(rel_proj), "--no-launch-profile"],
        **kwargs,
    )
    write_pid(rt, stem, proc.pid)
    return proc.pid


def stop_service(catalog: ProjectCatalog, rt: ProjectRuntime, svc: ServiceEntry) -> None:
    host = host_service(catalog, svc)
    kill_pid_file(rt, host.stem)
    release_port(host.port)


def restart_service(catalog: ProjectCatalog, rt: ProjectRuntime, svc: ServiceEntry) -> int | None:
    host = host_service(catalog, svc)
    stop_service(catalog, rt, host)
    time.sleep(0.8)
    return start_service(catalog, rt, host)


def start_all(catalog: ProjectCatalog, rt: ProjectRuntime, *, delay: float = 0.5) -> list[str]:
    started: list[str] = []
    for svc in ordered_runnable_services(catalog):
        start_service(catalog, rt, svc)
        started.append(svc.id)
        if delay:
            time.sleep(delay)
    return started


def stop_all(catalog: ProjectCatalog, rt: ProjectRuntime) -> None:
    for svc in catalog.services:
        if svc.hosted_by:
            continue
        kill_pid_file(rt, svc.stem)
        release_port(svc.port)
    kill_all_pids(rt)


def doctor_report(catalog: ProjectCatalog | None) -> str:
    from .cursor_ops import resolve_cursor_cli

    lines = ["AI_Project 環境體檢", ""]
    lines.append(f"Python: {sys.version.split()[0]}")
    lines.append(f"dotnet: {'OK' if command_exists('dotnet') else '缺少'}")
    lines.append(f"git: {'OK' if command_exists('git') else '缺少'}")
    lines.append(f"gh: {'OK' if command_exists('gh') else '缺少（GitHub CLI，選用）'}")
    cursor = resolve_cursor_cli()
    lines.append(f"Cursor CLI: {'OK — ' + cursor if cursor else '缺少（選用；安裝 Cursor 後可自動開專案／求救）'}")
    if catalog is None:
        lines.extend(["", "尚未選擇專案目錄。"])
        return "\n".join(lines)
    lines.extend(
        [
            "",
            f"專案：{catalog.name}",
            f"路徑：{catalog.root}",
            f"摘要：{catalog.summary}",
            f"服務：{len(catalog.services)}",
        ]
    )
    for svc in catalog.services:
        port = svc.port if svc.port is not None else "-"
        lines.append(f"  - {svc.label} [{svc.id}] port={port} ({svc.source})")
    lines.append("manifest: ai-project.json 已載入" if catalog.manifest else "manifest: 無（使用掃描結果）")
    return "\n".join(lines)
