---
title: 營運部署
---

# 營運部署

分三層，不要混在一起。

| 要部署什麼 | 狀態 | 文件 |
|------------|------|------|
| **控制台**到工程師電腦 | 現況 | [安裝與第一次使用](../user/getting-started.md) · [發版](../maintainer/release.md) |
| 被管專案的發佈目標（GCP／Azure／自家機） | 現況（只記設定，不代管 CI） | [部署](../user/deploy.md) |
| 被管專案的說明站 | 現況 | [專案文件](../user/docs.md) · GitHub Pages |
| **公司工作區**／仲介 SaaS | 規劃 | [公司工作區部署](../product/deploy-company.md) · [技術架構](../product/architecture.md) |

控制台關閉視窗**不會**停掉已啟動的服務。生產金鑰、示範密碼與加密金鑰注意見 [SECURITY.md](https://github.com/sjvann/AI_Project_Console/blob/main/SECURITY.md)。
