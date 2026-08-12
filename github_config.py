"""GitHub 倉庫設定：git remote 偵測 → manifest → 本機 settings。"""

from __future__ import annotations

import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .runtime import load_console_settings, save_console_settings
from .service_catalog import ProjectCatalog

_SLUG_RE = re.compile(r"[:/]([^/:]+)/([^/]+?)(?:\.git)?$")


@dataclass
class GithubConfig:
    owner: str = ""
    repo: str = ""
    remote: str = "origin"
    default_branch: str = "main"
    # 完整 clone／push URL；空則用 https://github.com/{owner}/{repo}.git
    url: str = ""

    def is_complete(self) -> bool:
        return bool(self.owner.strip() and self.repo.strip()) or bool(self.url.strip())

    def slug(self) -> str:
        if self.owner and self.repo:
            return f"{self.owner}/{self.repo}"
        return ""

    def web_url(self) -> str:
        slug = self.slug()
        return f"https://github.com/{slug}" if slug else ""

    def remote_url(self) -> str:
        if self.url.strip():
            return self.url.strip()
        slug = self.slug()
        return f"https://github.com/{slug}.git" if slug else ""

    def as_dict(self) -> dict[str, str]:
        return {
            "owner": self.owner,
            "repo": self.repo,
            "remote": self.remote or "origin",
            "defaultBranch": self.default_branch or "main",
            "url": self.url,
        }


def parse_github_slug(remote_url: str) -> tuple[str, str]:
    text = (remote_url or "").strip()
    if not text:
        return "", ""
    m = _SLUG_RE.search(text.replace("\\", "/"))
    if not m:
        return "", ""
    return m.group(1), m.group(2)


def _git(root: Path, args: list[str]) -> tuple[int, str]:
    try:
        result = subprocess.run(
            ["git", *args],
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        return 1, str(exc)
    out = (result.stdout or result.stderr or "").strip()
    return result.returncode, out


def detect_from_git(root: Path, *, remote: str = "origin") -> GithubConfig:
    cfg = GithubConfig(remote=remote or "origin")
    code, url = _git(root, ["remote", "get-url", cfg.remote])
    if code == 0 and url:
        cfg.url = url
        owner, repo = parse_github_slug(url)
        cfg.owner, cfg.repo = owner, repo
    code, branch = _git(root, ["rev-parse", "--abbrev-ref", "HEAD"])
    if code == 0 and branch and branch != "HEAD":
        cfg.default_branch = branch
    else:
        code, sym = _git(root, ["symbolic-ref", "refs/remotes/origin/HEAD"])
        if code == 0 and sym:
            cfg.default_branch = sym.rsplit("/", 1)[-1]
    return cfg


def _from_mapping(raw: dict[str, Any] | None) -> GithubConfig:
    if not isinstance(raw, dict):
        return GithubConfig()
    return GithubConfig(
        owner=str(raw.get("owner") or "").strip(),
        repo=str(raw.get("repo") or "").strip(),
        remote=str(raw.get("remote") or "origin").strip() or "origin",
        default_branch=str(raw.get("defaultBranch") or raw.get("default_branch") or "main").strip()
        or "main",
        url=str(raw.get("url") or "").strip(),
    )


def github_from_manifest(catalog: ProjectCatalog | None) -> GithubConfig:
    if catalog is None:
        return GithubConfig()
    return _from_mapping(catalog.manifest.get("github") if catalog.manifest else None)


def github_from_local(root: Path | None) -> GithubConfig:
    if root is None:
        return GithubConfig()
    data = load_console_settings()
    by_root = data.get("githubByProject") or {}
    if not isinstance(by_root, dict):
        return GithubConfig()
    return _from_mapping(by_root.get(str(root.resolve())))


def resolve_github(catalog: ProjectCatalog | None) -> GithubConfig:
    """優先本機覆寫 → manifest → git remote 偵測。"""
    if catalog is None:
        return GithubConfig()
    local = github_from_local(catalog.root)
    manifest = github_from_manifest(catalog)
    detected = detect_from_git(
        catalog.root, remote=local.remote or manifest.remote or "origin"
    )

    remote = local.remote or manifest.remote or detected.remote or "origin"
    owner = local.owner or manifest.owner or detected.owner
    repo = local.repo or manifest.repo or detected.repo
    url = local.url or manifest.url or detected.url
    if not owner or not repo:
        o2, r2 = parse_github_slug(url)
        owner = owner or o2
        repo = repo or r2
    branch = local.default_branch or manifest.default_branch or detected.default_branch or "main"
    return GithubConfig(
        owner=owner,
        repo=repo,
        remote=remote,
        default_branch=branch,
        url=url,
    )


def save_github_local(root: Path, cfg: GithubConfig) -> None:
    data = load_console_settings()
    by_root = data.get("githubByProject")
    if not isinstance(by_root, dict):
        by_root = {}
    by_root[str(root.resolve())] = cfg.as_dict()
    data["githubByProject"] = by_root
    save_console_settings(data)


def write_github_to_manifest(catalog: ProjectCatalog, cfg: GithubConfig) -> Path:
    path = catalog.root / "ai-project.json"
    doc: dict[str, Any] = {}
    if path.is_file():
        try:
            loaded = json.loads(path.read_text(encoding="utf-8-sig"))
            if isinstance(loaded, dict):
                doc = loaded
        except (OSError, json.JSONDecodeError):
            doc = {}
    doc["github"] = cfg.as_dict()
    if "name" not in doc:
        doc["name"] = catalog.name
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


def apply_remote(catalog: ProjectCatalog, cfg: GithubConfig) -> str:
    """確保 remote URL 與設定一致；回傳結果訊息。"""
    root = catalog.root
    remote = cfg.remote or "origin"
    url = cfg.remote_url()
    if not url:
        return "無法套用：缺少 owner/repo 或 url。"
    code, existing = _git(root, ["remote", "get-url", remote])
    if code == 0 and existing:
        if existing == url:
            return f"remote `{remote}` 已是 {url}"
        c2, err = _git(root, ["remote", "set-url", remote, url])
        return f"已更新 `{remote}` → {url}" if c2 == 0 else f"set-url 失敗：{err}"
    c2, err = _git(root, ["remote", "add", remote, url])
    return f"已新增 `{remote}` → {url}" if c2 == 0 else f"remote add 失敗：{err}"
