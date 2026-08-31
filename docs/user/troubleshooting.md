# 常見問題

先按工具列「環境體檢」，再對照本頁。畫面各區說明見 [畫面導覽](interface.md)。

## 程式打不開或視窗是白的

- Windows 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)。系統通常已內建；企業映像若拔掉元件，請向 IT 重裝。
- zip 版請解壓後執行 `AI_Project_Console.exe`，不要在壓縮檔總管裡直接開。
- 從原始碼執行需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。一般使用者請改用 [Releases](https://github.com/sjvann/AI_Project_Console/releases)。

## 沒有服務

左側「服務」是空的，通常是掃描不到可對外的執行項目。

1. 確認選到的是倉**根**，不是某個 `src` 子資料夾（除非你就是只要那一層）。
2. 可執行專案的 `Properties/launchSettings.json` 需有 `applicationUrl`。
3. 薄工作區（根目錄沒有 `.csproj`）請放 `ai-project.json` 並填 `productLines`／`services`。見 [工作區設定](../workspace/ai-project-json.md)。
4. 按「環境體檢」看載入的服務清單與是否已讀到 manifest。

## 剛加的專案沒出現

控制台只在**開啟工作區時**掃一次目錄。方案已開著、Agent 或別人又加了 `.csproj`（或可啟動程式）時，請按專案列 **重新掃描**，或左側「專案」的 **刷新**。摘要列會寫新增／移除了幾個專案與服務。已在跑的服務不會因此停止。

若 `ai-project.json` 已填 `services`，服務列以清單為準；新程式仍會出現在「專案」。要把新程式列入服務，請補進清單後再掃描。

## 服務啟動失敗或一直離線

1. 選該列，右側「服務 Log」從頭看例外與 port 佔用。
2. 列上「失敗」可滑鼠停看短訊。
3. 有 `preStart` 時，腳本失敗就不會啟動行程；Log 會有 `=== preStart ... ===`。
4. 確認本機已裝該專案需要的 SDK（體檢的 `dotnet: OK`）。
5. 仍不明就用 **執行求救**。見 [卡住時怎麼辦](help.md)。

「隨 xxx」的項目沒有獨立行程，請啟動宿主服務。

有 `dependsOn` 時，相依沒就緒會先出現在該列「失敗」與狀態列。軟相依可 Alt＋啟動略過；硬相依失敗則不會起自己。

## 編譯成功仍顯示需重編

1. 把滑鼠停在該列，看「原因」指出的檔案。
2. 按「刷新」。`appsettings`、`data/*.json` 不應觸發。
3. 用控制台「編譯」成功且之後沒改原始碼，應變「最新」。若 DLL 時間戳看起來較舊，以控制台徽章為準。

## 啟動了但前端打不開

- 用服務列「開啟」，不要手打記錯的 port。
- 摘要「就緒」沒到齊時先等健康檢查，或看該服務是否真的 listen。
- `openUrl` 寫錯時請工作區負責人改 `ai-project.json`。

## Agent／求救沒反應

| 現象 | 處理 |
|------|------|
| 設定裡後端不可用 | 換已安裝的後端，或填 CLI 路徑覆寫 |
| Cursor 沒開 New Agent | 確認已裝 Cursor；可改勾「同時開啟」並手動在 Cursor 貼提示 |
| VS Code／Windsurf | 設計就是開資料夾 + 複製提示，請到 Copilot／Cascade 貼上 |
| 自訂命令沒跑 | 檢查 `{root}`／`{promptFile}` 範本與 PATH |

求救不需要雲端控制台帳號。Cursor 等後端若自己要登入，依該產品的方式。

## GitHub 選單不能用

- 開啟已接 GitHub 的專案時會要求登入。沒裝 `gh`：先裝 [GitHub CLI](https://cli.github.com/)。瀏覽器沒跳出：在登入對話框按「在瀏覽器開啟」，或複製一次性代碼後手動打開 https://github.com/login/device。也可到終端機執行 `gh auth login`。
- 任務分頁是空的：確認已登入、倉庫有開放 Issue，並按「刷新」。未指派清單預設可收合。
- 提交需要資料夾是 git 倉，且已安裝 Git。
- 專案列看不到分支：確認目錄是 git 倉。切換分支前必須先提交或還原未提交變更。
- 不能關閉專案／離開：工作區還有未提交變更，或目前分支尚未 push。請先提交並發布。
- 開啟專案後自動同步失敗：看警告內容。工作區不乾淨會略過同步；網路或衝突請到終端機處理後再按 GitHub → 同步。
- 同步／發布／Release／PR 需要完成 GitHub 設定（或從 remote 預填）以及網路。
- Release、PR、Actions 需要 `gh auth login`。
- 私人倉「檢查控制台更新」失敗：登入 `gh` 或設 `GH_TOKEN`。
- 摘要列「文件 無文件／缺骨架」：按文件 → 建立／補齊體系。已有內容不會被覆蓋。
- 網站預覽起不來：環境體檢看 `dotnet`。第一次會 `dotnet tool restore` 安裝 DocFX，需網路。若提示埠被占用，關掉其他 DocFX／本機網站後再試；控制台會改用下一格埠。
- 啟用 GitHub Pages 失敗：登入 `gh`，或到倉庫 Settings → Pages，Source 選 GitHub Actions。
- 線上文件 404：確認已 push 到 `main`，且 Actions 的 Deploy docs 成功。

同步是 `pull --rebase`。衝突請在 IDE／終端機解決。

## 更新失敗或不出現橫幅

- 開發模式（`dotnet run`）不能用「立即更新」覆蓋。
- 橫幅被「稍後再說」略過後，同一版號不會再煩你，直到更新的 tag。
- 可手動按「檢查更新」。仍沒有就打開 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 對照版號。

## MCP 審計是空的

不是故障。代表 Agent 還沒呼叫堆疊工具，或控制台尚未寫入專案 `.cursor/mcp.json`。步驟見 [MCP](../agent/mcp.md)。摘要「MCP 拒絕」是對帳單；政策擋下（例如 `stop_all` 缺少 `confirm=true`）不會變黃。點一下可看原因。

## 專案問答沒有回覆

1. 設定 → 專案問答，確認 Base URL 與模型。
2. 本機 Ollama 需先啟動且已 `pull` 該模型。
3. 必須已開啟專案。雲端端點需填 API key。

## 關了控制台服務還在

這是預期行為。請用「停止全部」或各列「停止」。狀態寫在專案 `.ai_project/`（`logs`、`pids`、`build-reports`、`uat-reports`）。

## 還需要人幫忙時

把這三樣一併提供：環境體檢全文、該服務 Log 尾端、建置輸出（若與編譯有關）。
