# 使用文件

這份文件讓**一般使用者**能自己安裝、開啟專案、啟動服務、編譯、提交，以及在卡住時求救。進階主題（工作區清單、Agent、發版）另外放，不會擋在入門前面。

文件站頂列分成四區：**使用文件**（本頁）、[產品規格](product/README.md)、[銷售套件](salekit/README.md)、API。規格裡的公司平台標「規劃」，尚未出貨。

現行出貨為控制台 **0.6.16**（Windows x64、macOS、Linux）。變更摘要：[現行版本](user/whats-new.md)。倉庫公開可見，授權為**保留一切權利**，見 [版權與授權](legal/copyright.md)。

## 我是誰、該看哪裡

| 角色 | 目標 | 從這裡開始 |
|------|------|------------|
| **一般使用者** | 操作控制台、跑本機堆疊 | [現行版本](user/whats-new.md) · [安裝與第一次使用](user/getting-started.md) |
| **專案經理** | 需求／設計變更、發出任務、驗收 | [需求工作台](user/intake.md) |
| **工作區負責人** | 讓控制台正確掃到多個倉／服務 | [工作區設定（ai-project.json）](workspace/ai-project-json.md) |
| **團隊導入** | 帶新人走完掃描 → 編譯 → 啟動 → 求救 | [90 分鐘導入](team/trial-90min.md) |
| **要用 Agent** | 求救後端、MCP、專案問答 | [Agent 後端](agent/backends.md) |
| **維護此控制台** | 從原始碼編譯、打包、發 Release、簽署 | [開發與維護](maintainer/develop.md) · [程式碼簽署與 Store](maintainer/code-signing.md) |
| **業務／採購** | 對外部軟體公司高層說明定位與管理特色 | [銷售套件](salekit/README.md) |
| **產品／實作對齊** | 軟體公司營運層要做什麼 | [產品規格](product/README.md) · [產品概觀](product/overview.md) |
| **授權／法務** | 公開倉庫、安裝包、第三方元件 | [版權與授權](legal/copyright.md) · [第三方聲明](legal/third-party.md) |

線上版（GitHub Pages）：<https://sjvann.github.io/AI_Project_Console/>

倉庫根目錄的 [README](https://github.com/sjvann/AI_Project_Console/blob/main/README.md) 只保留產品說明與最短路徑；細節都在本目錄。本機預覽見 [文件網站（DocFX）](maintainer/docfx.md)。

## 一般使用者閱讀順序

1. [現行版本](user/whats-new.md) — 目前出貨 0.6.16
2. [安裝與第一次使用](user/getting-started.md) — 下載、啟動、選專案、環境體檢
3. [畫面導覽](user/interface.md) — 每個按鈕與分頁做什麼
4. [日常操作](user/daily-use.md) — 啟動、停止、編譯、看 Log、開前端
5. [GitHub](user/github.md) — clone、提交、同步、發布
6. [卡住時怎麼辦](user/help.md) — UAT 求救、編譯求救、專案問答
7. [設定](user/settings.md) — 外觀、Agent、MCP、問答模型
8. [常見問題](user/troubleshooting.md) — 服務起不來、需重編、更新失敗
9. [專案文件](user/docs.md) — `docs/`、DocFX、本機預覽、GitHub Pages

可選：

- [產品規格](product/README.md) — 買家、六大模組、UI／UX、路線圖（規劃與現況分開標）
- [銷售套件](salekit/README.md) — 一頁紙、投影片、演示腳本、採購 FAQ
- [部署](user/deploy.md) — 本機以外的發佈目標
- [工作區設定](workspace/ai-project-json.md) — 服務清單、產品線與各專案用途
- [MCP](agent/mcp.md) — 讓 Agent 回呼控制台

## 名詞（先看這 8 個就夠用）

| 詞 | 意思 |
|----|------|
| **專案目錄** | 你要管的那份本機資料夾（通常是 git 倉根，或薄工作區） |
| **服務** | 可啟動的程式（API、網站）。「服務」頁籤 |
| **專案（編譯）** | 倉裡偵測到的專案（`.csproj`、`package.json` 等）。名稱旁一句用途；「專案」頁籤看要不要重編 |
| **就緒** | 服務已啟動且健康檢查通過（顯示「線上」） |
| **需重編** | 原始碼比上次編譯結果新，建議先編譯再啟動 |
| **求救** | 把錯誤說明交給你選的 Agent（Cursor 等），不是把程式送到雲端代管 |
| **MCP** | Agent 呼叫控制台的通道（查狀態、編譯、啟停） |
| **ai-project.json** | 可選的工作區清單。沒有也能掃描；多倉或服務對不上時再補 |
