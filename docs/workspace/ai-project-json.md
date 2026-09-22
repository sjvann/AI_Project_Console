# 工作區設定（ai-project.json）

`ai-project.json` 放在**你選進控制台的那個根目錄**。可選：沒有它時，控制台會掃描專案檔（`.csproj`、`package.json`、`pyproject.toml`、`go.mod`、`Cargo.toml`、`pom.xml` 等）與 `launchSettings.json` 推服務。下列情況請補清單：

- 根目錄本身幾乎沒有專案（薄工作區），真正的產品線在隔壁資料夾
- 掃描到的服務名稱、port、健康檢查不對
- 要固定啟動順序、預設前端，或宣告服務啟動相依（`dependsOn`）
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

`services` 有內容時，**預設以清單為準**，不再只靠掃描結果當服務列。

若只要**多一個群組**（例如桌面控制台），掃描到的 API／Web 仍要留著，加 `"mergeScanServices": true`（也可用 `merge_scan_services`／`mergeScan`）。清單列會覆寫同路徑的掃描項（可改 `label`／`group`），其餘掃描結果照舊。

`scanProjects` 與 `services` **不是互斥**：不寫或 `true` 時專案頁仍掃專案檔。只有要關掉專案掃描時才設 `"scanProjects": false`（也可用 `scan_projects`／`scan`）；專案頁會變空，編譯過期項目也不再列那些專案。

