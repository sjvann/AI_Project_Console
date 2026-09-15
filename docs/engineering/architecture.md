---
title: 工程架構
---

# 工程架構

本頁給要改**這個倉庫**的人。產品家族不變量見 [技術架構](../product/architecture.md)；日常開發指令見 [開發與維護](../maintainer/develop.md)。

## 現況：控制台

- .NET 10、Photino.Blazor 桌面視窗、Blazor UI（過渡宿主；目標見產品架構 AD-9／AD-10）
- 核心在 `AiProject.Console.Core`（掃描、建置、GitHub、工時、進件、文件）
- MCP 在 `AiProject.Console.Mcp`（或產品 exe `--mcp`）
- 正式安裝包：Windows x64 self-contained，GitHub Release 自動更新，見 [發版](../maintainer/release.md)

## 規劃：四套產品、同一核心

`AiProject.Company.*` 是開發中的 Blazor Web App 與領域層，**不是**已出貨安裝包。P3／P4 尚未建專案。部署與金鑰注意見 [公司工作區部署](../product/deploy-company.md)。授權上亦未允許正式營運，見 [版權與授權](../legal/copyright.md)。家族共用 .NET 10、Blazor、DesignSystem、UpdateClient；產品間只靠契約。詳見 [技術架構](../product/architecture.md)。
