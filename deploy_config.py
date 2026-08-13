"""部署目標設定：不下發／GCP／自家機房／Azure。相容既有 gcp 欄位。"""

from __future__ import annotations

import json
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from .gcp_config import GcpConfig, _from_mapping as gcp_from_mapping
from .gcp_config import open_host as open_gcp_host
from .gcp_config import workflow_exists
from .runtime import load_console_settings, save_console_settings
from .service_catalog import ProjectCatalog

TARGET_NONE = "none"
TARGET_GCP = "gcp"
TARGET_ONPREM = "onprem"
TARGET_AZURE = "azure"

DEPLOY_TARGETS: list[tuple[str, str]] = [
    (TARGET_NONE, "不下發（僅本機）"),
    (TARGET_GCP, "Google Cloud (GCP)"),
    (TARGET_ONPREM, "自家機房／遠端伺服器"),
    (TARGET_AZURE, "Microsoft Azure"),
]

ONPREM_PROTOCOLS: list[tuple[str, str]] = [
    ("ssh", "SSH／SCP／rsync"),
    ("winrm", "WinRM（PowerShell 遠端）"),
    ("iis", "IIS 網站"),
    ("smb", "檔案分享（SMB／網路磁碟）"),
]

PROTOCOL_DEFAULT_PORT = {
    "ssh": "22",
    "winrm": "5985",
    "iis": "80",
    "smb": "445",
}

_TARGET_LABELS = {k: v for k, v in DEPLOY_TARGETS}
_PROTOCOL_LABELS = {k: v for k, v in ONPREM_PROTOCOLS}


def target_label(key: str) -> str:
    return _TARGET_LABELS.get(key, _TARGET_LABELS[TARGET_NONE])


def protocol_label(key: str) -> str:
    return _PROTOCOL_LABELS.get(key, _PROTOCOL_LABELS["ssh"])


def target_key_from_label(label: str) -> str:
    for key, text in DEPLOY_TARGETS:
        if text == label:
            return key
    return TARGET_NONE


def protocol_key_from_label(label: str) -> str:
    for key, text in ONPREM_PROTOCOLS:
        if text == label:
            return key
    return "ssh"


@dataclass
class OnpremConfig:
    protocol: str = "ssh"
    host: str = ""
    port: str = "22"
    user: str = ""
    remote_path: str = ""
    open_url: str = ""
    key_path: str = ""

    def is_complete(self) -> bool:
        proto = (self.protocol or "ssh").strip() or "ssh"
        if proto == "smb":
            return bool(self.host.strip() or self.remote_path.strip())
        return bool(self.host.strip() and self.user.strip())

    def as_dict(self) -> dict[str, str]:
        return {
            "protocol": (self.protocol or "ssh").strip() or "ssh",
            "host": self.host,
            "port": self.port,
            "user": self.user,
            "remotePath": self.remote_path,
            "openUrl": self.open_url,
            "keyPath": self.key_path,
        }


@dataclass
class AzureConfig:
    subscription_id: str = ""
    resource_group: str = ""
    app_name: str = ""
    region: str = ""
    open_url: str = ""
    workflow: str = "deploy-azure.yml"

    def is_complete(self) -> bool:
        return bool(
            self.subscription_id.strip()
            and self.resource_group.strip()
            and self.app_name.strip()
        )

    def as_dict(self) -> dict[str, str]:
        return {
            "subscriptionId": self.subscription_id,
            "resourceGroup": self.resource_group,
            "appName": self.app_name,
            "region": self.region,
            "openUrl": self.open_url,
            "workflow": self.workflow or "deploy-azure.yml",
        }


