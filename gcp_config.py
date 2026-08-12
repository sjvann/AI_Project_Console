"""GCP 部署設定：manifest → 本機 settings → 對話框。"""

from __future__ import annotations

import json
import shutil
import webbrowser
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .runtime import load_console_settings, save_console_settings
from .service_catalog import ProjectCatalog


@dataclass
class GcpConfig:
    project_id: str = ""
    zone: str = ""
    instance: str = ""
    host: str = ""
    workflow: str = "deploy-gcp.yml"

    def is_complete(self) -> bool:
        return bool(self.project_id.strip() and self.zone.strip() and self.instance.strip())

    def as_dict(self) -> dict[str, str]:
        return {
            "projectId": self.project_id,
            "zone": self.zone,
            "instance": self.instance,
            "host": self.host,
            "workflow": self.workflow,
        }


def _from_mapping(raw: dict[str, Any] | None) -> GcpConfig:
    if not isinstance(raw, dict):
        return GcpConfig()
    return GcpConfig(
        project_id=str(raw.get("projectId") or raw.get("project_id") or "").strip(),
        zone=str(raw.get("zone") or "").strip(),
        instance=str(raw.get("instance") or "").strip(),
        host=str(raw.get("host") or "").strip(),
        workflow=str(raw.get("workflow") or "deploy-gcp.yml").strip() or "deploy-gcp.yml",
    )


def gcp_from_manifest(catalog: ProjectCatalog | None) -> GcpConfig:
    if catalog is None:
        return GcpConfig()
    return _from_mapping(catalog.manifest.get("gcp") if catalog.manifest else None)


def gcp_from_local(root: Path | None) -> GcpConfig:
    if root is None:
        return GcpConfig()
    data = load_console_settings()
    by_root = data.get("gcpByProject") or {}
    if not isinstance(by_root, dict):
        return GcpConfig()
    return _from_mapping(by_root.get(str(root.resolve())))


def resolve_gcp(catalog: ProjectCatalog | None) -> GcpConfig:
    root = catalog.root if catalog else None
    local = gcp_from_local(root)
    manifest = gcp_from_manifest(catalog)
    return GcpConfig(
        project_id=local.project_id or manifest.project_id,
        zone=local.zone or manifest.zone,
        instance=local.instance or manifest.instance,
        host=local.host or manifest.host,
        workflow=local.workflow or manifest.workflow or "deploy-gcp.yml",
    )


def save_gcp_local(root: Path, cfg: GcpConfig) -> None:
    data = load_console_settings()
    by_root = data.get("gcpByProject")
    if not isinstance(by_root, dict):
        by_root = {}
    by_root[str(root.resolve())] = cfg.as_dict()
    data["gcpByProject"] = by_root
    save_console_settings(data)


def write_gcp_to_manifest(catalog: ProjectCatalog, cfg: GcpConfig) -> Path:
    path = catalog.root / "ai-project.json"
    doc: dict[str, Any] = {}
    if path.is_file():
        try:
            loaded = json.loads(path.read_text(encoding="utf-8-sig"))
            if isinstance(loaded, dict):
                doc = loaded
        except (OSError, json.JSONDecodeError):
            doc = {}
    doc["gcp"] = cfg.as_dict()
    if "name" not in doc:
        doc["name"] = catalog.name
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


def workflow_exists(catalog: ProjectCatalog | None, cfg: GcpConfig | None = None) -> bool:
    if catalog is None:
        return False
    cfg = cfg or resolve_gcp(catalog)
    return (catalog.root / ".github" / "workflows" / (cfg.workflow or "deploy-gcp.yml")).is_file()


def status_report(catalog: ProjectCatalog | None) -> str:
    cfg = resolve_gcp(catalog)
    lines = ["GCP 設定", ""]
    if catalog is None:
        lines.append("尚未選擇專案。")
        return "\n".join(lines)
    lines.extend(
        [
            f"專案目錄：{catalog.root}",
            f"projectId：{cfg.project_id or '（未設定）'}",
            f"zone：{cfg.zone or '（未設定）'}",
            f"instance：{cfg.instance or '（未設定）'}",
            f"host：{cfg.host or '（未設定）'}",
            f"workflow：{cfg.workflow}",
            f"workflow 檔案：{'存在' if workflow_exists(catalog, cfg) else '找不到'}",
            "",
            "設定齊全。" if cfg.is_complete() else "設定未齊全：請先完成「部署設定…」。",
        ]
    )
    return "\n".join(lines)


def open_host(cfg: GcpConfig) -> bool:
    host = cfg.host.strip()
    if not host:
        return False
    if not host.startswith("http"):
        host = "https://" + host
    webbrowser.open(host)
    return True


def ci_hint(catalog: ProjectCatalog | None) -> str:
    cfg = resolve_gcp(catalog)
    wf = cfg.workflow or "deploy-gcp.yml"
    lines = [
        "CI 部署說明",
        "",
        "本控制台不內建 GCP 直推腳本。建議：",
        f"1. 確認 .github/workflows/{wf} 存在",
        "2. 完成 GCP 部署設定",
        "3. 以 git tag 或 gh workflow 觸發",
        "",
    ]
    if catalog and workflow_exists(catalog, cfg):
        lines.append(f"已找到 workflow：.github/workflows/{wf}")
        if shutil.which("gh"):
            lines.append(f"  gh workflow run {wf}")
    else:
        lines.append(f"尚未找到 .github/workflows/{wf}")
    return "\n".join(lines)
