---
title: 系統分析輔助部署
---

# 系統分析輔助部署

獨立 ASP.NET 宿主；與公司工作區、控制台、仲介**分庫分宿主**。見 [架構 AD-8／AD-11](architecture.md)。本站**不得**參考 `Company.Infrastructure`。

開案／產物／Issue 包預設存在行程記憶體：重啟即清空。持久化 DB 另議。

## 需求

- .NET 10 執行時（本機）或 Docker
- 反向代理終止 TLS（生產）
- **無**資料庫（記憶體 store）
- 真實寫入 GitHub 時需 PAT／App token（`issues:write`）

## 設定

| 鍵 | 用途 |
|----|------|
| `Hosting:Mode` | `SaaS` 或 `SelfHosted` |
| `Analysis:GitHub:Repo` | 目標倉 `owner/repo` |
| `Analysis:GitHub:Token` | GitHub token；空則強制 dry-run |
| `Analysis:GitHub:ApiHost` | 預設 `https://api.github.com`（GHE 可改） |
| `Analysis:GitHub:DryRun` | `true`（預設）不打真實 API，回傳 dry-run URL |

## 本機啟動

```powershell
dotnet run --project src/AiProject.Analysis.Web
```

預設：`http://localhost:5200`。健康檢查：`GET /health`（回傳 `status`、`product=analysis`、`version`）。

產物 API：`/api/cases/{id}/requirements|spec|issues.(md|json)`。

## Docker／compose

倉庫路徑：`deploy/analysis/`。

```powershell
cd deploy/analysis
Copy-Item .env.example .env   # 可選：填 ANALYSIS_GITHUB_REPO／TOKEN
docker compose up -d --build
# 瀏覽 http://localhost:5200 ；健康檢查 http://localhost:5200/health
```

## 邊界（勿破）

- 不是仲介發案精靈，也不是控制台雲端進件
- 不連公司工作區 DbContext
- 控制台不另做分析站 UI；工程師經 GitHub assignee／既有任務面接收指派
