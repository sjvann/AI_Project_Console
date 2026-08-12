# AI_Project 控制台

通用本機堆疊控制台：選專案目錄 → 掃描服務 → 啟動／建置；並支援 **GitHub**（設定／同步／發布）與 **GCP** 部署設定。

現有 AI_House 控制台（`Scripts/console*`）維持不動。

## 啟動

```powershell
python -m AI_Project_Console
```

## Cursor

需本機已安裝 [Cursor](https://cursor.com/)，且 `cursor` 在 PATH（安裝程式通常會帶 CLI）。目前走本機 Cursor（開 IDE + 剪貼簿），不使用雲端 Agent API Key。

| 時機 | 行為 |
|------|------|
| 選擇專案目錄 | 啟動時不帶預設專案；可從「歷史專案」下拉選取，或按「選擇專案目錄…」。若勾選「同時開啟 Cursor」，載入後以 `--reuse-window` 開啟 |
| UAT 求救 | 按鈕 → 填說明／貼截圖 → 寫入 `.ai_project/uat-reports/`、開 Cursor、提示進剪貼簿 |
| 建置失敗 | 「建置輸出」啟用「編譯求救」→ 寫入 `.ai_project/build-reports/cursor-help.md`、開 Cursor、提示進剪貼簿 |

環境體檢會檢查 Cursor CLI。

## GitHub

工具列「GitHub ▾」：

| 動作 | 說明 |
|------|------|
| GitHub 設定… | owner／repo／remote／預設分支；可寫本機或 `ai-project.json`，並套用 git remote |
| 狀態 | 分支、髒工作區、領先／落後、gh 登入 |
| 同步（pull） | `git fetch` + `pull --rebase` |
| 發布（push） | `git push -u` 目前分支 |
| 開啟倉庫 | 瀏覽器開啟 GitHub |
| 建立／開啟 PR | 需 [GitHub CLI](https://cli.github.com/)（`gh`） |
| Actions 最近執行 | `gh run list` |

未設定時會先開設定對話框；也可從既有 `git remote` 自動預填。

`ai-project.json` 可含：

```json
"github": {
  "owner": "my-org",
  "repo": "my-repo",
  "remote": "origin",
  "defaultBranch": "main",
  "url": ""
}
```

## GCP

「GCP 部署 ▾」需先設定 `projectId`／`zone`／`instance`。不硬綁 AI_House infra。

## ai-project.json

可選。完整範例：[`schema/ai-project.example.json`](schema/ai-project.example.json)。

```powershell
Copy-Item AI_Project_Console\schema\ai-project.example.json .\ai-project.json
```

## 執行期

選定專案根下 `.ai_project/{logs,pids,build-reports,uat-reports}`。  
控制台設定：`%LOCALAPPDATA%\AI_Project_Console\settings.json`。
