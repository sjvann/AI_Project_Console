---
_layout: landing
---

# AI_Project 控制台

本機桌面程式：選一個專案目錄，掃描服務、編譯、一鍵啟動。卡住時可把說明交給本機 Agent。

**一般使用者請從安裝包開始，不必編譯原始碼。**

線上文件：[使用文件](docs/README.md) · 安裝包：[Releases](https://github.com/sjvann/AI_Project_Console/releases)

## 三分鐘開始

1. 從 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 下載 `*-win-x64-setup.exe`（或 zip）。Windows 10／11 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)（通常已內建）。
2. 開啟控制台 → **選擇專案目錄…**（或「從 GitHub 開啟…」）。
3. 有「需重編」先 **建置 → 編譯過期項目**，再按 **啟動**，然後 **開啟前端**。

[安裝與第一次使用](docs/user/getting-started.md) · [畫面導覽](docs/user/interface.md) · [日常操作](docs/user/daily-use.md) · [常見問題](docs/user/troubleshooting.md)

## 依角色閱讀

| 你想做的事 | 文件 |
|------------|------|
| 安裝、開專案、啟動、編譯、看 Log | [一般使用者](docs/README.md) |
| 服務掃不到、多個倉要一起管 | [工作區設定](docs/workspace/ai-project-json.md) |
| 提交、同步、Release | [GitHub](docs/user/github.md) |
| 求救、專案問答 | [卡住時怎麼辦](docs/user/help.md) |
| 讓 Agent 回呼控制台 | [MCP](docs/agent/mcp.md) |
| 帶團隊走一遍 | [90 分鐘導入](docs/team/trial-90min.md) |
| 改控制台原始碼、打包 | [開發與維護](docs/maintainer/develop.md) |
| 查公開型別 | [API 參考](api/index.md) |

關閉控制台**不會**停止已啟動的服務。請用「停止全部」。