@dataclass
class DeployConfig:
    target: str = TARGET_NONE
    gcp: GcpConfig = field(default_factory=GcpConfig)
    onprem: OnpremConfig = field(default_factory=OnpremConfig)
    azure: AzureConfig = field(default_factory=AzureConfig)

    def normalized_target(self) -> str:
        key = (self.target or "").strip() or TARGET_NONE
        return key if key in _TARGET_LABELS else TARGET_NONE

    def is_complete(self) -> bool:
        target = self.normalized_target()
        if target == TARGET_NONE:
            return True
        if target == TARGET_GCP:
            return self.gcp.is_complete()
        if target == TARGET_ONPREM:
            return self.onprem.is_complete()
        if target == TARGET_AZURE:
            return self.azure.is_complete()
        return False

    def public_url(self) -> str:
        target = self.normalized_target()
        if target == TARGET_GCP:
            return self.gcp.host.strip()
        if target == TARGET_ONPREM:
            return (self.onprem.open_url or self.onprem.host).strip()
        if target == TARGET_AZURE:
            return self.azure.open_url.strip()
        return ""

    def as_dict(self) -> dict[str, Any]:
        return {
            "target": self.normalized_target(),
            "gcp": self.gcp.as_dict(),
            "onprem": self.onprem.as_dict(),
            "azure": self.azure.as_dict(),
        }


def _onprem_from_mapping(raw: dict[str, Any] | None) -> OnpremConfig:
    if not isinstance(raw, dict):
        return OnpremConfig()
    proto = str(raw.get("protocol") or "ssh").strip() or "ssh"
    if proto not in _PROTOCOL_LABELS:
        proto = "ssh"
    port = str(raw.get("port") or "").strip() or PROTOCOL_DEFAULT_PORT.get(proto, "22")
    return OnpremConfig(
        protocol=proto,
        host=str(raw.get("host") or "").strip(),
        port=port,
        user=str(raw.get("user") or "").strip(),
        remote_path=str(raw.get("remotePath") or raw.get("remote_path") or "").strip(),
        open_url=str(raw.get("openUrl") or raw.get("open_url") or "").strip(),
        key_path=str(raw.get("keyPath") or raw.get("key_path") or "").strip(),
    )


def _azure_from_mapping(raw: dict[str, Any] | None) -> AzureConfig:
    if not isinstance(raw, dict):
        return AzureConfig()
    return AzureConfig(
        subscription_id=str(raw.get("subscriptionId") or raw.get("subscription_id") or "").strip(),
        resource_group=str(raw.get("resourceGroup") or raw.get("resource_group") or "").strip(),
        app_name=str(raw.get("appName") or raw.get("app_name") or "").strip(),
        region=str(raw.get("region") or "").strip(),
        open_url=str(raw.get("openUrl") or raw.get("open_url") or raw.get("host") or "").strip(),
        workflow=str(raw.get("workflow") or "deploy-azure.yml").strip() or "deploy-azure.yml",
    )


def _deploy_from_mapping(raw: dict[str, Any] | None, *, gcp_fallback: dict[str, Any] | None = None) -> DeployConfig:
    if not isinstance(raw, dict):
        raw = {}
    target = str(raw.get("target") or "").strip()
    gcp_raw = raw.get("gcp") if isinstance(raw.get("gcp"), dict) else None
    if gcp_raw is None:
        gcp_raw = gcp_fallback
    gcp = gcp_from_mapping(gcp_raw)
    onprem = _onprem_from_mapping(raw.get("onprem") if isinstance(raw.get("onprem"), dict) else None)
    azure = _azure_from_mapping(raw.get("azure") if isinstance(raw.get("azure"), dict) else None)
    if not target:
        if gcp.project_id or gcp.instance:
            target = TARGET_GCP
        elif onprem.host or onprem.remote_path:
            target = TARGET_ONPREM
        elif azure.subscription_id or azure.app_name:
            target = TARGET_AZURE
        else:
            target = TARGET_NONE
    if target not in _TARGET_LABELS:
        target = TARGET_NONE
    return DeployConfig(target=target, gcp=gcp, onprem=onprem, azure=azure)


def _pick(a: str, b: str) -> str:
    return a.strip() if a.strip() else b


