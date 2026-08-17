"""掃描專案目錄內的 .csproj 與 launchSettings.json。"""

from __future__ import annotations

import json
import re
import subprocess
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from urllib.parse import urlparse

_SKIP_DIR_NAMES = frozenset(
    {"bin", "obj", ".git", "node_modules", ".ai_project", ".ai_house", "packages"}
)
_URL_RE = re.compile(r"https?://[^\s;]+", re.I)
_API_DOC_PACKAGES = frozenset(
    {"scalar.aspnetcore", "microsoft.aspnetcore.openapi", "swashbuckle.aspnetcore"}
)
_UI_NAME_SEGMENTS = frozenset({"web", "frontend", "blazor", "ui"})


@dataclass
class ProjectInfo:
    rel_dir: str
    name: str
    csproj: Path
    sdk: str
    output_type: str
    is_executable: bool
    is_web: bool
    is_web_api: bool
    is_test: bool
    ports: list[int] = field(default_factory=list)
    application_urls: list[str] = field(default_factory=list)
    launch_url: str = ""
    group: str = ""


@dataclass
class ScanResult:
    root: Path
    projects: list[ProjectInfo]
    error: str = ""


def _tag(elem: ET.Element) -> str:
    return elem.tag.rsplit("}", 1)[-1]


def _rel_posix(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def _guess_group(rel_dir: str) -> str:
    top = rel_dir.split("/", 1)[0] if rel_dir else ""
    return top or "其他"


def list_csproj_paths(root: Path) -> list[Path]:
    root = root.resolve()
    try:
        result = subprocess.run(
            ["git", "ls-files", "-c", "-o", "--exclude-standard", "-z"],
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
    except (OSError, subprocess.SubprocessError):
        result = None

    found: list[Path] = []
    if result and result.returncode == 0 and result.stdout:
        for rel in result.stdout.split("\0"):
            if not rel.endswith(".csproj"):
                continue
            path = (root / rel).resolve()
            if path.is_file():
                found.append(path)
    else:
        for path in root.rglob("*.csproj"):
            if any(part in _SKIP_DIR_NAMES for part in path.parts):
                continue
            found.append(path.resolve())
    found.sort(key=lambda p: str(p).lower())
    return found


def _last_name_segment(name: str) -> str:
    return name.rsplit(".", 1)[-1]


def name_looks_like_api(name: str) -> bool:
    return _last_name_segment(name).lower() == "api"


def name_looks_like_ui_web(name: str) -> bool:
    return _last_name_segment(name).lower() in _UI_NAME_SEGMENTS


def _has_razor_files(project_dir: Path) -> bool:
    try:
        for path in project_dir.rglob("*.razor"):
            if any(part in _SKIP_DIR_NAMES for part in path.parts):
                continue
            return True
    except OSError:
        return False
    return False


def _parse_csproj_meta(csproj: Path) -> tuple[str, str, bool, bool, bool, bool]:
    try:
        root = ET.parse(csproj).getroot()
    except (OSError, ET.ParseError):
        return "", "Library", False, False, False, False
    sdk = (root.get("Sdk") or "").strip()
    is_web = "Microsoft.NET.Sdk.Web" in sdk
    output_type = "Library"
    has_api_docs = False
    has_ui_markers = False
    for elem in root.iter():
        tag = _tag(elem)
        if tag == "OutputType" and (elem.text or "").strip() and output_type == "Library":
            output_type = elem.text.strip()
        if tag == "PackageReference":
            include = (elem.get("Include") or "").strip()
            if include.lower() in _API_DOC_PACKAGES:
                has_api_docs = True
            lowered = include.lower()
            if "components.web" in lowered or "blazor" in lowered:
                has_ui_markers = True
        if tag.lower().startswith("blazor"):
            has_ui_markers = True
    is_exe = output_type.lower() in ("exe", "winexe") or is_web
    return sdk, output_type, is_exe, is_web, has_api_docs, has_ui_markers


def _parse_launch_settings(project_dir: Path) -> tuple[list[str], list[int], str]:
    path = project_dir / "Properties" / "launchSettings.json"
    if not path.is_file():
        return [], [], ""
    try:
        doc = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return [], [], ""

    urls: list[str] = []
    ports: list[int] = []
    launch_url = ""
    profiles = doc.get("profiles") or {}
    if not isinstance(profiles, dict):
        return [], [], ""

    def _profile_rank(name: str) -> int:
        key = name.lower()
        if key in {"http", "development"}:
            return 0
        if "http" in key and "https" not in key:
            return 1
        if key == "https":
            return 9
        return 5

    for _name, profile in sorted(profiles.items(), key=lambda kv: _profile_rank(str(kv[0]))):
        if not isinstance(profile, dict):
            continue
        if not launch_url:
            launch_url = str(profile.get("launchUrl") or "").strip()
        raw = profile.get("applicationUrl") or profile.get("applicationUrls") or ""
        if not raw:
            continue
        for match in _URL_RE.findall(str(raw)):
            if match.lower().startswith("https://"):
                continue
            urls.append(match.rstrip("/"))
            parsed = urlparse(match)
            if parsed.port:
                ports.append(parsed.port)
            elif parsed.scheme == "http":
                ports.append(80)

    seen_u: set[str] = set()
    uniq_urls = [u for u in urls if not (u in seen_u or seen_u.add(u))]  # type: ignore[func-returns-value]
    seen_p: set[int] = set()
    uniq_ports = [p for p in ports if not (p in seen_p or seen_p.add(p))]  # type: ignore[func-returns-value]
    return uniq_urls, uniq_ports, launch_url


def scan_project(csproj: Path, root: Path) -> ProjectInfo:
    project_dir = csproj.parent
    sdk, output_type, is_exe, is_web, has_api_docs, has_ui_markers = _parse_csproj_meta(csproj)
    urls, ports, launch_url = _parse_launch_settings(project_dir)
    rel_dir = _rel_posix(root, project_dir)
    name = csproj.stem
    parts = rel_dir.lower().split("/")
    is_test = "tests" in parts or name.lower().endswith(".tests") or name.lower().endswith("test")
    is_web_api = (
        is_web
        and not has_ui_markers
        and not name_looks_like_ui_web(name)
        and not _has_razor_files(project_dir)
        and (has_api_docs or name_looks_like_api(name))
    )
    return ProjectInfo(
        rel_dir=rel_dir,
        name=name,
        csproj=csproj,
        sdk=sdk,
        output_type=output_type,
        is_executable=is_exe and not is_test,
        is_web=is_web,
        is_web_api=is_web_api,
        is_test=is_test,
        ports=ports,
        application_urls=urls,
        launch_url=launch_url,
        group=_guess_group(rel_dir),
    )


def scan_workspace(root: Path) -> ScanResult:
    root = root.resolve()
    if not root.is_dir():
        return ScanResult(root=root, projects=[], error=f"目錄不存在：{root}")
    projects = [scan_project(p, root) for p in list_csproj_paths(root)]
    return ScanResult(root=root, projects=projects)


def external_service_candidates(scan: ScanResult) -> list[ProjectInfo]:
    return [p for p in scan.projects if p.is_executable and (p.ports or p.application_urls)]
