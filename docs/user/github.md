# GitHub

工具列「GitHub」處理遠端倉與本機提交。多數日常動作（看狀態、提交）不需先填設定；同步、發布、Release、PR 需要知道 owner／repo。

## 登入（GitHub 管理的專案必填）

開啟的目錄若已接上 GitHub（`github.com` 遠端，或設定了 owner／repo），控制台會要求用 [GitHub CLI](https://cli.github.com/) 登入。未登入不能繼續使用該專案。

1. 安裝 `gh` 後，按對話框「登入 GitHub」。會開瀏覽器完成授權。
2. 登入後，標題列會顯示 `@帳號`，旁邊有「登出 GitHub」（在「離開」前面）。
3. 左側「任務」分頁列出指派給你的 Issue；未指派清單在下方，可開合，也能「接受」成自己的任務。

已登入時，關閉專案**不會**登出。要換帳號請按「登出 GitHub」。

## 第一次：從遠端開啟

適合電腦上還沒有這份倉的人。

1. 按「從 GitHub 開啟…」或專案列同名按鈕。
2. 倉庫填 `owner/repo` 或 `https://github.com/owner/repo.git`。
3. 本機目錄選父資料夾（例如 `E:\work`），資料夾名稱可留白（用 repo 名）。
4. 分支選填；空白＝遠端預設分支。
5. 「Clone 並開啟」。若目標資料夾已存在且是同一倉庫，會直接開啟、不重 clone。

需要本機已安裝 Git。私人倉請先在終端機確認 `git`／`gh` 能存取。

## GitHub 設定

「GitHub 設定…」填 owner、repo、remote 名稱、預設分支、Remote URL。

- 未設定時，許多動作會先打開這個對話框；也可從既有 `git remote` 預填。
- **存到本機 settings**：只影響你這台電腦。
- **寫入專案 ai-project.json**：跟倉走，團隊可共用。
- 勾選「儲存後立即套用到 git remote」會改本機 remote。

`ai-project.json` 片段：

```json
"github": {
  "owner": "my-org",
  "repo": "my-repo",
  "remote": "origin",
  "defaultBranch": "main",
  "url": ""
}
```

## 日常：狀態、提交、同步、發布

| 動作 | 說明 |
|------|------|
| **狀態** | 分支、髒工作區、領先／落後、`gh` 是否登入 |
| **提交…** | 列出未提交檔、填說明後 `git add -A` + `git commit`。可勾選提交後 push。說明可按「AI 建議」（有 [Cursor Agent CLI](https://cursor.com/docs/cli/overview) 時用 Agent，否則依 diff 起草） |
| **同步（pull）** | `git fetch` + `pull --rebase`。會先確認 |
| **發布（push）** | `git push -u` 目前分支。會先確認 |

摘要列有「未提交」時也可直接按「提交…」。

同步用 rebase。若出現衝突，到終端機或 IDE 解完再回控制台看狀態。

## Release 與 PR

這些需要 [GitHub CLI](https://cli.github.com/) 已登入（`gh auth login`）。

| 動作 | 說明 |
|------|------|
| **發行 Release…** | SemVer 版號（可 Major／Minor／Patch 遞增）、標題、說明；可草稿、預發行、標 Latest、附加檔案 |
| **Release 列表** | `gh release list` |
| **開啟 Releases** | 瀏覽器開 GitHub Releases |
| **開啟倉庫** | 瀏覽器開倉庫首頁 |
| **建立／開啟 PR** | 已有 PR 則開啟，否則走建立流程 |
| **Actions 最近執行** | `gh run list` |
| **檢查控制台更新** | 查**這個控制台**有沒有新版，不需先設定專案 GitHub |

發行此控制台自己的安裝包，是維護者流程，見 [scripts/README.md](../../scripts/README.md)。
