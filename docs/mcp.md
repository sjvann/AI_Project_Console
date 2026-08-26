# Agentic 與 MCP

控制台不再只是「把錯誤貼給 Agent」。Agent 也可以**反向呼叫控制台**，替開發管理者執行本機堆疊操作。

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

## 為什麼這對管理者有用

| 情境 | 以前 | 現在（Agent + MCP） |
|------|------|---------------------|
| 晨會／值班 | 自己開控制台對 port | `duty_summary`（必要時再 `stack_status` + `git_status`） |
| AI 剛改完碼 | 人去終端機 `dotnet build` | Agent 自己 `build mode=stale` |
| 服務起不來 | 翻 `.ai_project/logs` | `get_log` → 修碼 → `start_service` |
| 發版前 | 口頭問「都編過了嗎」 | `list_projects` 需重編必須為 0 |

控制台仍是**真相來源**；Agent 是操作者。不要讓 Agent 自己猜 port 或亂殺行程。

## 接上 Cursor

1. 控制台「設定」→ MCP：加入「本控制台」（本機堆疊）。也可加入建議範本（GitHub、Context7）或「自訂」填命令／URL，寫進專案 `.cursor/mcp.json`。
2. 重新載入 Cursor 後，Agent 應能看到本控制台工具：`stack_status`、`build`、`get_log`…；其他 MCP 的工具一併出現。
3. 求救提示已附驗證步驟，要求 Agent 用這些工具，而不是只改檔。

手動指令（開發）：

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root E:\your\repo --list-tools
dotnet run --project src/AiProject.Console.Mcp -- --root E:\your\repo --invoke stack_status
```

stdio 模式（給 IDE 用）不要加 `--invoke`：

```powershell
dotnet run --project src/AiProject.Console.Mcp -- --root "${workspaceFolder}"
```

## 建議並排的其他 MCP（可一直加，不鎖死三個）

設定裡的清單**不是**「只能這三個」。本控制台只內建 `ai-project-console`；GitHub、Context7 是一鍵範本，公司內部閘道用「自訂」。全部寫進專案 `.cursor/mcp.json`，讓 Agent 並排呼叫。

| 加入方式 | 寫入內容 | 之後要做的事 |
|----------|----------|----------------|
| 本控制台 | `dotnet`／exe + `--root` | 重新載入 Cursor |
| GitHub | `https://api.githubcopilot.com/mcp/` | 在 Cursor 或 mcp.json 補 Personal Access Token |
| Context7 | `npx -y @upstash/context7-mcp` | 重新載入後即可查套件文件 |
| 自訂 | 你填的 `command`＋參數，或遠端 `url` | 依該服務補權杖／環境變數 |

也可按「開啟 mcp.json」直接編輯，或「複製本控制台設定」貼到使用者全域 `~/.cursor/mcp.json`。清單只顯示**目前專案**檔，不含全域。

原則：遠端與文件用別人的 MCP；**本機堆疊只走 ai-project-console**。

## 工具一覽

見 `StackToolRouter.Tools`：`duty_summary`、`stack_status`、`list_services`、`list_projects`、`build_freshness`、`build`、`start_service`、`stop_service`、`start_all`、`stop_all`、`get_log`、`doctor`、`git_status`、`list_audit`。

晨會／值班先呼叫 `duty_summary`（就緒、離線、需重編、最近 MCP 拒絕）。控制台摘要列也顯示同一組數字，點「需重編」會切到專案頁，點「MCP 拒絕」會打開審計分頁。

## 企業政策（白名單／確認／審計）

控制台設定「工具權限」或專案檔 `{root}/.ai_project/mcp-policy.json`：

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

## MCP 審計（控制台右側分頁）

**用途：** 對帳「Agent 對本機堆疊做了什麼」。每次 MCP 工具呼叫（成功或被拒）寫入 `{root}/.ai_project/mcp-audit.jsonl`。服務 Log 看程式輸出；審計看工具名、參數、耗時、拒絕原因。

**怎麼用**

1. 設定 → MCP，把「本控制台」加入專案，並重新載入 Cursor。
2. 請 Agent 呼叫 `stack_status`、`build`、`get_log` 等（或問「用 `duty_summary` 看現在能不能交班」）。
3. 打開右側「MCP 審計」，按「重新載入」。摘要列「MCP 拒絕」有數字時點一下也會打開這頁。
4. 搜尋可過濾工具名／參數／拒絕原因；「開啟檔案」用系統編輯器看完整 jsonl。Agent 也可用 `list_audit`。

**怎麼讀一列**

| 欄 | 意義 |
|----|------|
| 時間 | 本地時間；今天只顯示時分秒 |
| OK／拒 | 成功，或政策禁止／缺 `confirm`／工具失敗 |
| 工具 | 例如 `stack_status`、`stop_all` |
| 耗時 | 該次呼叫毫秒 |
| 參數／拒絕原因 | 成功看參數；拒絕看原因（例如需要 `confirm=true`） |

上方政策列對應設定或 `mcp-policy.json`：**可寫／唯讀**＝能否編譯與啟停；**允許**空白＝全部已知工具；**禁止**一律擋下；**需確認**必須帶 `confirm=true`（預設 `stop_all`）。

尚無紀錄通常表示 Agent 還沒呼叫堆疊工具，或控制台尚未加入 Cursor 的 MCP——不是故障。

## 專案問答（控制台內對談）

右側第四個頁籤。用設定裡的 **OpenAI 相容**端點（預設本機 Ollama `http://127.0.0.1:11434/v1`、模型 `llama3.2`）在控制台內回答，並本機呼叫唯讀堆疊工具（`duty_summary`、`stack_status`、`get_log`、`doctor`、`git_status`、`list_audit`…）。**不會**外開 Cursor，也**不會**佔用啟動／編譯的忙碌鎖。

**怎麼用**

1. 設定 → 專案問答，確認 Base URL 與模型。本機 Ollama 的 API key 可空白；OpenAI／Groq 填 key 並改 URL。
2. 點右側「專案問答」。可點建議題（離線／需重編／MCP 拒絕有數字時會置頂並標「建議」），或自己打字送出。
3. 等回覆時可切到 Log／審計、按啟動。要中斷按「取消」。

這頁只談目前開啟的專案。編譯與啟停仍用控制台按鈕或既有「求救」。
