# 安裝與第一次使用

目標：在 15 分鐘內把控制台裝好、打開一份專案、確認環境可用。

## 控制台能做什麼

AI_Project 控制台是本機桌面程式。你選一個專案目錄後，它會：

1. 掃出可啟動的服務與可編譯的專案
2. 一鍵啟動／停止、看 Log、開網頁
3. 告訴你哪些專案「需重編」
4. 幫忙提交到 GitHub
5. 卡住時把說明交給本機 Agent（預設是 Cursor）
6. 在 `docs/` 建立文件骨架、本機預覽，並可發到 GitHub Pages

它**不會**把你的程式碼上傳到控制台自己的雲端。Agent 也是跑在你電腦上的 IDE／CLI。

## 你的電腦需要什麼

| 項目 | 誰需要 | 說明 |
|------|--------|------|
| Windows 10／11（64 位元） | 所有人 | 目前正式安裝包是 Windows x64 |
| [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | 所有人 | Win10／11 通常已內建；若視窗打不開再裝 |
| [.NET SDK](https://dotnet.microsoft.com/download) | 要啟動／編譯 .NET 服務時 | 控制台本身已內含執行環境；**被管理的專案**仍需要 `dotnet` |
| [Git](https://git-scm.com/) | 要用 GitHub 選單時 | 提交、同步、clone |
| [GitHub CLI `gh`](https://cli.github.com/) | GitHub 管理的專案、任務 Issue、發行 Release、建 PR | 開啟這類專案時控制台會要求登入；也可先執行 `gh auth login` |
| Cursor 或其他 Agent | 要用「求救」時 | 見 [Agent 後端](../agent/backends.md) |

按工具列「環境體檢」可一次核對 `dotnet`、`git`、`gh` 與目前 Agent。

## 安裝

### 方式 A：安裝程式（建議）

1. 打開 [Releases](https://github.com/sjvann/AI_Project_Console/releases)
2. 下載 `AI_Project_Console-*-win-x64-setup.exe`
3. 執行安裝程式。會裝到目前使用者的 Local AppData，並加入開始選單；可選桌面捷徑
4. 從開始選單開啟「AI_Project 控制台」

### 方式 B：免安裝壓縮包

1. 下載 `AI_Project_Console-*-win-x64.zip`
2. 解壓到固定資料夾（不要放會被清掉的暫存目錄）
3. 執行 `AI_Project_Console.exe`

兩種方式都能用標題列「檢查更新」。安裝版會用安裝程式覆蓋；zip 版會下載新壓縮包後覆蓋檔案。

### 從原始碼執行

給要改控制台本身的人：[開發與維護](../maintainer/develop.md)。一般使用者請用 Releases。

## 第一次啟動

1. 開啟控制台。啟動時**沒有**預設專案。
2. 用其中一種方式打開工作區：
   - **選擇專案目錄…** — 指向本機已有的倉根
   - **歷史專案** — 下拉選最近開過的
   - **從 GitHub 開啟…** — 輸入 `owner/repo` 或網址，clone 後開啟（見 [GitHub](github.md)）
3. 若目錄已接上 GitHub，會先請你登入（需已安裝 `gh`）。登入後標題列會顯示帳號，左側「任務」可看指派給你的 Issue。接著會自動從遠端同步目前分支；專案名稱旁可看到並切換分支。
4. 勾選「啟動時還原上次專案」，下次會自動載入。
5. 按「環境體檢」，確認 `dotnet`、`git` 為 OK。Agent 若顯示不可用，先到「設定 → Agent」換已安裝的後端，或暫時不求救也能啟動服務。

標題列下方若出現新版本橫幅，見 [更新](#更新)。

## 選哪個資料夾？

選**你要一次管起來的根目錄**：

| 情況 | 選這個 |
|------|--------|
| 單一 git 倉，裡面有多個 `.csproj` | 該倉的根目錄 |
| 薄工作區（這個資料夾本身沒有 `.csproj`，隔壁才有產品線） | 工作區根，並準備 `ai-project.json`（見 [工作區設定](../workspace/ai-project-json.md)） |
| 只想開遠端倉 | 「從 GitHub 開啟…」，再選 clone 到哪個父目錄 |

選完後：

- 左側「服務」應出現可啟動項目
- 左側「專案」列出 `.csproj` 與編譯狀態
- 左側「任務」列出指派給你的 GitHub Issue（需登入）
- 若服務是空的：確認有 `launchSettings.json` 的 `applicationUrl`，或請工作區負責人放 `ai-project.json`

## 第一次建議做的三件事

1. 左側切到「專案」，看有沒有「需重編」。有的話用工具列 **建置 → 編譯過期項目**。
2. 按 **啟動**。摘要列「就緒 N / M」會上升；服務列變成「線上」。
3. 用 **開啟前端** 在瀏覽器看畫面。失敗時選該服務，右側「服務 Log」看原因。

下一步：[畫面導覽](interface.md)、[日常操作](daily-use.md)。

## 更新

啟動約 1.5 秒後會查詢 GitHub Releases。有新版時標題列下方出現橫幅。

| 你的安裝方式 | 按「立即更新」會怎樣 |
|--------------|----------------------|
| 安裝程式（資料夾內有 `unins*.exe`） | 下載 setup、靜默裝到目前目錄、重開控制台 |
| zip 免安裝 | 下載 zip，程式結束後覆蓋並重開 |
| `dotnet run`／從原始碼 | 不能覆蓋開發目錄；請 `git pull` 後重編，或改用 Releases |

也可按標題列「檢查更新」，或 GitHub 選單「檢查控制台更新」。按「稍後再說」會略過該版號，直到出現更新的 tag。背景檢查最多每 6 小時一次。

私人倉庫的 latest 查詢需要已登入的 `gh`，或環境變數 `GH_TOKEN`。
