# AI_Project 控制台

本機桌面程式：選一個專案目錄，掃描服務、編譯、一鍵啟動，並支援 GitHub 與部署設定。卡住時可把說明交給本機 Agent（預設 [Cursor](https://cursor.com/)）。

**一般使用者請從安裝包開始，不必編譯原始碼。**

完整操作說明：[使用文件](https://sjvann.github.io/AI_Project_Console/)（倉庫內原稿：[docs/](docs/README.md)）。給買家：[產品規格](docs/product/README.md) · [銷售套件](docs/salekit/README.md)（公司營運層標為規劃，尚未出貨）。

現行出貨：**0.6.13**（Windows x64）。[現行版本](docs/user/whats-new.md) · [版權與授權](docs/legal/copyright.md)（公開可見，**保留一切權利**，不是開源授權）。

## 三分鐘開始

1. 從 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 下載 `*-win-x64-setup.exe`（或 zip 免安裝包）。Windows 10／11 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)（通常已內建）。
2. 開啟控制台 → **選擇專案目錄…**（或「從 GitHub 開啟…」）。
3. 有「需重編」先 **建置 → 編譯過期項目**，再按 **啟動**，然後 **開啟前端**。

下一步：[安裝與第一次使用](docs/user/getting-started.md) · [畫面導覽](docs/user/interface.md) · [日常操作](docs/user/daily-use.md) · [常見問題](docs/user/troubleshooting.md)

## 這是給誰用的

| 你想做的事 | 文件 |
|------------|------|
| 安裝、開專案、啟動、編譯、看 Log | [一般使用者](docs/README.md#一般使用者閱讀順序) |
| 服務掃不到、多個倉要一起管 | [工作區設定（ai-project.json）](docs/workspace/ai-project-json.md) |
| 提交、同步、Release | [GitHub](docs/user/github.md) |
| 求救、專案問答 | [卡住時怎麼辦](docs/user/help.md) |
| 讓 Agent 回呼控制台 | [MCP](docs/agent/mcp.md) |
| 帶團隊走一遍 | [90 分鐘導入](docs/team/trial-90min.md) |
| 對軟體公司說明現況與規劃 | [銷售套件](docs/salekit/README.md) · [產品規格](docs/product/README.md) |
| 改控制台原始碼、打包 | [開發與維護](docs/maintainer/develop.md) |

關閉控制台**不會**停止已啟動的服務。請用「停止全部」。

## 從原始碼執行（維護者）

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet run --project src/AiProject.Console.App
```

第二個視窗（第一個還在跑時不要再編譯）：

```powershell
dotnet run --no-build --project src/AiProject.Console.App
```

Windows 安裝包與 GitHub Release：[scripts/README.md](scripts/README.md)。方案結構與測試：[開發與維護](docs/maintainer/develop.md)。

倉庫根目錄的 Python／Tk 實作僅供對照，不是正式產品路徑。

## 授權

Copyright © 2026 sjvann。**保留一切權利。** 公開本倉庫是為了文件與評估，不授權他人把原始碼當產品再發布。安裝包使用範圍與禁止事項見 [LICENSE](LICENSE)；第三方元件見 [NOTICE](NOTICE)。安全回報見 [SECURITY.md](SECURITY.md)。貢獻政策見 [CONTRIBUTING.md](CONTRIBUTING.md)。
