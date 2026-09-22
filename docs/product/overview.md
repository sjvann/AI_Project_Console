---
title: 產品概觀
---

# 產品概觀

這頁對齊**目前出貨**（控制台 **0.6.16**）與規格書裡的**規劃**。一般使用者請從 [安裝與第一次使用](../user/getting-started.md) 開始。買家五分鐘： [一頁紙](../salekit/one-pager.md)。

## 現況（已出貨）

Windows x64 本機桌面：**AI_Project 控制台**（P1）。給軟體工程師面對不同公司、不同專案，並與本機 AI 輔助編碼工具一起完成任務。選專案目錄後可：

- 掃描服務與編譯專案，標出需重編；各專案一句用途，開啟時寫入 `.ai_project/product-purposes.md`
- 一鍵啟動／停止、看 Log；開前端會先確保服務就緒
- GitHub：clone、提交、同步、PR、Actions、Release、檢查更新
- 需求工作台：進件關卡、驗收到報帳
- 工時從開啟專案起算，可匯出 CSV／Markdown
- Agent 求救、MCP 回呼、審計
- 專案問答：OpenAI 相容多來源（本機 Ollama／LM Studio，或 OpenAI／Groq／OpenRouter），只讀堆疊事實
- 被管專案的 DocFX／GitHub Pages 輔助

安裝包見 [Releases](https://github.com/sjvann/AI_Project_Console/releases)。程式碼不上本產品自己的雲。

## 規劃（尚未出貨，不能當安裝包驗收）

| 代號 | 產品 | 狀態 |
|------|------|------|
| P2 | 公司工作區 | 規劃；**下一實作**。公開回報契約＋六大模組；獨立安裝產物（我們主機與自架同一通道）。倉庫內 `AiProject.Company.*` 是開發示範，未授權正式營運 |
| P3 | 系統分析輔助 | 規劃；P2 之後。概念 → 需求 → 規格 → Issue |
| P4 | 仲介平台 | 規劃；**門檻產品**。P3 驗證成功後才對非軟體業廠商開放 |

建構順序：控制台已出貨 → 公司端能收貢獻回報 → 系統分析能產出可指派 Issue → 仲介才有資格服務「不會寫需求」的廠商。細節：[產品規格](README.md) · [路線圖](roadmap.md) · [版權與授權](../legal/copyright.md)。
