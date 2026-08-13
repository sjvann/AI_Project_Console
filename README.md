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

發布單檔執行檔：

```powershell
dotnet publish src/AiProject.Console.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

macOS / Linux 將 `-r` 改為 `osx-arm64` 或 `linux-x64`。

## 啟動（Python，舊版）

```powershell
python -m AI_Project_Console
```

## Cursor

需本機已安裝 [Cursor](https://cursor.com/)，且 `cursor` 在 PATH（安裝程式通常會帶 CLI）。目前走本機 Cursor（開 IDE + New Agent deeplink），不使用雲端 Agent API Key。

| 時機 | 行為 |
|------|------|
| 選擇專案目錄 | 啟動時不帶預設專案；可從「歷史專案」下拉選取，或按「選擇專案目錄…」。若勾選「同時開啟 Cursor」，載入後以 `--reuse-window` 開啟 |
| UAT 求救 | 按鈕 → 填說明／貼截圖 → 確認後開啟 Cursor 跳出視窗，再確認即建立 New Agent（說明直接帶入提示） |
| 建置失敗 | 「建置輸出」啟用「編譯求救」→ 預覽錯誤 → 確認後同樣以 New Agent 帶入錯誤內容（不寫求助檔） |

環境體檢會檢查 Cursor CLI。

## GitHub

工具列「GitHub」：

| 動作 | 說明 |
|------|------|
| 從遠端開啟… | 輸入 `owner/repo` 或 URL，clone 到本機後當作專案開啟 |
| GitHub 設定… | owner／repo／remote／預設分支；可寫本機或 `ai-project.json`，並套用 git remote |
| 狀態 | 分支、髒工作區、領先／落後、gh 登入 |
| 同步（pull） | `git fetch` + `pull --rebase` |
| 發布（push） | `git push -u` 目前分支 |
| 發行 Release… | 填版號（SemVer，可 Major／Minor／Patch 遞增）、標題、說明；可草稿／預發行、附加檔案。需 [GitHub CLI](https://cli.github.com/)（`gh`） |
| Release 列表 | `gh release list` |
| 開啟 Releases | 瀏覽器開啟 GitHub Releases 頁 |
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
