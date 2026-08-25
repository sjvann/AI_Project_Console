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
| 晨會／值班 | 自己開控制台對 port | `stack_status` + `git_status` |
| AI 剛改完碼 | 人去終端機 `dotnet build` | Agent 自己 `build mode=stale` |
| 服務起不來 | 翻 `.ai_project/logs` | `get_log` → 修碼 → `start_service` |
| 發版前 | 口頭問「都編過了嗎」 | `list_projects` 需重編必須為 0 |

控制台仍是**真相來源**；Agent 是操作者。不要讓 Agent 自己猜 port 或亂殺行程。

## 接上 Cursor

1. 控制台「設定」→「複製 MCP 設定」，貼到使用者級 `mcp.json`；或「寫入目前專案 `.cursor/mcp.json`」。
2. 重新載入視窗後，Agent 應能看到工具：`stack_status`、`build`、`get_log`…
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

## 建議並排的其他 MCP（不內建、不鎖死）

本伺服器**不取代**下列能力，管理者在 IDE 裡一起開即可：

- **GitHub MCP**：Issue／PR／Actions，對齊控制台的 GitHub 選單
- **Context7**：套件文件，避免 Agent 用過期 API
- 公司內部閘道／監控 MCP：把事故與本機 `get_log` 對讀

原則：遠端與文件用別人的 MCP；**本機堆疊只走 ai-project-console**。

## 工具一覽

見 `StackToolRouter.Tools`：`stack_status`、`list_services`、`list_projects`、`build_freshness`、`build`、`start_service`、`stop_service`、`start_all`、`stop_all`、`get_log`、`doctor`、`git_status`。

破壞性操作（`stop_all`）請在 Agent 規則裡要求先確認。可在專案 `AGENTS.md` 寫：「停全部服務前必須問管理者」。