| 欄位 | 必填 | 說明 |
|------|------|------|
| `project` | 是 | 相對路徑，指向專案目錄、`.csproj`（副檔名可省略）、`package.json` 所在目錄，或 Python 控制台腳本（`.py`；Windows 以 `py -3` 啟動） |
| `id` | 否 | 穩定識別；空白則由名稱產生 |
| `label` | 否 | 畫面上的名稱 |
| `description` | 否 | 服務列名稱旁的一句用途。沒寫則用該專案掃到的用途（見下方「專案用途」） |
| `port` | 否 | 顯示與推斷健康檢查用 |
| `health` | 否 | 健康檢查。HTTP URL；或桌面程式用 `mutex:Local\Name`、`tcp:17888`。空白且有 port 時預設 `http://127.0.0.1:{port}/health` |
| `openUrl` | 否 | 「開啟」用的瀏覽器網址 |
| `aspnetUrls` | 否 | 傳給 ASP.NET 的 `ASPNETCORE_URLS`（也可用 `urls`） |
| `group` | 否 | 左側分組標題。可用 `Lab/HL7` 表示次群組（`/` 分段，也接受 `\`）。未寫斜線則先當單層；若同一群組裡有兩種以上共用名稱開頭（例如兩個 HL7…、兩個 SFTP…），畫面會自動拆次群組。要固定層級請寫路徑 |
| `hostedBy` | 否 | 掛在另一個服務的 id 上，不單獨啟動 |
| `preStart` | 否 | 相對工作區根的腳本；也可用 `ensure`。啟動該服務前先跑，失敗則不起行程。**禁止**用來啟動另一條產品線。依副檔名分流：`.py` 用 `py -3`／`python`／`python3`；`.ps1` 用 powershell／pwsh；`.sh` 用 bash |
| `dependsOn` | 否 | 另一個行程的啟動相依（見下方）。與 `hostedBy` 不同。**開啟**與**啟動**都會先起相依 |
| `ready` | 否 | 等就緒用的 URL；空白則用 `health`。跨線相依請用這個，不要只看 port 占用 |
| `readyTimeoutMs` | 否 | 等就緒逾時毫秒；空白預設 180000 |

## 群組說明（groups）

可選。頂層服務群組名稱旁顯示一句話，方便辨識（例如 Clinic vs Solution）：

```json
"groups": [
  { "id": "Clinic", "description": "第一線臨床畫面（櫃檯／個案工作區）" },
  { "id": "Solution", "description": "可換端點的工具與中介（含 EHR Launch／抽審）" }
]
```

也可用 `serviceGroups`／`service_groups`。`id`（或 `name`／`key`）對齊 `services[].group` 的頂層段；`description`（或 `label`／`summary`）為短說明。未宣告的群組不顯示說明。

## 專案用途（所有專案）

可啟動服務與函式庫、測試專案都要有一句產品功能描述。不必每個都寫進清單：掃描會從專案檔擷取。

| 來源（先寫的贏） | 欄位 |
|------------------|------|
| 工作區 `ai-project.json` 的 `projects` | `path`（或 `project`）＋ `description`。也可用物件：`"projects": { "src/Foo": "一句用途" }` |
| 專案檔 | .NET：`<Description>`，沒有則 `<PackageDescription>` |
| 套件清單 | `package.json`、`pyproject.toml`、`Cargo.toml`、`pom.xml` 的 description |
| 專案目錄 README | `README.md`（或 `README.zh-Hant.md`）標題與徽章之後的第一段 |

```json
"projects": [
  { "path": "src/Demo.Core", "description": "核心領域模型" }
]
```

開啟或重新掃描工作區時，控制台把目前掃到的用途寫成 `.ai_project/product-purposes.md`。這份是給人眼與 Agent 對照的快照，下次會覆寫；要改用途請改上面的權威來源，不要手改這份檔。

## 啟動相依（dependsOn）

三層分工，不要混：

| 誰 | 做什麼 | 不做什麼 |
|----|--------|----------|
| **AI_Project 控制台**（0.6.6+） | 解析 `dependsOn`、先起相依、等 `ready`、循環檢查、略過可選相依 | 不把「先起誰」寫死在程式裡 |
| **工作區 `ai-project.json`** | 只宣告誰依誰 | 不改各倉應用程式碼 |
| **各應用** | 維持獨立可開；連不上就說清楚 | 不要在程式裡啟動其他服務（禁止 `dotnet run` 另一條產品線） |

相依是本機編排，不是編譯相依。寫在清單裡，由控制台先起、等到就緒，再起自己。

```json
"dependsOn": ["lab-api"]
```

字串＝硬相依：沒起來就不要起自己（例如 Lab Web → API）。

```json
"dependsOn": [{ "id": "fhir-host", "optional": true }]
```

`optional: true`＝軟相依：本機預設先起；使用者要接外部服務時可略過（服務列「啟動」或「開啟」按住 Alt 略過可選相依；「啟動」按住 Shift 只起自己）。

控制台會遞迴展開、偵測循環、把 `hostedBy` 別名解析成真正宿主。不要把這種跨線關係寫進 `startOrder` 或 `preStart`。

**開啟**（有 `openUrl`）：先對目標跑與啟動相同的相依展開與就緒等待，成功後再開瀏覽器；硬相依失敗則不開 URL。

## 啟動順序與前端

```json
"startOrder": ["hub-api", "hub-web", "platform-host"],
"frontend": "hub-web"
```

`startOrder` 裡的 id 要對得上 `services[].id`。`frontend` 是「開啟前端」的預設項。`startOrder` 只影響工具列「啟動全部」的偏好順序，不要拿來表達跨線相依（用 `dependsOn`）。

## GitHub 與部署

可與畫面「GitHub 設定」「部署設定」互相寫入。結構見範例檔的 `github`、`deploy`、`gcp`、`onprem`、`azure`。`github.host`／`github.kind` 指定認證對象（公開 GitHub 或企業主機）。使用者操作見 [GitHub](../user/github.md)、[部署](../user/deploy.md)、[需求工作台](../user/intake.md)。

## 執行期目錄

選定專案根下會出現 `.ai_project/`：

| 子目錄／檔 | 用途 |
|------------|------|
| `logs/` | 各服務輸出 |
| `pids/` | 行程紀錄 |
| `build-reports/` | 編譯成功／失敗報告 |
| `uat-reports/` | UAT 截圖 |
| `agent-prompts/` | 求救提示備份 |
| `product-purposes.md` | 開啟／重掃時產生的各專案用途清單 |
| `mcp-audit.jsonl` | MCP 對帳 |
| `mcp-policy.json` | 專案級工具權限（可選） |

這些是本機執行期資料，通常不要提交（視團隊 `.gitignore` 而定）。`ai-project.json` 本身則適合進版控，作為團隊採用控制台的證據。

## 改完清單之後

工作區已開啟時，按專案列 **重新掃描**（不必關閉再開）。環境體檢會顯示 `manifest: ai-project.json 已載入` 與服務列表。
