---
title: 產品規格
---

# 產品規格

這份規格給**產品與下一階段實作**對齊用。一般使用者請走 [使用文件](../README.md)，不必從這裡開始。

## 現況與規劃

| 標籤 | 意思 | 現在能不能演示 |
|------|------|----------------|
| **現況** | 已在 Windows 桌面控制台出貨（約 0.6.x） | 能。見 [90 分鐘導入](../team/trial-90min.md) |
| **規劃** | 仲介平台（下一實作）與成交後工作區 | 不能當成已出貨。見 [路線圖](roadmap.md) |

銷售與簡報必須沿用這兩個標籤。把規劃講成現況，等於對買家說謊。

本目錄的 `intake.json`、`design/`、`intake-assets/` 是**控制台需求進件**寫入的資料，不是這份規格書。規格書是下面這些 Markdown。

## 三層產品、同一品牌

| 產品 | 狀態 | 給誰 | 技術 |
|------|------|------|------|
| **AI_Project 控制台** | 現況 | 工程師、Tech Lead、本機 PM | .NET 10、Photino.Blazor、Windows 桌面 |
| **AI_Project 仲介平台** | 規劃；**下一實作** | 需求公司（不限軟體業）、工程師會員 | .NET 10、Blazor Web App、**我們營運的 SaaS** |
| **AI_Project 公司工作區** | 規劃；**成交後** | 需求公司管理面（經營／交付／人資／財務） | 沿用 Company.*；預設我們主機租戶，自架後期 |

控制台管本機堆疊與工時真相，**給所有工程師用**。仲介管會員、認證、發布、成交、合同與抽成應收。工作區管人、專案、毛利與發薪匯出，由仲介在成交後開租戶。三者用 GitHub 身分與**接收 API**接起來。切割以 [總規格](spec.md#三層產品) 為準；仲介細節見 [仲介平台](marketplace.md)。

## 閱讀順序

1. [願景與買家](vision.md) — 賣給誰、三層產品、刻意不做什麼
2. [仲介平台](marketplace.md) — 會員、認證、精靈、成交、合同、抽成
3. [角色](personas.md) — 使用者與進場畫面
4. [總規格](spec.md) — 詞彙、權限、主鍵、三層邊界
5. [產品需求（PRD）](prd.md) — 波次 A 仲介／波次 B 工作區
6. [系統執行計劃書](execution-plan.md) — 仲介第一；工作區租戶化
7. 工作區六大模組（成交後；除非內文標現況）
   - [人員與外包](modules/people.md)
   - [派工](modules/dispatch.md)
   - [專案與客戶](modules/projects-clients.md)
   - [預算費用](modules/budget.md)
   - [薪資](modules/payroll.md)
   - [戰情室](modules/war-room.md)
8. [UI／UX](ux.md) — 資訊架構、關鍵畫面、桌面 vs Web
9. [技術架構](architecture.md) — 我們營運多租戶、接收 API
10. [路線圖](roadmap.md) — 已交付／仲介第一刀／工作區／後續

買家若只有五分鐘：讀 [銷售套件一頁紙](../salekit/one-pager.md)，再翻本頁的三層表。需求與驗收以 [PRD](prd.md) 為準；施工順序以 [系統執行計劃書](execution-plan.md) 為準。
