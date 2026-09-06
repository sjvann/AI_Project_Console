# 開發與維護

這頁給要改 **AI_Project 控制台本身** 的人。操作本機堆疊請回到 [使用文件](../README.md)。

目前主程式為 **C# + Photino.Blazor**（跨平台桌面視窗）。倉庫根目錄仍保留 Python／Tk 對照實作，一般使用者與日常開發請用 C#。

## 從原始碼執行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)（可編譯 net10.0）。Windows 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)。

```powershell
dotnet run --project src/AiProject.Console.App
```

Debug 建置不產生 `AI_Project_Console.exe`（改由 `dotnet.exe` 載入 DLL）。Windows 若擋未簽名的 apphost，會出現「存取被拒」；這是為了避開該限制。Release／安裝包仍用 exe。

發布單檔執行檔：

```powershell
dotnet publish src/AiProject.Console.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

macOS／Linux 將 `-r` 改為 `osx-arm64` 或 `linux-x64`。此路徑不會出現在 GitHub Releases 的 Windows 安裝包流程裡。

### Python 對照（舊版）

```powershell
python -m AI_Project_Console
```

不要把它當成正式產品路徑。

## 方案結構

```
AiProject.Console.slnx
  src/AiProject.Console.Core   掃描、服務目錄、行程、建置、GitHub、部署、Agent
  src/AiProject.Console.App    Photino 視窗 + Blazor UI
  src/AiProject.Console.Mcp    stdio MCP 伺服器
  src/AiProject.Console.CompanyClient  公司平台 HTTPS 用戶端
  src/AiProject.Company.*      公司平台（Domain／Application／Infrastructure／Contracts／Web）
  tests/...
```

動作目錄：`src/AiProject.Console.Core/Actions/actions.json`。版號來源：`src/AiProject.Console.Core/AppInfo.cs` 的 `Version`。

MCP 本機除錯：

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root . --invoke stack_status
```

說明見 [MCP](../agent/mcp.md)。

## 測試

```powershell
dotnet test AiProject.Console.slnx
```

推到 `main` 或開 PR 時，[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) 會 restore、Release 建置並跑同一組測試。文件站仍由 [`.github/workflows/docs.yml`](../../.github/workflows/docs.yml) 處理（只在文件相關路徑變更時）。

## 文件網站

使用文件、產品規格、銷售套件與 API 參考用 DocFX 建置，發佈於 <https://sjvann.github.io/AI_Project_Console/>。本機預覽與設定見 [文件網站（DocFX）](docfx.md)。

公司平台（人員、派工、戰情室等）施工以 [系統執行計劃書](../product/execution-plan.md) 與 [自架說明](../product/deploy-company.md) 為準。本機：

```powershell
dotnet run --project src/AiProject.Company.Web
```


## 打包與 Release

Windows 安裝包、升版檢查清單、GitHub Release 指令：[scripts/README.md](../../scripts/README.md) 與 [發版](release.md)。

圖示原始檔在 `assets/brand/`。改主圖後：

```powershell
python scripts/make-icons.py
```

需要 [Pillow](https://pypi.org/project/Pillow/)。工作列圖示依賴穩定的 `AppInfo.AppUserModelId`（`sjvann.AIProjectConsole`），不要隨便改。

## 設定與執行期（開發時）

| 路徑 | 內容 |
|------|------|
| `%LOCALAPPDATA%\AI_Project_Console\settings.json` | 使用者偏好、歷史專案 |
| 被開啟專案下的 `.ai_project/` | logs、pids、編譯報告、審計 |

從原始碼執行時，「檢查更新 → 立即更新」無法覆蓋開發目錄；請 `git pull` 後重編，或改用 Releases 安裝包驗證更新流程。
