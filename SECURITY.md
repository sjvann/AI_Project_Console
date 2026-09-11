# 安全回報

請**不要**用公開 Issue 回報可被利用的漏洞。

Please **do not** file public GitHub Issues for exploitable vulnerabilities.

## 如何回報

請對本倉庫開 [GitHub Security Advisory（非公開）](https://github.com/sjvann/AI_Project_Console/security/advisories/new)，或經倉庫頁聯絡維護者。請附：

- 影響的版本（例如控制台 0.6.13）
- 重現步驟（不含攻擊用 payload）
- 預期影響（本機資料、GitHub token、他人專案等）

我們會確認後再決定公開時程。請給合理處理時間。

## 範圍

**在範圍內：** AI_Project 控制台安裝包、本倉庫的 MCP 伺服器、本機設定與工時檔。

**通常不在範圍內：** 你本機 Agent（Cursor 等）自己的行為、你開啟的第三方專案、GitHub 平台本身。

## 本倉庫公開後仍請當示範憑證

公司工作區（`AiProject.Company.*`）是**規劃／開發示範**，不是已出貨產品：

- `appsettings.Development.json` 與登入頁顯示的帳號密碼只給本機開發。
- `appsettings.json` 的連線字串、空白 Token、佔位加密金鑰不是生產設定。
- 任何共享或公開環境都必須自行換成強密碼與新的 `Company:EncryptionKey`，並關閉 `SeedDemoData`。

控制台**不會**把你專案的原始碼上傳到本產品自己的雲。Agent 與專案問答使用你本機或你設定的端點。詳見 [使用文件](https://sjvann.github.io/AI_Project_Console/docs/user/getting-started.html)。

## 支援的版本

目前只對 [GitHub Releases](https://github.com/sjvann/AI_Project_Console/releases) 上標為 Latest 的正式版提供安全修正。預發行（RC）與舊 minor 不保證回修。
