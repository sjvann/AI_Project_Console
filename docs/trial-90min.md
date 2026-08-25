# 90 分鐘導入劇本

目標：讓一位 Tech Lead 用自己的多服務 .NET 倉走完「掃描 → 編譯真相 → 啟動 → 求救」，結束時新人少開 3 個終端機。

成功標準：

- 左側看得出哪個專案在編、哪個需重編、上次編譯何時
- 建置輸出看得到 `{已完成}/{預計}`
- 編譯失敗能用團隊既有的 Agent（Cursor 或 Claude Code）帶上下文求救
- 倉裡留下或更新 `ai-project.json`
- Agent 能用 MCP `stack_status` 讀堆疊，而不是猜

## 0–10 分：安裝與後端

1. 從 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 安裝，或 `dotnet run --project src/AiProject.Console.App`。
2. 按「設定」：
   - Agent 後端選團隊主力（Cursor / Claude Code / Aider / Codex / VS Code / Windsurf / 自訂）。
   - 確認偵測狀態為可用；必要時填 CLI 路徑覆寫。
   - 可選深色外觀。
3. 按「環境體檢」，確認 dotnet、git，以及目前後端。

## 10–30 分：打開真實倉

1. 「選擇專案目錄…」指向多服務倉根（含多個 `.csproj` 或薄工作區）。
2. 若是薄工作區，複製 [`schema/ai-project.example.json`](../schema/ai-project.example.json) 為 `ai-project.json`，填 `productLines`／`services`／`startOrder`。
3. 左側「服務」應出現可啟動項目；「專案」顯示需重編／未建置／最新。
4. 把滑鼠停在「需重編」上看原因（哪個檔比 DLL 新）。這是編譯可信度的核心。

## 30–55 分：編譯可見性

1. 建置選單「編譯過期項目」。
2. 切到「專案」分頁：等待＝灰、編譯中＝主色、完成＝綠、失敗＝紅。
3. 右側建置輸出應顯示例如 `3/12`，摘要列同步。
4. 結束後確認「上次編譯」相對時間；若仍需重編，點列上看觸發檔，而不是再盲編一次。
5. 成功／失敗各會寫入 `.ai_project/build-reports/`。

## 55–70 分：啟動與求救

1. 按「啟動」，看摘要「就緒 N / M」與服務 Log。
2. 若有服務失敗：選該列 →「執行求救」→ 確認。提示已複製；Cursor 會開 New Agent，其他後端依設定開啟。
3. 可故意讓一個專案編譯失敗，走「編譯求救」。

## 70–80 分：接上 MCP（Agentic 閉環）

1. 設定 →「寫入目前專案 .cursor/mcp.json」或複製設定到使用者 MCP。
2. 在 Cursor 問：「用 stack_status 看這個倉哪些服務沒起來。」
3. 確認 Agent 真的呼叫工具，而不是猜。求救提示已要求修完後 `build`／`stack_status`。

細節：[mcp.md](mcp.md)。

## 80–90 分：收尾與落地

1. GitHub 選單看狀態；有髒檔可「提交…」並用「AI 建議」。
2. 把可用的 `ai-project.json` 提交進主分支（這是團隊採用的證據）。
3. 記下三個數字給下一次對照：誤報需重編次數、從開啟到全服務就緒分鐘數、求救到開始改碼是否少開終端機。

## 常見卡住

| 現象 | 處理 |
|------|------|
| 編譯成功仍需重編 | 看列上的原因檔；`appsettings`／`data/*.json` 不應觸發。按「刷新」。 |
| 後端不可用 | 設定裡換已安裝的後端，或填 CLI 路徑。VS Code／Windsurf 是開倉 + 貼上提示。 |
| 沒有服務 | 補 `ai-project.json` 的 `services`，或確認 `launchSettings.json` 有 `applicationUrl`。 |
