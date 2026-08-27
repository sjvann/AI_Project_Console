# 工作區設定（ai-project.json）

`ai-project.json` 放在**你選進控制台的那個根目錄**。可選：沒有它時，控制台會掃描 `.csproj` 與 `launchSettings.json` 推服務。下列情況請補清單：

- 根目錄本身幾乎沒有專案（薄工作區），真正的產品線在隔壁資料夾
- 掃描到的服務名稱、port、健康檢查不對
- 要固定啟動順序或預設前端
- 要把 GitHub／部署設定跟倉一起走

完整範例：[`schema/ai-project.example.json`](../../schema/ai-project.example.json)。

```powershell
Copy-Item schema/ai-project.example.json .\ai-project.json
```

（在工作區根執行；範例檔在控制台倉庫裡，請依實際路徑調整。）

## 產品線（多倉）

薄工作區請加 `productLines`，控制台才會去掃隔壁目錄，並在「專案」頁依線分組：

```json
"productLines": [
  { "id": "ledger", "label": "Ledger", "root": "../FHIR-ProfileServer" },
  { "id": "weave", "label": "Weave", "root": "../AI_EZIE" }
]
```

`root` 相對於工作區根。也可用 `product_lines`、`scanRoots`。

未寫 `productLines` 時，若 `services[].project` 指向 `../某倉/...`，會推斷該倉為一條產品線。

## 服務

`services` 有內容時，**以清單為準**，不再只靠掃描結果當服務列。

若後續作業改由清單內的控制台／入口負責、不要再掃全倉 `.csproj`，可加 `"scanProjects": false`（也可用 `scan_projects`／`scan`）。專案頁會變空，編譯過期項目也不再列那些專案。

| 欄位 | 必填 | 說明 |
|------|------|------|
| `project` | 是 | 相對路徑，指向專案目錄、`.csproj`（副檔名可省略），或 Python 控制台腳本（`.py`；Windows 以 `py -3` 啟動） |
| `id` | 否 | 穩定識別；空白則由名稱產生 |
| `label` | 否 | 畫面上的名稱 |
| `port` | 否 | 顯示與推斷健康檢查用 |
| `health` | 否 | 健康檢查 URL；空白且有 port 時預設 `http://127.0.0.1:{port}/health` |
| `openUrl` | 否 | 「開啟」用的瀏覽器網址 |
| `aspnetUrls` | 否 | 傳給 ASP.NET 的 `ASPNETCORE_URLS`（也可用 `urls`） |
| `group` | 否 | 左側分組標題 |
| `hostedBy` | 否 | 掛在另一個服務的 id 上，不單獨啟動 |
| `preStart` | 否 | 相對工作區根的腳本；也可用 `ensure`。啟動該服務前先跑，失敗則不起行程。**禁止**用來啟動另一條產品線 |

## 啟動順序與前端

```json
"startOrder": ["hub-api", "hub-web", "platform-host"],
"frontend": "hub-web"
```

`startOrder` 裡的 id 要對得上 `services[].id`。`frontend` 是「開啟前端」的預設項。

## GitHub 與部署

可與畫面「GitHub 設定」「部署設定」互相寫入。結構見範例檔的 `github`、`deploy`、`gcp`、`onprem`、`azure`。使用者操作見 [GitHub](../user/github.md)、[部署](../user/deploy.md)。

## 執行期目錄

選定專案根下會出現 `.ai_project/`：

| 子目錄／檔 | 用途 |
|------------|------|
| `logs/` | 各服務輸出 |
| `pids/` | 行程紀錄 |
| `build-reports/` | 編譯成功／失敗報告 |
| `uat-reports/` | UAT 截圖 |
| `agent-prompts/` | 求救提示備份 |
| `mcp-audit.jsonl` | MCP 對帳 |
| `mcp-policy.json` | 專案級工具權限（可選） |

這些是本機執行期資料，通常不要提交（視團隊 `.gitignore` 而定）。`ai-project.json` 本身則適合進版控，作為團隊採用控制台的證據。

## 改完清單之後

關閉再重新選擇同一目錄，或換一個歷史項目再選回來，讓控制台重掃。環境體檢會顯示 `manifest: ai-project.json 已載入` 與服務列表。
