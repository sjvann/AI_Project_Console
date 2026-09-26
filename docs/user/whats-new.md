---
title: 現行版本
---

# 現行版本

文件與安裝包以 **0.6.19** 為準（Windows x64、macOS、Linux）。標 **規劃** 的仲介與公司工作區尚未出貨。

## 這版有什麼（0.6.19）

- 開啟含很多專案的工作區時，沒有一句用途就留空，不再讀 README，視窗不會因此沒有回應
- 文件 front matter 沒有 title 時，開啟專案不再停住；掃描改在背景進行
- 需求工作台改為開 GitHub Issue：必填標題與說明，可選指派與截圖；不再分需求／設計變更
- 發出後可暫停、收回、驗收結案；審查與合併仍走開發台與 GitHub
- 「開啟」與「開啟前端」會先起自己與 `dependsOn`，就緒後再開瀏覽器；硬相依失敗則不開 URL
- 掃描時每個專案都要有一句用途；開啟或重掃會寫入 `.ai_project/product-purposes.md`
- 建置／啟動進行中會顯示佔用列（進度、目前項目、可取消）；兩個視窗不會搶同一個工作區
- 設定可加入多家申報公司，工時儀表板一次送到選定那一家
- 專案問答：多個 OpenAI 相容來源（本機 Ollama／LM Studio，或 OpenAI／Groq／OpenRouter），可記住多組端點；只呼叫唯讀堆疊工具
- MCP 值班工具與審計（政策擋下不算警報）
- GitHub Pulse／操作台、檢查更新
- Windows 安裝包；macOS `.app` zip 與 Linux zip／`.deb`（Mac 尚未 Apple 公證，第一次請右鍵打開）
- 工時儀表、DocFX／GitHub Pages 輔助

下載：[Releases](https://github.com/sjvann/AI_Project_Console/releases)。授權：[版權與授權](../legal/copyright.md)。路線圖：[路線圖](../product/roadmap.md)。
