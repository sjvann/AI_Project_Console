---
title: 公司平台自架
---

# 公司工作區部署

**規劃。** 工作區是**可獨立安裝**的產品：我們主機多租戶與客戶自架使用**同一發行產物**，差在設定（`Hosting:Mode`、連線字串、憑證）。見 [架構 AD-12](architecture.md)。憑證與備份：SaaS 由我們管；自架由客戶管。

工作區可獨立於仲介存在。仲介是門檻後另一個產品，預設由我們營運；不要把工作區部署文件寫成「仲介的子站」。跨產品連線預設關閉，契約測試通過才啟用。

## 需求

- .NET 10 執行時
- PostgreSQL 16（試用與生產一律 PostgreSQL；SQLite 只給開發與自動化測試）
- 反向代理終止 TLS（nginx／IIS）
- GitHub App（讀 org／Issue、寫回 assignee；PAT 僅備援）。**後台不需要 GitHub OAuth 登入。**

## 金鑰

`Company:EncryptionKey` 必須是可解出 32 位元組的 Base64。遺失金鑰則薪資／費率欄無法讀，不會默默用明文。請離線備份金鑰。

產生：

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }) -as [byte[]])
```

## 設定

| 鍵 | 用途 |
|----|------|
| `Hosting:Mode` | `SaaS`（我們多租戶）或 `SelfHosted`（客戶主機）。同一發行產物，見 [架構 AD-12](architecture.md) |
| `ConnectionStrings:Company` | PostgreSQL |
| `Company:EncryptionKey` | 欄位加密 |
| `Company:Auth:LocalOwner:Enabled` | 第一次啟動時種入公司管理員帳戶 |
| `Company:Auth:LocalOwner:UserName` | 管理員登入帳號 |
| `Company:Auth:LocalOwner:Password` | 管理員初始密碼（寫入資料庫雜湊後，請到「帳戶」改掉；生產用環境變數） |
| `Company:GitHub:WebHost` / `ApiHost` | GHE 時改主機 |
| `Company:GitHub:Org` / `Token` | 讀 org 倉與寫回 assignee（App token 或 PAT） |
| `Company:SeedDemoData` | 開發用範例資料。生產必須 `false` |

## 啟動

```powershell
dotnet run --project src/AiProject.Company.Web
```

健康檢查：`GET /health`（回傳 `status` 與 `hostingMode`）。OpenAPI：`/openapi/v1.json`。

## Docker／compose（A8 同一產物）

倉庫內路徑：`deploy/workspace/`。自架與我們主機**同一 Dockerfile**；差在環境變數。

```powershell
cd deploy/workspace
Copy-Item .env.example .env
# 編輯 .env：COMPANY_ENCRYPTION_KEY、OWNER_PASSWORD、POSTGRES_PASSWORD
docker compose up -d --build
# 瀏覽 http://localhost:5100 ；健康檢查 http://localhost:5100/health
```

| 環境變數 | 對應設定 |
|----------|----------|
| `Hosting__Mode` | `SelfHosted`（預設）或 `SaaS` |
| `ConnectionStrings__Company` | compose 已指向 `db` 服務 |
| `Company__EncryptionKey` | 欄位加密（必填） |
| `Company__Auth__LocalOwner__*` | 第一次啟動種 owner |

發行資產：`scripts/pack-workspace.ps1 -Version 0.1.0` → `dist/workspace-v0.1.0/`（compose、`.env.example`、`image-digest.txt`、`SHA256SUMS.txt`）。Release tag 前綴 `workspace-v*`。

## 後台管理者（公司帳戶，不是 GitHub）

經營層、人資、財務、交付主管、專案經理、外包窗口用**公司帳戶**（帳號＋密碼）登這個網站。工程師、從線上招募進來的外部工程師用**桌面控制台**的 GitHub，不在後台開登入。

開發環境開 http://localhost:5100/login ：

| 欄 | 值 |
|----|----|
| 帳號 | `owner` |
| 密碼 | `AiProject-Owner-2026` |
| 角色 | 公司管理員 |

登入後到「帳戶」：幫人資／財務開公司帳戶；用 **GitHub 邀請**把招募進來的工程師建檔（綁 GitHub，控制台上傳才能對到人）。到「帳戶」改掉管理員密碼後，設定檔裡的初始密碼不再使用。

## 開發範例資料

`appsettings.Development.json` 開 `Company:SeedDemoData`。第一次啟動（或尚無 `exec` 帳戶／「晨星銀行」客戶時）會種入凌波資訊的測試公司：三個客戶、逾期專案、派工、工時、招募邀請、待歸戶、九月薪資週期。重啟時會補上控制台示範專案 `AI_Project_Console`（專案碼 `CONSOLE-DEMO`）與王工程的固定回報 API 金鑰。

| 帳號 | 密碼 | 角色 | 建議驗收 |
|------|------|------|----------|
| `owner` | `AiProject-Owner-2026` | 公司管理員 | 設定、帳戶 |
| `exec` | `Demo-Pass-2026` | 經營層 | 戰情室紅燈／逾期 |
| `delivery` | 同上 | 交付主管 | 派工週矩陣、超載 |
| `pm` | 同上 | 專案經理 | 專案甘特、公開回報確認工時 |
| `hr` | 同上 | 人資 | 人員、薪資週期、待歸戶 |
| `finance` | 同上 | 財務 | 預算／USD 匯率 |
| `vendor` | 同上 | 外包窗口 | 只能看迅馳科技己方 |

工程師不登後台。控制台 GitHub：`wang-dev`（正職）、`li-analyst`（招募進來的個人外包）、`chao-lead`、`chen-vendor`。待接受邀請 `newhire-dev`；未綁定上傳 `ghost-coder`。

## 控制台 ↔ 工作區範例整合（開發）

API 已存在，但入帳要在工作區**看得見**。開發示範走這條，不必把你自己的 GitHub 綁進名冊：

1. 啟動公司工作區：`dotnet run --project src/AiProject.Company.Web` → http://localhost:5100
2. 用 `pm` / `Demo-Pass-2026` 登入，打開導覽 **公開回報**（設定裡也有「公開回報」分頁）。複製 Base URL 與示範 API 金鑰。
3. 控制台設定 → 申報目的地：按「加入本機公司工作區」，把金鑰貼到該筆，測試連線 → 啟用這家公司。
4. 開啟本機資料夾 `AI_Project_Console`（名稱會對到示範專案）、讓工時儀表板有時段，按「送到〔凌波資訊（本機示範）〕」。
5. 回到工作區「公開回報」→「待 PM 確認」。PM 確認歸屬；人資再到「薪資」。

握手也可用 GitHub 權杖：名冊 GitHub 必須是 `wang-dev`（或你在人員卡片改成自己的 login）。未對到的上傳進待歸戶。

已有範例資料後重啟不會重複種客戶與帳戶。示範專案與 API 金鑰若缺失會補上。要重來：停掉網站後刪掉 `company.dev.db`（或清掉「晨星銀行」與 `exec` 帳戶）再啟動。

生產第一次啟動用環境變數種管理員（密碼勿提交進 git）：

```powershell
$env:Company__Auth__LocalOwner__Enabled = "true"
$env:Company__Auth__LocalOwner__UserName = "owner"
$env:Company__Auth__LocalOwner__Password = "請改成你的長密碼"
```

已有公司帳戶後，重啟不會覆寫密碼。

本機可自備 PostgreSQL；生產請用反向代理終止 TLS，不要把示範密碼或加密金鑰提交進 git。

## 備份還原

1. `pg_dump -Fc aiproject_company > company.dump`
2. 另存 `EncryptionKey`
3. 還原：`pg_restore -d aiproject_company company.dump` 後放入同一把金鑰再啟動

## GitHub App 權限

- 讀 org 成員與 repo
- 讀 Issue
- 寫 Issue assignee（可關 `WriteBackGithubAssignee`）

輪替 Client Secret 與 PAT 後重啟程序。PAT 只作安裝備援。後台沒有使用者 OAuth。

## 控制台

控制台是獨立安裝包，不是本公司發行的前後台。把本實例的接收 API 位址（HTTPS Base URL）交給參與專案的工程師，請他們在控制台加成**申報目的地**。握手用控制台既有的 GitHub 權杖（`gh auth`）或公司核發的 `apk_` 回報金鑰，標頭 `X-Company-Api-Version: 1`。名冊有此人才可上傳；本機 `work-hours.json` 仍是真相。未邀請、未綁人員檔的 GitHub 會進待歸戶。PM 在導覽「公開回報」看待確認。同一工程師的控制台可以同時對別家公司申報，與本實例無關。

## 導入一個月

1. owner 完成公司設定
2. 開人資／PM 的公司帳戶；用 GitHub 邀請招募進來的工程師
3. 建客戶→合約→專案→掛倉
4. 派工一週
5. 工程師從控制台上傳 → PM 確認 → 人資鎖定 → CSV
6. 財務看損益；經營層開戰情室

這不是 HRIS、不是報稅、不是 Jira 替代品。