def _merge_gcp(local: GcpConfig, manifest: GcpConfig) -> GcpConfig:
    return GcpConfig(
        project_id=_pick(local.project_id, manifest.project_id),
        zone=_pick(local.zone, manifest.zone),
        instance=_pick(local.instance, manifest.instance),
        host=_pick(local.host, manifest.host),
        workflow=_pick(local.workflow, manifest.workflow) or "deploy-gcp.yml",
    )


def _merge_onprem(local: OnpremConfig, manifest: OnpremConfig) -> OnpremConfig:
    proto = _pick(local.protocol, manifest.protocol) or "ssh"
    return OnpremConfig(
        protocol=proto,
        host=_pick(local.host, manifest.host),
        port=_pick(local.port, manifest.port) or PROTOCOL_DEFAULT_PORT.get(proto, "22"),
        user=_pick(local.user, manifest.user),
        remote_path=_pick(local.remote_path, manifest.remote_path),
        open_url=_pick(local.open_url, manifest.open_url),
        key_path=_pick(local.key_path, manifest.key_path),
    )


def _merge_azure(local: AzureConfig, manifest: AzureConfig) -> AzureConfig:
    return AzureConfig(
        subscription_id=_pick(local.subscription_id, manifest.subscription_id),
        resource_group=_pick(local.resource_group, manifest.resource_group),
        app_name=_pick(local.app_name, manifest.app_name),
        region=_pick(local.region, manifest.region),
        open_url=_pick(local.open_url, manifest.open_url),
        workflow=_pick(local.workflow, manifest.workflow) or "deploy-azure.yml",
    )


def deploy_from_manifest(catalog: ProjectCatalog | None) -> DeployConfig:
    if catalog is None:
        return DeployConfig()
    doc = catalog.manifest if isinstance(catalog.manifest, dict) else {}
    deploy_raw = doc.get("deploy") if isinstance(doc.get("deploy"), dict) else {}
    gcp_raw = doc.get("gcp") if isinstance(doc.get("gcp"), dict) else None
    # 允許 onprem／azure 寫在根層或 deploy 底下
    merged: dict[str, Any] = dict(deploy_raw)
    if "onprem" not in merged and isinstance(doc.get("onprem"), dict):
        merged["onprem"] = doc["onprem"]
    if "azure" not in merged and isinstance(doc.get("azure"), dict):
        merged["azure"] = doc["azure"]
    return _deploy_from_mapping(merged, gcp_fallback=gcp_raw)


def deploy_from_local(root: Path | None) -> DeployConfig:
    if root is None:
        return DeployConfig()
    data = load_console_settings()
    by_root = data.get("deployByProject") or {}
    if not isinstance(by_root, dict):
        by_root = {}
    raw = by_root.get(str(root.resolve()))
    gcp_by = data.get("gcpByProject") or {}
    gcp_raw = gcp_by.get(str(root.resolve())) if isinstance(gcp_by, dict) else None
    if not isinstance(raw, dict) and isinstance(gcp_raw, dict):
        return _deploy_from_mapping({"target": TARGET_GCP, "gcp": gcp_raw})
    return _deploy_from_mapping(raw if isinstance(raw, dict) else {}, gcp_fallback=gcp_raw if isinstance(gcp_raw, dict) else None)


def _has_local_deploy(root: Path | None) -> bool:
    if root is None:
        return False
    data = load_console_settings()
    by_root = data.get("deployByProject")
    return isinstance(by_root, dict) and isinstance(by_root.get(str(root.resolve())), dict)


def resolve_deploy(catalog: ProjectCatalog | None) -> DeployConfig:
    root = catalog.root if catalog else None
    local = deploy_from_local(root)
    manifest = deploy_from_manifest(catalog)
    # 本機若已寫過 deploy，包含「不下發」，優先採用；否則用 manifest 或從 GCP 欄位推斷
    if _has_local_deploy(root):
        target = local.target or TARGET_NONE
    else:
        target = local.target if local.target and local.target != TARGET_NONE else (manifest.target or TARGET_NONE)
    return DeployConfig(
        target=target,
        gcp=_merge_gcp(local.gcp, manifest.gcp),
        onprem=_merge_onprem(local.onprem, manifest.onprem),
        azure=_merge_azure(local.azure, manifest.azure),
    )


