"""以選定專案根為基準的建置新鮮度（簡化版）。"""

from __future__ import annotations

import time
import xml.etree.ElementTree as ET
from pathlib import Path

from .project_scan import ProjectInfo
from .service_catalog import ProjectCatalog, ServiceEntry, host_service

SOURCE_SUFFIXES = frozenset(
    {".cs", ".razor", ".cshtml", ".csproj", ".props", ".targets", ".json", ".js", ".css", ".html", ".proto", ".resx"}
)


def _tag(elem: ET.Element) -> str:
    return elem.tag.rsplit("}", 1)[-1]


def _project_file(project_dir: Path) -> Path | None:
    files = sorted(project_dir.glob("*.csproj"))
    return files[0] if files else None


def assembly_name(project_dir: Path) -> str:
    csproj = _project_file(project_dir)
    if not csproj:
        return project_dir.name
    try:
        root = ET.parse(csproj).getroot()
    except (OSError, ET.ParseError):
        return csproj.stem
    for elem in root.iter():
        if _tag(elem) == "AssemblyName" and (elem.text or "").strip():
            return elem.text.strip()
    return csproj.stem


def build_output(project_dir: Path) -> tuple[float, Path | None]:
    name = assembly_name(project_dir)
    bin_dir = project_dir / "bin"
    if not bin_dir.is_dir():
        return 0.0, None
    best_mtime = 0.0
    best_path: Path | None = None
    for dll in bin_dir.rglob(f"{name}.dll"):
        try:
            mtime = dll.stat().st_mtime
        except OSError:
            continue
        if mtime > best_mtime:
            best_mtime = mtime
            best_path = dll
    return best_mtime, best_path


def newest_source(project_dir: Path) -> tuple[float, Path | None]:
    best_mtime = 0.0
    best_path: Path | None = None
    if not project_dir.is_dir():
        return 0.0, None
    for path in project_dir.rglob("*"):
        if not path.is_file() or any(part in {"bin", "obj"} for part in path.parts):
            continue
        if path.suffix.lower() not in SOURCE_SUFFIXES:
            continue
        try:
            mtime = path.stat().st_mtime
        except OSError:
            continue
        if mtime > best_mtime:
            best_mtime = mtime
            best_path = path
    return best_mtime, best_path


def project_dir_for_service(catalog: ProjectCatalog, svc: ServiceEntry) -> Path:
    host = host_service(catalog, svc)
    path = catalog.root / host.project.replace("\\", "/")
    return path.parent if path.is_file() else path


def service_build_state(catalog: ProjectCatalog, svc: ServiceEntry) -> dict:
    host = host_service(catalog, svc)
    project_dir = project_dir_for_service(catalog, host)
    src_mtime, _ = newest_source(project_dir)
    out_mtime, out_path = build_output(project_dir)
    if out_mtime <= 0:
        status = "unbuilt"
    elif src_mtime > out_mtime:
        status = "stale"
    else:
        status = "fresh"
    return {
        "id": svc.id,
        "label": svc.label,
        "status": status,
        "path": host.project,
        "output": str(out_path) if out_path else "",
        "checkedAt": time.time(),
    }


def all_service_build_states(catalog: ProjectCatalog) -> list[dict]:
    seen: set[str] = set()
    out: list[dict] = []
    for svc in catalog.services:
        host = host_service(catalog, svc)
        if host.id in seen:
            continue
        seen.add(host.id)
        out.append(service_build_state(catalog, host))
    return out


def project_build_state(root: Path, info: ProjectInfo) -> dict:
    project_dir = root / info.rel_dir
    src_mtime, _ = newest_source(project_dir)
    out_mtime, out_path = build_output(project_dir)
    if out_mtime <= 0:
        status = "unbuilt"
    elif src_mtime > out_mtime:
        status = "stale"
    else:
        status = "fresh"
    kind = "測試" if info.is_test else ("核心" if info.is_executable or info.is_web else "函式庫")
    return {
        "id": info.rel_dir,
        "name": info.name,
        "system": info.group,
        "kind": kind,
        "path": info.rel_dir,
        "status": status,
        "output": str(out_path) if out_path else "",
    }


def all_project_build_states(catalog: ProjectCatalog) -> list[dict]:
    states = [project_build_state(catalog.root, p) for p in catalog.projects]
    kind_order = {"核心": 0, "函式庫": 1, "測試": 2}
    states.sort(key=lambda s: (s["system"], kind_order.get(str(s["kind"]), 9), s["name"]))
    return states
