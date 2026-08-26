# 文件網站（DocFX）

使用文件與 API 參考由 [DocFX](https://dotnet.github.io/docfx/) 建成靜態站，發佈到 GitHub Pages：

**https://sjvann.github.io/AI_Project_Console/**

## 本機預覽

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。在倉庫根目錄：

```powershell
dotnet tool restore
dotnet docfx docfx.json --serve
```

瀏覽器開啟提示的位址（預設 `http://localhost:8080`）。改 markdown 後再執行一次 `dotnet docfx docfx.json`（或關掉後重新 `--serve`）。

只建置、不開伺服器：

```powershell
dotnet docfx docfx.json
```

輸出在 `_site/`（已列入 `.gitignore`）。API YAML 寫在 `api/*.yml`，同樣不提交。

## 設定檔

| 檔案 | 用途 |
|------|------|
| `docfx.json` | 要掃的專案、markdown、範本、站名 |
| `toc.yml` | 頂列：使用文件／API |
| `docs/toc.yml` | 使用文件側欄 |
| `index.md` | 網站首頁 |
| `templates/public/` | 自訂樣式與頂列 GitHub 圖示 |
| `.config/dotnet-tools.json` | 鎖定 DocFX 2.78.5 |

API 來自 `AiProject.Console.Core` 與 `AiProject.Console.Mcp`（不含桌面 UI 專案）。兩個專案都已開 `GenerateDocumentationFile`，有 XML 註解的公開成員會出現在 API 頁。

## 發佈

推送到 `main` 後，[`.github/workflows/docs.yml`](https://github.com/sjvann/AI_Project_Console/blob/main/.github/workflows/docs.yml) 會建置並部署。倉庫 **Settings → Pages → Source** 須為 **GitHub Actions**。也可在 Actions 手動跑「Deploy docs」。

Pull request 只建置、不發佈，用來確認文件沒破。
