"""掃描結果與 ai-project.json 合併成服務目錄。"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .project_scan import ProjectInfo, ScanResult, external_service_candidates, scan_workspace

MANIFEST_NAME = "ai-project.json"
_NON_ALNUM = re.compile(r"[^A-Za-z0-9]+")


@dataclass
class ServiceEntry:
    id: str
    label: str
    stem: str
    project: str
    port: int | None
    health: str
    open_url: str
    group: str
    hosted_by: str | None = None
    aspnet_urls: str | None = None
    pre_start: str | None = None
    source: str = "scan"


@dataclass
class ProjectCatalog:
    root: Path
    name: str
    services: list[ServiceEntry]
    projects: list[ProjectInfo]
    start_order: list[str]
    frontend: str
    manifest: dict[str, Any]
    scan: ScanResult
    summary: str = ""


def load_manifest(root: Path) -> dict[str, Any]:
    path = root / MANIFEST_NAME
    if not path.is_file():
        return {}
    try:
        doc = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return {}
    return doc if isinstance(doc, dict) else {}


def _slug(name: str) -> str:
    return _NON_ALNUM.sub("-", name).strip("-").lower() or "service"


def _guess_group_from_project(project: str) -> str:
    return project.split("/", 1)[0] if project else "其他"


def resolve_open_path(info: ProjectInfo) -> str:
    if info.launch_url.strip():
        return info.launch_url.strip().lstrip("/")
    return "scalar" if info.is_web_api else ""


def _service_from_scan(info: ProjectInfo) -> ServiceEntry:
    port = info.ports[0] if info.ports else None
    http_urls = [u for u in info.application_urls if u.lower().startswith("http://")]
    base = http_urls[0] if http_urls else (info.application_urls[0] if info.application_urls else "")
    open_path = resolve_open_path(info)
    if base:
        health = base.rstrip("/") + "/health"
        open_url = base.rstrip("/") + (f"/{open_path}" if open_path else "/")
        aspnet = ";".join(info.application_urls) if info.application_urls else None
    elif port:
        health = f"http://127.0.0.1:{port}/health"
        open_url = f"http://localhost:{port}/{open_path}" if open_path else f"http://localhost:{port}/"
        aspnet = f"http://localhost:{port}"
    else:
        health, open_url, aspnet = "", "", None
    return ServiceEntry(
        id=_slug(info.name),
        label=info.name,
        stem=info.name,
        project=info.rel_dir,
        port=port,
        health=health,
        open_url=open_url,
        group=info.group,
        aspnet_urls=aspnet,
        source="scan",
    )


def _service_from_manifest(item: dict[str, Any]) -> ServiceEntry | None:
    project = str(item.get("project") or "").replace("\\", "/").strip()
    if not project:
        return None
    stem = Path(project).name
    if stem.lower().endswith(".csproj"):
        stem = Path(stem).stem
    label = str(item.get("label") or stem)
    sid = str(item.get("id") or _slug(label))
    port_raw = item.get("port")
    port = int(port_raw) if port_raw is not None and str(port_raw).strip() != "" else None
    health = str(item.get("health") or "").strip()
    if not health and port:
        health = f"http://127.0.0.1:{port}/health"
    open_url = str(item.get("openUrl") or item.get("open_url") or "").strip()
    aspnet = item.get("aspnetUrls") or item.get("aspnet_urls") or item.get("urls")
    hosted = item.get("hostedBy") or item.get("hosted_by")
    pre = item.get("preStart") or item.get("pre_start") or item.get("ensure")
    pre_start = str(pre).replace("\\", "/").strip() if pre else None
    return ServiceEntry(
        id=sid,
        label=label,
        stem=stem,
        project=project.removesuffix(".csproj"),
        port=port,
        health=health,
        open_url=open_url,
        group=str(item.get("group") or _guess_group_from_project(project)),
        hosted_by=str(hosted) if hosted else None,
        aspnet_urls=str(aspnet).strip() if aspnet else None,
        pre_start=pre_start or None,
        source="manifest",
    )


def _dedupe_ids(services: list[ServiceEntry]) -> list[ServiceEntry]:
    seen: dict[str, int] = {}
    out: list[ServiceEntry] = []
    for svc in services:
        base = svc.id
        n = seen.get(base, 0)
        seen[base] = n + 1
        if n:
            svc = ServiceEntry(**{**svc.__dict__, "id": f"{base}-{n + 1}"})
        out.append(svc)
    return out


def build_catalog(root: Path) -> ProjectCatalog:
    root = root.resolve()
    scan = scan_workspace(root)
    manifest = load_manifest(root)
    name = str(manifest.get("name") or root.name)
    projects = list(scan.projects)
    services: list[ServiceEntry] = []

    raw_services = manifest.get("services")
    if isinstance(raw_services, list) and raw_services:
        for item in raw_services:
            if isinstance(item, dict):
                svc = _service_from_manifest(item)
                if svc:
                    services.append(svc)
        services = _dedupe_ids(services)
    else:
        services = _dedupe_ids([_service_from_scan(p) for p in external_service_candidates(scan)])

    start_order = [str(x) for x in (manifest.get("startOrder") or manifest.get("start_order") or [])]
    if not start_order:
        start_order = [s.id for s in services if not s.hosted_by]

    frontend = str(manifest.get("frontend") or "").strip()
    if not frontend:
        for s in services:
            if s.open_url:
                frontend = s.id
                break

    summary = (
        f"{len(projects)} 專案 · {sum(1 for p in projects if p.is_executable)} 可執行 · "
        f"{len(services)} 對外服務"
    )
    if scan.error:
        summary = scan.error

    return ProjectCatalog(
        root=root,
        name=name,
        services=services,
        projects=projects,
        start_order=start_order,
        frontend=frontend,
        manifest=manifest,
        scan=scan,
        summary=summary,
    )


def service_by_id(catalog: ProjectCatalog, service_id: str) -> ServiceEntry | None:
    for svc in catalog.services:
        if svc.id == service_id:
            return svc
    return None


def host_service(catalog: ProjectCatalog, svc: ServiceEntry) -> ServiceEntry:
    if not svc.hosted_by:
        return svc
    return service_by_id(catalog, svc.hosted_by) or svc


def ordered_runnable_services(catalog: ProjectCatalog) -> list[ServiceEntry]:
    by_id = {s.id: s for s in catalog.services if not s.hosted_by}
    ordered: list[ServiceEntry] = []
    seen: set[str] = set()
    for sid in catalog.start_order:
        svc = by_id.get(sid)
        if svc and sid not in seen:
            ordered.append(svc)
            seen.add(sid)
    for svc in catalog.services:
        if svc.hosted_by or svc.id in seen:
            continue
        ordered.append(svc)
        seen.add(svc.id)
    return ordered
