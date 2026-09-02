# GitHub

GitHub 入口是 **Pulse 狀態列**（專案列下方）與 **操作台 Hub**（抽屜）。開發與需求兩個工作台都能用。多數日常動作（看狀態、提交）不需先填設定；同步、發布、Release、PR 需要知道 owner／repo。

Pulse 只顯示**一個**下一步（未提交 → 提交；落後 → 同步；超前／無追蹤 → 發布；功能分支尚無 PR → 建立 PR；CI 進行中或剛失敗 → Actions）。其餘動作按「日常／協作／發行／接入」分組，在 Hub 裡。快捷鍵 **Ctrl+G** 開／關操作台。

檢查此控制台有沒有新版，請用標題列「檢查更新」，不在 GitHub 操作台裡。

## 登入（GitHub 管理的專案必填）

開啟的目錄若已接上遠端 Git 主機（預設 `github.com`，或設定／remote 上的企業主機），控制台會要求用 [GitHub CLI](https://cli.github.com/) 對**該主機**登入。未登入不能繼續使用該專案。

1. 安裝 `gh` 後，按對話框「登入」。可改認證主機（例如 `ghe.corp.com`）。會開瀏覽器完成授權。
2. 登入後，標題列徽章顯示 `@帳號`（點一下開操作台）；非公開主機時加上主機名稱。登出在操作台頂欄。
3. 左側「任務」分頁列出指派給你的 Issue；未指派清單在下方，可開合，也能「接受」成自己的任務。操作台「遠端協作」也有「我的任務」捷徑。

已登入時，關閉專案**不會**登出。要換帳號請開操作台按「登出」。

## 第一次：從遠端開啟

適合電腦上還沒有這份倉的人。

1. 按專案列「從 GitHub 開啟…」，或操作台「接入」裡的同名項目。
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
  "host": "github.com",
  "kind": "github",
  "remote": "origin",
  "defaultBranch": "main",
  "url": ""
}
```

## 日常：分支、狀態、提交、同步、發布

開啟 git 專案時，**第一件事**會自動從遠端 `fetch` + `pull --rebase` 目前分支。工作區不乾淨則略過並警告，避免覆蓋本機改動。

專案列與 Pulse 都會顯示**目前分支**（點一下可切換）。無法離開時 Pulse 會亮黃框，主按鈕對準阻擋原因（提交或發布）。

| 動作 | 說明 |
|------|------|
| **切換分支…** | 列出本機與遠端分支。工作區必須乾淨才能切換；切換後立刻同步該分支。也可建立新分支 |
| **狀態** | 操作台底列「狀態報告」：分支、髒工作區、領先／落後、`gh` 是否登入 |
| **提交…** | 列出未提交檔、填說明後 `git add -A` + `git commit`。可勾選提交後 push。說明可按「AI 建議」（有 [Cursor Agent CLI](https://cursor.com/docs/cli/overview) 時用 Agent，否則依 diff 起草） |
| **同步（pull）** | `git fetch` + `pull --rebase`。會先確認 |
| **發布（push）** | `git push -u` 目前分支。會先確認。推送後 Pulse 監看 CI；若開啟「發布前先跑本機測試」則測試沒過就不推 |

Pulse 有未提交時會直接出現「提交…」。

### 離開前必須乾淨

關閉專案、切換專案、按「離開」，只要工作區**不乾淨**或還有**未發布提交**（含尚未追蹤遠端的新分支），就**不能離開**。請先提交並發布，讓本機與遠端一致。這是為了避免本機偷懶留下與遠端不同的版本。

同步用 rebase。若出現衝突，到終端機或 IDE 解完再回控制台看狀態。

## Release 與 PR

這些需要 [GitHub CLI](https://cli.github.com/) 已登入（`gh auth login`）。

| 動作 | 說明 |
|------|------|
| **發行 Release…** | SemVer 版號（可 Major／Minor／Patch 遞增）、標題、說明；可草稿、預發行、標 Latest、附加檔案 |
| **Release 列表** | `gh release list` |
| **開啟 Releases** | 瀏覽器開 GitHub Releases |
| **開啟倉庫** | 瀏覽器開倉庫首頁 |
| **建立／開啟 PR** | 已有 PR 則看檢查摘要（能不能請人審）。沒有則用提交說明建立。行內討論仍開遠端網頁；需求工作台可請人審、並在 CI 綠後合併 |
| **補齊 CI workflow…** | 若沒有建置／測試 workflow，寫入 `.github/workflows/ci.yml` 骨架。既有檔不覆蓋。請審查後再提交；控制台不代跑 |
| **Actions 狀態…** | 最近幾次遠端執行的通過／失敗／進行中。點一列或「開啟最新」到 GitHub 看 log。控制台不能修 CI，也不取代 Actions 畫面 |

這些項目在操作台「遠端協作」與「發行與 CI」。推送（含「提交後再 push」）之後，Pulse 會監看這次 CI。目前分支若有 PR（或不是預設分支），Pulse 也會顯示檢查是否通過。設定裡可勾「發布前先跑本機測試」。

發行此控制台自己的安裝包，是維護者流程，見 [scripts/README.md](../../scripts/README.md)。
