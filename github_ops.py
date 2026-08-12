"""GitHub／git 操作：狀態、同步、發布、開啟倉庫、建立 PR。"""

from __future__ import annotations

import shutil
import subprocess
import webbrowser
from pathlib import Path

from .github_config import GithubConfig, apply_remote, resolve_github
from .service_catalog import ProjectCatalog


def _run(root: Path, args: list[str], *, timeout: float = 120) -> tuple[int, str]:
    try:
        result = subprocess.run(
            args,
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        return 1, str(exc)
    out = ((result.stdout or "") + (result.stderr or "")).strip()
    return result.returncode, out


def _git(root: Path, args: list[str], *, timeout: float = 120) -> tuple[int, str]:
    return _run(root, ["git", *args], timeout=timeout)


def is_git_repo(root: Path) -> bool:
    code, _ = _git(root, ["rev-parse", "--is-inside-work-tree"])
    return code == 0


def gh_available() -> bool:
    return shutil.which("gh") is not None


def gh_auth_status(root: Path) -> str:
    if not gh_available():
        return "未安裝 gh CLI"
    code, out = _run(root, ["gh", "auth", "status"], timeout=30)
    return out or ("已登入" if code == 0 else "未登入（請執行 gh auth login）")


def status_report(catalog: ProjectCatalog | None) -> str:
    if catalog is None:
        return "尚未選擇專案。"
    root = catalog.root
    lines = ["GitHub / Git 狀態", ""]
    if not is_git_repo(root):
        lines.append(f"不是 git 倉庫：{root}")
        return "\n".join(lines)

    cfg = resolve_github(catalog)
    code, branch = _git(root, ["rev-parse", "--abbrev-ref", "HEAD"])
    branch = branch if code == 0 else "（未知）"
    code, sha = _git(root, ["rev-parse", "--short", "HEAD"])
    sha = sha if code == 0 else ""
    code, dirty = _git(root, ["status", "--porcelain"])
    dirty_n = len([ln for ln in dirty.splitlines() if ln.strip()]) if code == 0 else -1

    _git(root, ["fetch", "--dry-run"], timeout=30)  # 不強制；下面用本地追蹤資訊
    ahead = behind = "?"
    code, counts = _git(root, ["rev-list", "--left-right", "--count", f"@{{u}}...HEAD"])
    if code == 0 and counts:
        parts = counts.split()
        if len(parts) == 2:
            behind, ahead = parts[0], parts[1]

    lines.extend(
        [
            f"路徑：{root}",
            f"分支：{branch}  ({sha})",
            f"工作區：{'乾淨' if dirty_n == 0 else (f'{dirty_n} 筆未提交變更' if dirty_n >= 0 else '未知')}",
            f"相對 upstream：領先 {ahead}／落後 {behind}",
            "",
            f"remote：{cfg.remote}",
            f"倉庫：{cfg.slug() or '（未設定）'}",
            f"URL：{cfg.remote_url() or '（未設定）'}",
            f"預設分支：{cfg.default_branch}",
            f"設定齊全：{'是' if cfg.is_complete() else '否'}",
            "",
            "gh：" + gh_auth_status(root).splitlines()[0],
        ]
    )
    return "\n".join(lines)


def sync_from_remote(catalog: ProjectCatalog, cfg: GithubConfig | None = None) -> str:
    """fetch + pull --rebase（對目前分支）。"""
    root = catalog.root
    if not is_git_repo(root):
        raise RuntimeError("不是 git 倉庫。")
    cfg = cfg or resolve_github(catalog)
    remote = cfg.remote or "origin"
    lines = [f"同步自 {remote}…"]
    code, out = _git(root, ["fetch", remote, "--prune"], timeout=180)
    lines.append(out or ("fetch OK" if code == 0 else "fetch 失敗"))
    if code != 0:
        raise RuntimeError("\n".join(lines))

    code, branch = _git(root, ["rev-parse", "--abbrev-ref", "HEAD"])
    if code != 0 or not branch or branch == "HEAD":
        raise RuntimeError("無法判斷目前分支（detached HEAD？）。")

    code, out = _git(root, ["pull", "--rebase", remote, branch], timeout=180)
    lines.append(out or ("pull --rebase OK" if code == 0 else "pull 失敗"))
    if code != 0:
        # 嘗試一般 pull
        code2, out2 = _git(root, ["pull", remote, branch], timeout=180)
        lines.append(out2 or ("pull OK" if code2 == 0 else "pull 失敗"))
        if code2 != 0:
            raise RuntimeError("\n".join(lines))
    return "\n".join(lines)


def publish_branch(catalog: ProjectCatalog, cfg: GithubConfig | None = None) -> str:
    """推送目前分支到 remote（-u）。"""
    root = catalog.root
    if not is_git_repo(root):
        raise RuntimeError("不是 git 倉庫。")
    cfg = cfg or resolve_github(catalog)
    if not cfg.is_complete() and not _remote_exists(root, cfg.remote or "origin"):
        raise RuntimeError("尚未設定 GitHub remote。請先完成「GitHub 設定…」。")

    apply_msg = ""
    if cfg.is_complete():
        apply_msg = apply_remote(catalog, cfg)

    remote = cfg.remote or "origin"
    code, branch = _git(root, ["rev-parse", "--abbrev-ref", "HEAD"])
    if code != 0 or not branch or branch == "HEAD":
        raise RuntimeError("無法判斷目前分支。")

    lines = []
    if apply_msg:
        lines.append(apply_msg)
    lines.append(f"發布 {branch} → {remote}…")
    code, out = _git(root, ["push", "-u", remote, branch], timeout=300)
    lines.append(out or ("push OK" if code == 0 else "push 失敗"))
    if code != 0:
        raise RuntimeError("\n".join(lines))
    return "\n".join(lines)


def _remote_exists(root: Path, remote: str) -> bool:
    code, _ = _git(root, ["remote", "get-url", remote])
    return code == 0


def open_on_github(catalog: ProjectCatalog, cfg: GithubConfig | None = None) -> bool:
    cfg = cfg or resolve_github(catalog)
    url = cfg.web_url()
    if not url:
        return False
    code, branch = _git(catalog.root, ["rev-parse", "--abbrev-ref", "HEAD"])
    if code == 0 and branch and branch not in {"HEAD", cfg.default_branch}:
        url = f"{url}/tree/{branch}"
    webbrowser.open(url)
    return True


def create_pull_request(catalog: ProjectCatalog, cfg: GithubConfig | None = None) -> str:
    """使用 gh pr create --fill（需已登入）。"""
    root = catalog.root
    if not gh_available():
        raise RuntimeError("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/")
    cfg = cfg or resolve_github(catalog)
    if cfg.slug():
        repo_args = ["--repo", cfg.slug()]
    else:
        repo_args = []
    base = cfg.default_branch or "main"
    args = ["gh", "pr", "create", *repo_args, "--base", base, "--fill"]
    code, out = _run(root, args, timeout=120)
    if code != 0:
        # 若已有 PR，改為檢視
        code2, out2 = _run(root, ["gh", "pr", "view", "--web", *repo_args], timeout=60)
        if code2 == 0:
            return out2 or "已開啟既有 PR。"
        raise RuntimeError(out or "建立 PR 失敗。")
    return out or "PR 已建立。"


def watch_actions(catalog: ProjectCatalog, cfg: GithubConfig | None = None) -> str:
    """列出最近 workflow runs。"""
    root = catalog.root
    if not gh_available():
        raise RuntimeError("需要 gh CLI。")
    cfg = cfg or resolve_github(catalog)
    args = ["gh", "run", "list", "--limit", "5"]
    if cfg.slug():
        args.extend(["--repo", cfg.slug()])
    code, out = _run(root, args, timeout=60)
    if code != 0:
        raise RuntimeError(out or "無法列出 Actions。")
    return out or "（沒有最近的 workflow runs）"