def save_deploy_local(root: Path, cfg: DeployConfig) -> None:
    data = load_console_settings()
    by_root = data.get("deployByProject")
    if not isinstance(by_root, dict):
        by_root = {}
    by_root[str(root.resolve())] = cfg.as_dict()
    data["deployByProject"] = by_root
    # 同步舊鍵，讓既有 GCP 讀取路徑仍可用
    gcp_by = data.get("gcpByProject")
    if not isinstance(gcp_by, dict):
        gcp_by = {}
    gcp_by[str(root.resolve())] = cfg.gcp.as_dict()
    data["gcpByProject"] = gcp_by
    save_console_settings(data)


def write_deploy_to_manifest(catalog: ProjectCatalog, cfg: DeployConfig) -> Path:
    path = catalog.root / "ai-project.json"
    doc: dict[str, Any] = {}
    if path.is_file():
        try:
            loaded = json.loads(path.read_text(encoding="utf-8-sig"))
            if isinstance(loaded, dict):
                doc = loaded
        except (OSError, json.JSONDecodeError):
            doc = {}
    doc["deploy"] = {"target": cfg.normalized_target()}
    doc["gcp"] = cfg.gcp.as_dict()
    doc["onprem"] = cfg.onprem.as_dict()
    doc["azure"] = cfg.azure.as_dict()
    if "name" not in doc:
        doc["name"] = catalog.name
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


def _looks_like_lan(host: str) -> bool:
    text = host.strip()
    if text.startswith("http://") or text.startswith("https://"):
        text = urlparse(text).hostname or text
    if text in {"localhost", "127.0.0.1"}:
        return True
    parts = text.split(".")
    if len(parts) == 4 and all(p.isdigit() and 0 <= int(p) <= 255 for p in parts):
        a, b = int(parts[0]), int(parts[1])
        return a == 10 or a == 192 and b == 168 or a == 172 and 16 <= b <= 31
    return False


def open_host(cfg: DeployConfig | GcpConfig) -> bool:
    if isinstance(cfg, GcpConfig):
        return open_gcp_host(cfg)
    url = cfg.public_url()
    if not url:
        return False
    if not url.startswith("http"):
        scheme = "http://" if _looks_like_lan(url) else "https://"
        url = scheme + url
    import webbrowser

    webbrowser.open(url)
    return True


def status_report(catalog: ProjectCatalog | None) -> str:
    cfg = resolve_deploy(catalog)
    lines = ["部署設定", ""]
    if catalog is None:
        lines.append("尚未選擇專案。")
        return "\n".join(lines)
    target = cfg.normalized_target()
    lines.extend(
        [
            f"專案目錄：{catalog.root}",
            f"發佈目標：{target_label(target)}",
            "",
        ]
    )
    if target == TARGET_NONE:
        lines.append("此專案只在本機執行，不發佈到雲端或遠端伺服器。")
        return "\n".join(lines)
    if target == TARGET_GCP:
        g = cfg.gcp
        lines.extend(
            [
                f"projectId：{g.project_id or '（未設定）'}",
                f"zone：{g.zone or '（未設定）'}",
                f"instance：{g.instance or '（未設定）'}",
                f"host：{g.host or '（未設定）'}",
                f"workflow：{g.workflow}",
                f"workflow 檔案：{'存在' if workflow_exists(catalog, g) else '找不到'}",
            ]
        )
    elif target == TARGET_ONPREM:
        o = cfg.onprem
        lines.extend(
            [
                f"協定：{protocol_label(o.protocol)}",
                f"主機：{o.host or '（未設定）'}",
                f"連接埠：{o.port or '（預設）'}",
                f"帳號：{o.user or '（未設定）'}",
                f"遠端路徑：{o.remote_path or '（未設定）'}",
                f"SSH 金鑰：{o.key_path or '（未設定，使用預設 agent）'}",
                f"對外網址：{o.open_url or '（未設定）'}",
            ]
        )
    elif target == TARGET_AZURE:
        a = cfg.azure
        lines.extend(
            [
                f"subscriptionId：{a.subscription_id or '（未設定）'}",
                f"resourceGroup：{a.resource_group or '（未設定）'}",
                f"appName：{a.app_name or '（未設定）'}",
                f"region：{a.region or '（未設定）'}",
                f"對外網址：{a.open_url or '（未設定）'}",
                f"workflow：{a.workflow}",
            ]
        )
    lines.append("")
    lines.append("設定齊全。" if cfg.is_complete() else "設定未齊全：請先完成「部署設定…」。")
    return "\n".join(lines)


