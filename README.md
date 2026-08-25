# AI_Project 控制台

通用本機堆疊控制台：選專案目錄 → 掃描服務 → 啟動／建置；並支援 **GitHub**（設定／同步／發布）與 **部署**（GCP／自家機房／Azure／不下發）。

目前主程式為 **C# + Photino.Blazor**（跨平台桌面視窗）。Python／Tk 版本仍保留於倉庫根目錄，作為對照實作。

## 啟動（C#）

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)（或更新，可編譯 net8.0）。Windows 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)（Win10／11 通常已內建）。

```powershell
dotnet run --project src/AiProject.Console.App
```

### 安裝包（Windows x64）

從 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 下載：

- `AI_Project_Console-*-win-x64-setup.exe`：安裝程式（開始選單捷徑，可選桌面捷徑）
- `AI_Project_Console-*-win-x64.zip`：免安裝壓縮包

本機打包（需 [Inno Setup 6](https://jrsoftware.org/isinfo.php)）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-win.ps1
```

升版、打包、推送、建立 GitHub Release 的完整指令見 [`scripts/README.md`](scripts/README.md)。

發布單檔執行檔：

```powershell
dotnet publish src/AiProject.Console.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

macOS / Linux 將 `-r` 改為 `osx-arm64` 或 `linux-x64`。

### 檢查更新

啟動約 1.5 秒後會查詢本倉庫 [GitHub Releases](https://github.com/sjvann/AI_Project_Console/releases) 的 latest。若有比目前 `AppInfo.Version` 更新的版本，標題列下方會出現橫幅。

- **Inno 安裝版**（資料夾內有 `unins*.exe`）：下載 `*-win-x64-setup.exe`，靜默安裝到目前目錄後關閉控制台，安裝完成後自動重開。
- **zip 免安裝版**：下載 zip，等程式結束後覆蓋檔案並重開。
- **從原始碼執行**（`dotnet run` / `bin\Debug`）：無法直接覆蓋開發目錄；請 `git pull` 後重新編譯，或改用 Releases 安裝包。

私人倉庫需 [GitHub CLI](https://cli.github.com/) 已登入（`gh auth login`），否則匿名 API 會 404。也可設定環境變數 `GH_TOKEN`。

也可按標題列「檢查更新」，或 GitHub 選單「檢查控制台更新」。按「稍後再說」會略過該版號，直到出現更新的 tag。背景檢查最多每 6 小時一次。

## 啟動（Python，舊版）

```powershell
python -m AI_Project_Console
```

## Agent 後端

標題列「設定」可自訂求救後端。控制台負責組提示；後端只負責開啟工作區／Agent。預設仍是本機 [Cursor](https://cursor.com/)（CLI + New Agent deeplink），不使用雲端 API Key。

| 後端 | 類型 | 求救方式 |
|------|------|----------|
| Cursor | 本機 IDE | 開專案 + `cursor://` deeplink |
| Claude Code | 本機 CLI | `claude-cli://open?cwd=&q=` |
| Aider | 本機 CLI | 新終端機 `aider --message`／`--message-file` |
| OpenAI Codex CLI | 本機 CLI | 新終端機 `codex`／`codex exec` |
| VS Code / GitHub Copilot | 本機 IDE | 開資料夾 + 複製提示（請在 Copilot Chat 貼上） |
| Windsurf / Devin Desktop | 本機 IDE | 開資料夾 + 複製提示 |
| 自訂命令 | 本機 | 範本變數 `{root}`、`{promptFile}`、`{prompt}` |

| 時機 | 行為 |
|------|------|
| 選擇專案目錄 | 啟動時不帶預設專案；可從「歷史專案」下拉選取，或按「選擇專案目錄…」。若勾選「同時開啟 {後端}」，載入後開啟該後端的工作區 |
| UAT 求救 | 按鈕 → 填說明／貼截圖 → 確認後交給目前後端（說明直接帶入提示） |
| 建置失敗 | 「建置輸出」啟用「編譯求救」→ 預覽錯誤 → 確認後帶入錯誤內容（不寫求助檔） |

環境體檢會列出目前後端是否可用，以及其他已安裝後端。90 分鐘導入劇本見 [`docs/trial-90min.md`](docs/trial-90min.md)。

### MCP（Agent 回呼控制台）

控制台提供 stdio MCP 伺服器 `AiProject.Console.Mcp`：Agent 可 `stack_status`、`build`、`get_log`、啟停服務。設定裡可複製／寫入 `.cursor/mcp.json`，並可設白名單、唯讀、`stop_all` 確認與審計 log。說明見 [`docs/mcp.md`](docs/mcp.md)。

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root . --invoke stack_status
```

## GitHub

工具列「GitHub」：

| 動作 | 說明 |
|------|------|
| 從遠端開啟… | 輸入 `owner/repo` 或 URL，clone 到本機後當作專案開啟 |
| GitHub 設定… | owner／repo／remote／預設分支；可寫本機或 `ai-project.json`，並套用 git remote |
| 狀態 | 分支、髒工作區、領先／落後、gh 登入 |
| 提交… | 檢視未提交檔案、填寫說明後 `git add -A` + `git commit`；可選擇提交後 push。說明可按「AI 建議」（有 [Cursor Agent CLI](https://cursor.com/docs/cli/overview) 時用 Agent，否則依 diff 產生草稿）。不需先完成 GitHub 設定 |
| 同步（pull） | `git fetch` + `pull --rebase` |
| 發布（push） | `git push -u` 目前分支 |
| 發行 Release… | 填版號（SemVer，可 Major／Minor／Patch 遞增）、標題、說明；可草稿／預發行、附加檔案。需 [GitHub CLI](https://cli.github.com/)（`gh`） |
| Release 列表 | `gh release list` |
| 開啟 Releases | 瀏覽器開啟 GitHub Releases 頁 |
| 開啟倉庫 | 瀏覽器開啟 GitHub |
| 建立／開啟 PR | 需 [GitHub CLI](https://cli.github.com/)（`gh`） |
| Actions 最近執行 | `gh run list` |
| 檢查控制台更新 | 查詢本控制台 GitHub Releases 是否有新版本（不需先設定專案 GitHub） |

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

## 部署

工具列「部署」→「部署設定…」先用下拉選發佈目標，再填該目標的屬性：

| 目標 | 主要屬性 |
|------|----------|
| 不下發（僅本機） | 不需遠端欄位 |
| Google Cloud (GCP) | `projectId`／`zone`／`instance`，可選 host、workflow |
| 自家機房／遠端伺服器 | 協定（SSH／WinRM／IIS／SMB）、主機、連接埠、帳號、遠端路徑 |
| Microsoft Azure | `subscriptionId`／`resourceGroup`／`appName` |

既有 `ai-project.json` 的 `gcp` 區塊仍可讀寫；完整範例見 schema。

## ai-project.json

可選。完整範例：[`schema/ai-project.example.json`](schema/ai-project.example.json)。

```powershell
Copy-Item schema/ai-project.example.json .\ai-project.json
```

薄工作區（目錄本身沒有 `.csproj`）請加 `productLines`，控制台才會去掃隔壁產品線，並在「專案」頁依線分組：

```json
"productLines": [
  { "id": "ledger", "label": "Ledger", "root": "../FHIR-ProfileServer" },
  { "id": "weave", "label": "Weave", "root": "../AI_EZIE" }
]
```

未寫 `productLines` 時，若 `services[].project` 指向 `../某倉/...`，會推斷該倉為一條產品線。

服務可選 `preStart`（或 `ensure`）：相對專案根的腳本。按「啟動」或「啟動全部」時，**先跑該服務自己的前置**，成功才 `dotnet run`。失敗則不啟動行程，訊息寫進該服務 Log。各服務前置彼此獨立，禁止用這一欄去起另一條產品線。

## 執行期

選定專案根下 `.ai_project/{logs,pids,build-reports,uat-reports}`。  
控制台設定：`%LOCALAPPDATA%\AI_Project_Console\settings.json`。

## 方案結構

```
AiProject.Console.slnx
  src/AiProject.Console.Core   掃描、服務目錄、行程、建置、GitHub、部署、Cursor
  src/AiProject.Console.App    Photino 視窗 + Blazor UI
  tests/AiProject.Console.Core.Tests
```
