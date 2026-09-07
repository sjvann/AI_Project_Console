# Windows 安裝包與 GitHub Release

這份是**控制台維護者**的發版清單。一般使用者請看 [使用文件](https://sjvann.github.io/AI_Project_Console/)（原稿 [docs/](../docs/README.md)），從 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 安裝即可。摘要見 [docs/maintainer/release.md](../docs/maintainer/release.md)。

發佈新版本時依序做：改版號 → 打包 → 推送 → 建立 Release。已安裝舊版的使用者會靠 GitHub Releases 的 latest 做「檢查更新」，因此**不可覆寫舊 tag**，必須升版。

目前程式版號見 `src/AiProject.Console.Core/AppInfo.cs` 的 `Version`（例如 `0.3.7`）。下列指令以 `0.3.8` 為例，請改成實際新版號。

## 前置需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（`ISCC.exe`，用於 `*-setup.exe`）
- [GitHub CLI](https://cli.github.com/) 已登入：`gh auth login`
- （可選）Azure Artifact Signing：`.NET 8 Runtime`、`az login`、`installer/windows/trusted-signing.json`。見 [程式碼簽署](../docs/maintainer/code-signing.md)
- 在倉庫根目錄執行下列指令

```powershell
cd E:\sjvann\AI_Project_Console
```

## 1. 改版號

請同步改這幾處（數字要一致）：

| 檔案 | 欄位 |
|------|------|
| `src/AiProject.Console.Core/AppInfo.cs` | `Version` |
| `src/AiProject.Console.App/AiProject.Console.App.csproj` | `Version`、`InformationalVersion` |
| `installer/windows/setup.iss` | `MyAppVersion` 預設值 |
| `scripts/pack-win.ps1` | `$Version` 預設值 |

## 2. 提交並推送

```powershell
git add -A
git commit -m "Bump version to 0.3.8."
git push
```

先推 `main`，再建立 Release；控制台從原始碼執行時也會提示用 `git pull`。

## 3. 打包 Windows x64

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-win.ps1 -Version 0.3.8
```

成功後 `dist\` 會有：

- `AI_Project_Console-0.3.8-win-x64-setup.exe`：安裝程式（開始選單捷徑，可選桌面捷徑）
- `AI_Project_Console-0.3.8-win-x64.zip`：免安裝壓縮包，解壓後執行 `AI_Project_Console.exe`

已設定 Azure Artifact Signing 時，主程式與 setup.exe 會一併簽署。正式發行請加 `-RequireSign`；本機試包可加 `-SkipSign`。步驟見 [程式碼簽署](../docs/maintainer/code-signing.md)。

只產生單檔執行檔（不經 Inno Setup）可用：

```powershell
dotnet publish src/AiProject.Console.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

macOS / Linux 將 `-r` 改為 `osx-arm64` 或 `linux-x64`。

也可在控制台 GitHub 操作台按「發行 Release…」：會代跑本節打包，並把兩個檔附上 Release。沒有 `*-win-x64-setup.exe` 時，已安裝使用者按「立即更新」只能開 GitHub 頁，不會啟動安裝程式。

## 4. 建立 GitHub Release

```powershell
gh release create v0.3.8 --title "v0.3.8 AI_Project 控制台" --notes @"
## AI_Project 控制台 0.3.8

<在此寫本版說明>

### 下載

- **AI_Project_Console-0.3.8-win-x64-setup.exe**：安裝程式（開始選單捷徑，可選桌面捷徑；安裝到目前使用者的 Local AppData）
- **AI_Project_Console-0.3.8-win-x64.zip**：免安裝壓縮包，解壓後執行 ``AI_Project_Console.exe``

Windows 10/11 需已安裝 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)（系統通常已內建）。
"@ dist\AI_Project_Console-0.3.8-win-x64-setup.exe dist\AI_Project_Console-0.3.8-win-x64.zip
```

完成後網址：

`https://github.com/sjvann/AI_Project_Console/releases/tag/v0.3.8`

查看、列出：

```powershell
gh release list
gh release view v0.3.8
```

## 重新產生圖示（可選）

logo 原始檔在 `assets/brand/`。若改了主圖，可再產生 `.ico` / favicon：

```powershell
python scripts/make-icons.py
```

需要 [Pillow](https://pypi.org/project/Pillow/)。圖示會寫入：

- `src/AiProject.Console.App/Assets/app.ico`（執行檔、工作列、安裝程式）
- `src/AiProject.Console.App/wwwroot/favicon.ico`
- `src/AiProject.Console.App/wwwroot/img/logo.svg`

工作列圖示依賴穩定的 `AppInfo.AppUserModelId`（`sjvann.AIProjectConsole`），不要隨便改，否則 Windows 11 可能再快取成預設圖。