def ci_hint(catalog: ProjectCatalog | None) -> str:
    cfg = resolve_deploy(catalog)
    target = cfg.normalized_target()
    if target == TARGET_NONE:
        return "目前選擇不下發。若之後要發佈，請在「部署設定…」改選目標。"
    if target == TARGET_GCP:
        wf = cfg.gcp.workflow or "deploy-gcp.yml"
        lines = [
            "GCP CI 部署說明",
            "",
            "本控制台不內建 GCP 直推腳本。建議：",
            f"1. 確認 .github/workflows/{wf} 存在",
            "2. 完成 GCP 部署設定",
            "3. 以 git tag 或 gh workflow 觸發",
            "",
        ]
        if catalog and workflow_exists(catalog, cfg.gcp):
            lines.append(f"已找到 workflow：.github/workflows/{wf}")
            if shutil.which("gh"):
                lines.append(f"  gh workflow run {wf}")
        else:
            lines.append(f"尚未找到 .github/workflows/{wf}")
        return "\n".join(lines)
    if target == TARGET_ONPREM:
        o = cfg.onprem
        proto = o.protocol or "ssh"
        port = o.port or PROTOCOL_DEFAULT_PORT.get(proto, "")
        lines = [
            "自家機房／遠端伺服器部署說明",
            "",
            "本控制台先保存連線設定，實際發佈請用既有腳本或 CI。",
            "",
            f"協定：{protocol_label(proto)}",
            f"主機：{o.host or '（未設定）'}",
            f"連接埠：{port or '（預設）'}",
            f"帳號：{o.user or '（未設定）'}",
            f"遠端路徑：{o.remote_path or '（未設定）'}",
            "",
        ]
        if proto == "ssh":
            dest = f"{o.user}@{o.host}" if o.user and o.host else "user@host"
            path = o.remote_path or "/opt/app"
            key = f"-i {o.key_path} " if o.key_path else ""
            extra = f"-P {port} " if port and port != "22" else ""
            lines.extend(
                [
                    "常見作法：建置後以 scp／rsync 複製，再於遠端重啟服務。",
                    f"  scp {extra}{key}<artifact> {dest}:{path}",
                    f"  ssh {key}{dest} systemctl restart <service>",
                ]
            )
        elif proto == "winrm":
            lines.append("常見作法：WinRM／PowerShell Remoting 複製檔案並重啟 IIS 或 Windows 服務。")
        elif proto == "iis":
            lines.append("常見作法：dotnet publish 後以 msdeploy 或手動複製到 IIS 網站實體路徑。")
        else:
            lines.append("常見作法：對應到 UNC 路徑後複製發佈輸出。")
        return "\n".join(lines)
    a = cfg.azure
    wf = a.workflow or "deploy-azure.yml"
    return "\n".join(
        [
            "Azure 部署說明",
            "",
            "本控制台不內建 Azure 直推腳本。建議以 GitHub Actions 或 Azure DevOps 發佈。",
            f"subscription：{a.subscription_id or '（未設定）'}",
            f"resourceGroup：{a.resource_group or '（未設定）'}",
            f"appName：{a.app_name or '（未設定）'}",
            f"workflow：{wf}",
        ]
    )
