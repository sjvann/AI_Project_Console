# Agentic 與 MCP

一般使用者若只要「按求救、看審計」，先讀 [卡住時怎麼辦](../user/help.md) 與 [設定](../user/settings.md)。本頁給要讓 **Agent 回呼控制台** 的人。

控制台不只把錯誤貼給 Agent。Agent 也可以反向呼叫控制台，執行本機堆疊操作。

```
開發者／管理者
    │
    ├─ 控制台 UI：掃描、編譯、啟停、求救
    │
    └─ Agent（Cursor／Claude Code／…）
           │  MCP stdio：ai-project-console
           ▼
       堆疊真相（服務健康、需重編原因、Log、git）
```

## 為什麼這有用

| 情境 | 以前 | 現在（Agent + MCP） |
|------|------|---------------------|
| 晨會／值班 | 自己開控制台對 port | `duty_summary`（必要時再 `stack_status` + `git_status`） |
| AI 剛改完碼 | 人去終端機 `dotnet build` | Agent 自己 `build mode=stale` |
| 服務起不來 | 翻 `.ai_project/logs` | `get_log` → 修碼 → `start_service` |
| 發版前 | 口頭問「都編過了嗎」 | `list_projects` 需重編必須為 0 |

控制台仍是**真相來源**；Agent 是操作者。不要讓 Agent 自己猜 port 或亂殺行程。

## 接上 Cursor（一般路徑）

1. 開啟專案後，控制台「設定」→ MCP：加入「本控制台」。也可加入建議範本（GitHub、Context7）或「自訂」填命令／URL。都會寫進專案 `.cursor/mcp.json`。
2. 重新載入 Cursor。Agent 應能看到 `stack_status`、`build`、`get_log` 等；其他 MCP 的工具一併出現。
3. 求救提示已附驗證步驟，要求 Agent 用這些工具，而不是只改檔。

手動指令（從原始碼開發控制台時）：

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root E:\your\repo --list-tools
dotnet run --project src/AiProject.Console.Mcp -- --root E:\your\repo --invoke stack_status
```

stdio 模式（給 IDE 用）不要加 `--invoke`：

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root "${workspaceFolder}"
```

已安裝的控制台寫入 mcp.json 時，會指向**本控制台產品**（`AI_Project_Console.exe --mcp`，或開發時 `dotnet AI_Project_Console.dll --mcp`）以及目前專案 `--root`，不會寫相對路徑 `src/AiProject.Console.Mcp`。相對 `src` 會被 Cursor 當成受管理專案的目錄。

## 建議並排的其他 MCP

設定裡的清單**不是**「只能這三個」。本控制台只內建 `ai-project-console`；GitHub、Context7 是一鍵範本，公司內部閘道用「自訂」。全部寫進專案 `.cursor/mcp.json`。

| 加入方式 | 寫入內容 | 之後要做的事 |
|----------|----------|----------------|
| 本控制台 | 控制台 exe／dll + `--mcp` + 專案 `--root` | 重新載入 Cursor |
| GitHub | `https://api.githubcopilot.com/mcp/` | 在 Cursor 或 mcp.json 補 Personal Access Token |
| Context7 | `npx -y @upstash/context7-mcp` | 重新載入後即可查套件文件 |
| 自訂 | 你填的 `command`＋參數，或遠端 `url` | 依該服務補權杖／環境變數 |

也可按「開啟 mcp.json」直接編輯，或「複製本控制台設定」貼到使用者全域 `~/.cursor/mcp.json`。清單只顯示**目前專案**檔，不含全域。

原則：遠端與文件用別人的 MCP；**本機堆疊只走 ai-project-console**。

## 工具一覽

`duty_summary`、`stack_status`、`list_services`、`list_projects`、`build_freshness`、`build`、`start_service`、`stop_service`、`start_all`、`stop_all`、`get_log`、`doctor`、`git_status`、`ci_status`、`pr_status`、`list_audit`、`docs_status`、`list_docs`、`read_doc`。

`start_service` 預設先起 `dependsOn` 並等到就緒；只要自己時帶 `skipDepends=true`。`start_all` 同樣會補齊相依。

`ci_status` 只回最近 Actions 的通過／失敗／進行中；`pr_status` 只回目前分支能不能請人審。不要叫 Agent 在控制台裡翻 workflow 或 PR 畫面。

文件工具只讀 `docs/`：看狀態、列檔、讀一頁。寫檔請用控制台編輯或 Agent 在工作區改，沒有 `write_doc`。

晨會／值班先呼叫 `duty_summary`（就緒、離線、需重編；政策擋下不列入警報）。遠端 CI 再用 `ci_status`，PR 檢查用 `pr_status`。控制台摘要列：點「需重編」切到專案頁，點「MCP 拒絕」打開對帳單，點「CI／PR」到 GitHub 看 log。

## 企業政策（白名單／確認／審計）

畫面「設定 → 工具權限」，或專案檔 `{root}/.ai_project/mcp-policy.json`：

```json
{
  "readOnly": false,
  "allow": [],
  "deny": [],
  "confirm": ["stop_all"]
}
```

| 欄位 | 意義 |
|------|------|
| `allow` | 空白＝全部已知工具；有值則只暴露這些 |
| `deny` | 一律隱藏且拒絕（即使在 allow 裡） |
| `readOnly` | 只能看狀態／Log／審計，不能 `build`／啟停 |
| `confirm` | 預設 `stop_all`；呼叫時必須帶 `confirm=true` |

專案檔覆蓋使用者設定的同名欄位。`tools/list` 只列出政策允許的工具。

`stop_all` 沒有 `confirm=true`（或 `yes`／`1`）會回 `needConfirm`，Agent 必須先問人再重試：

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root . --invoke stop_all --arg confirm=true
```

## MCP 審計

點摘要列「MCP 拒絕」打開對帳單。每次工具呼叫（成功或被拒）寫入 `{root}/.ai_project/mcp-audit.jsonl`。服務 Log 看程式輸出；這裡看工具名、參數、耗時、拒絕原因。

1. 設定 → MCP，把「本控制台」加入專案，並重新載入 Cursor。
2. 請 Agent 呼叫工具，或問「用 `duty_summary` 看現在能不能交班」。
3. 打開「MCP 審計」，按「重新載入」。摘要「MCP 拒絕」點一下也會打開；政策擋下不會變黃。
4. 搜尋可過濾；「開啟檔案」看完整 jsonl。Agent 可用 `list_audit`。

| 欄 | 意義 |
|----|------|
| 時間 | 本地時間；今天只顯示時分秒 |
| OK／拒 | 成功，或政策禁止／缺 `confirm`／工具失敗 |
| 工具 | 例如 `stack_status`、`stop_all` |
| 耗時 | 該次呼叫毫秒 |
| 參數／拒絕原因 | 成功看參數；拒絕看原因 |

尚無紀錄通常表示 Agent 還沒呼叫，或尚未加入 MCP——不是故障。

專案問答（控制台內對談、不經 MCP 外開）見 [卡住時怎麼辦](../user/help.md)。後端選擇見 [Agent 後端](backends.md)。
