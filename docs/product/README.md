---
title: 產品規格
---

# 產品規格

這份規格給**產品與下一階段實作**對齊用。一般使用者請走 [使用文件](../README.md)，不必從這裡開始。

## 現況與規劃

| 標籤 | 意思 | 現在能不能演示 |
|------|------|----------------|
| **現況** | 已在 Windows 桌面控制台出貨（**0.6.13**） | 能。見 [90 分鐘導入](../team/trial-90min.md) · [現行版本](../user/whats-new.md) |
| **規劃** | 仲介平台（下一實作）與公司工作區 | 不能當成已出貨。見 [路線圖](roadmap.md) |

銷售與簡報必須沿用這兩個標籤。把規劃講成現況，等於對買家說謊。

本目錄的 `intake.json`、`design/`、`intake-assets/` 是**控制台需求進件**寫入的資料，不是這份規格書。規格書是下面這些 Markdown。

## 三套獨立機制、同一品牌

| 產品 | 狀態 | 機制 | 技術 |
|------|------|------|------|
| **AI_Project 仲介平台** | 規劃；**下一實作** | 媒合與成交 | .NET 10、Blazor Web App、**我們營運的 SaaS** |
| **AI_Project 公司工作區** | 規劃；可獨立開帳 | 需求公司管理工程師（經營／交付／人資／財務） | 沿用 Company.*；預設我們主機租戶，自架後期 |
| **AI_Project 控制台** | 現況 | 工程師承接專案後做工 | .NET 10、Photino.Blazor、Windows 桌面 |

仲介管會員、認證、發布、成交、合同與抽成應收。工作區管人、專案、毛利與發薪匯出，**不必先有仲介成交**。控制台管本機堆疊與工時真相，**給所有工程師用**。三者用成交事件、GitHub 身分與**接收 API**交換資料，不共用資料庫。切割以 [總規格](spec.md#三層產品) 為準；仲介細節見 [仲介平台](marketplace.md)。

## 閱讀順序

1. [產品概觀](overview.md) — 0.6.13 現況與規劃對照
2. [願景與買家](vision.md) — 賣給誰、三層產品、刻意不做什麼
3. [仲介平台](marketplace.md) — 會員、認證、精靈、成交、合同、抽成
4. [角色](personas.md) — 使用者與進場畫面
5. [總規格](spec.md) — 詞彙、權限、主鍵、三層邊界
6. [詞彙](glossary.md) — 現況／規劃對照表
7. [進件](intake.md) — 控制台進件（現況）與仲介精靈（規劃）
8. [產品需求（PRD）](prd.md) — 波次 A 仲介／波次 B 工作區
9. [系統執行計劃書](execution-plan.md) — 仲介第一；工作區租戶化
10. 工作區六大模組（獨立機制；除非內文標現況）
   - [人員與外包](modules/people.md)
   - [派工](modules/dispatch.md)
   - [專案與客戶](modules/projects-clients.md)
   - [預算費用](modules/budget.md)
   - [薪資](modules/payroll.md)
   - [戰情室](modules/war-room.md)
11. [UI／UX](ux.md) — 資訊架構、關鍵畫面、桌面 vs Web
12. [技術架構](architecture.md) — 我們營運多租戶、接收 API
13. [路線圖](roadmap.md) — 已交付／仲介第一刀／工作區／後續

走讀畫面：[規劃雛型](https://github.com/sjvann/AI_Project_Console/blob/main/prototype/README.md)（兩套獨立產品：仲介、公司工作區；控制台不做雛型。原始碼在倉庫 `prototype/`，不隨文件站發布）。

買家若只有五分鐘：讀 [銷售套件一頁紙](../salekit/one-pager.md)，再翻本頁的三層表。需求與驗收以 [PRD](prd.md) 為準；施工順序以 [系統執行計劃書](execution-plan.md) 為準。
