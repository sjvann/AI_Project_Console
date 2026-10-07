# 常見問題

先按工具列「環境體檢」，再對照本頁。畫面各區說明見 [畫面導覽](interface.md)。

## SmartScreen 擋下安裝程式

<a id="smartscreen"></a>

從 Releases 下載 `*-win-x64-setup.exe` 時，Windows 可能顯示「已保護您的電腦」，發行者為「不明的發行者」。這是 SmartScreen 對尚未簽署（或聲譽不足）的程式的提示，不是防毒判定為惡意軟體。

若你確認檔案來自本專案的 [GitHub Releases](https://github.com/sjvann/AI_Project_Console/releases)：

1. 在提示中按 **更多資訊**（若看得到），再按 **仍要執行**
2. 或對安裝檔右鍵 → **內容** → 勾選 **解除封鎖** → 套用後再執行

請不要為此關閉 Microsoft Defender SmartScreen。維護者如何用 Azure Artifact Signing 簽署安裝包，見 [程式碼簽署](../maintainer/code-signing.md)。

## macOS Gatekeeper 擋下應用程式

<a id="gatekeeper"></a>

從 Releases 下載的 `.app` **尚未**向 Apple 公證。第一次開啟時系統可能顯示「無法確認開發者」。這不是防毒判定。

若你確認檔案來自本專案的 [GitHub Releases](https://github.com/sjvann/AI_Project_Console/releases)：

1. 對 `AI_Project_Console.app` **右鍵 → 打開**，再按打開
2. 或在終端機執行 `xattr -cr /Applications/AI_Project_Console.app` 後再開啟

請不要為此關閉 Gatekeeper。公證完成前這段流程不會消失。

## 程式打不開或視窗是白的

- Windows 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)。系統通常已內建；企業映像若拔掉元件，請向 IT 重裝。
- macOS 請用對應晶片的 zip（Apple Silicon 用 `osx-arm64`），並依 [Gatekeeper](#gatekeeper) 第一次打開。
- Linux 需 GTK 3 與 WebKitGTK 4.1（`libwebkit2gtk-4.1-0`）。請用與 `uname -m` 相符的套件，並執行 `./install.sh` 或 `sudo apt install ./….deb`。只印出 apt 指令就結束的舊安裝程式，見 [Linux 安裝](#linux-install)。zip 請解壓後再執行，不要在壓縮檔瀏覽器裡直接開。
- Windows zip 版請解壓後執行 `AI_Project_Console.exe`，不要在壓縮檔總管裡直接開。
- 從原始碼執行需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。一般使用者請改用 [Releases](https://github.com/sjvann/AI_Project_Console/releases)。

## Linux：格式錯誤，或找不到 ai-project-console

<a id="linux-install"></a>

`./AI_Project_Console` 顯示「無法執行二進位檔案：可執行檔格式錯誤」時，這包的處理器與這台電腦不同。執行 `uname -m`：`x86_64` 改下 `linux-x64`；`aarch64` 改下 `linux-arm64`。

`ai-project-console：無此指令` 是因為 zip 安裝把命令放在 `~/.local/bin`，而目前這個終端機的 PATH 還沒有它。新開一個終端機，或執行：

```bash
export PATH="$HOME/.local/bin:$PATH"
```

若接著仍是格式錯誤，裝進去的仍是錯的架構。請刪掉錯的下載目錄，改裝相符的套件。Ubuntu 建議：

```bash
sudo apt install ./AI_Project_Console-*-linux-x64.deb
```

視窗打不開、並提到 `libwebkit2gtk`：Photino 4 需要 WebKitGTK 4.1（`libwebkit2gtk-4.1-0`）。

```bash
sudo apt update
sudo apt install libwebkit2gtk-4.1-0 libnotify4 libgtk-3-0t64 || sudo apt install libwebkit2gtk-4.1-0 libnotify4 libgtk-3-0
```

`apt update` 之後裝到的是這個 Ubuntu 套件庫裡的最新版。Photino 4 使用 WebKitGTK 4.1（`libwebkit2gtk-4.1-0`）。

較新的 `./install.sh` 與 `.deb` 會在安裝時做完架構核對、依賴與 PATH。若畫面上只出現 apt 提示並且寫著已安裝，用上面的指令把這一次補完。

## 沒有服務

左側「服務」是空的，通常是掃描不到可對外的執行項目。

1. 確認選到的是倉**根**，不是某個 `src` 子資料夾（除非你就是只要那一層）。
2. 可執行專案的 `Properties/launchSettings.json` 需有 `applicationUrl`。
3. 薄工作區（根目錄沒有專案檔）請放 `ai-project.json` 並填 `productLines`／`services`。見 [工作區設定](../workspace/ai-project-json.md)。
4. 按「環境體檢」看載入的服務清單與是否已讀到 manifest。

## 剛加的專案沒出現

控制台只在**開啟工作區時**掃一次目錄。方案已開著、Agent 或別人又加了專案檔（或可啟動程式）時，請按專案列 **重新掃描**，或左側「專案」的 **刷新**。摘要列會寫新增／移除了幾個專案與服務。已在跑的服務不會因此停止。

若 `ai-project.json` 已填 `services`，服務列以清單為準；新程式仍會出現在「專案」。要把新程式列入服務，請補進清單後再掃描。

外掛用的 `package.json`（`name` 是物件，或有 `entryDll`）以及檔頭是 PEP 723 的腳本（即使檔名是 `setup.py`）不會被當成專案，也不會出現在「還原套件」。

## 還原套件

環境體檢裡的「還原套件」依專案檔執行 `npm install`、`pip install` 等。Python 專案會先建立 `.venv`，再在那個環境安裝 `requirements.txt`，或對有 `pyproject.toml`／`setup.py` 的目錄做可編輯安裝。體檢以 `.venv` 是否存在判斷套件已還原。

## 服務啟動失敗或一直離線

1. 選該列，右側「服務 Log」從頭看例外與 port 佔用。
2. 列上「失敗」可滑鼠停看短訊。
3. 有 `preStart` 時，腳本失敗就不會啟動行程；Log 會有 `=== preStart ... ===`。
4. 確認本機已裝該專案需要的 SDK（體檢「開發環境」應對應語言顯示已安裝）。缺少時按安裝；控制台會用 winget 或開啟官方下載頁。
5. 仍不明就用 **執行求救**。見 [卡住時怎麼辦](help.md)。

「隨 xxx」的項目沒有獨立行程，請啟動宿主服務。

有 `dependsOn` 時，相依沒就緒會先出現在該列「失敗」與狀態列。軟相依可 Alt＋啟動略過；硬相依失敗則不會起自己。

## 編譯或測試之後才發現資料庫沒起來

工作區的資料庫若跑在 Podman 虛擬機裡，在 `ai-project.json` 宣告 `runtimes` 與 `datastores`（見 [工作區設定](../workspace/ai-project-json.md)）。宣告之後：

- 啟動列的 **Podman** 按鈕與摘要列會寫「Podman 虛擬機未啟動：機器名」，或「虛擬機已啟動，資料庫未就緒」。有幾筆問題看按鈕上的數字，滑鼠停上可看句子。**環境體檢** 按鈕同樣用筆數與提示列出缺少與不足。
- 「跑測試」與被 `requiredBy` 點名的服務啟動會在這之前停下。
- 「編譯過期項目」仍可編。控制台不會自動執行 `podman machine start`。

虛擬機已啟動時，在終端機啟動資料庫容器後再試。摘要列那一句消失，測試與啟動才會放行。

## 編譯成功仍顯示需重編

1. 把滑鼠停在該列，看「原因」指出的檔案。
2. 按「刷新」。`appsettings`、`data/*.json` 不應觸發。
3. 用控制台「編譯」成功且之後沒改原始碼，應變「最新」。若 DLL 時間戳看起來較舊，以控制台徽章為準。

## 啟動了但前端打不開

- 用服務列「開啟」，不要手打記錯的 port。
- 列上停在「開啟中…」時，開啟／啟動應是停用的；若還能按，把控制台重開一次再試。開啟結束後應自動清掉，不要無限轉。
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

## GitHub 操作台不能用

- 標題列右側「GitHub 操作台」應為綠框（需求台也看得到）。點了沒有抽屜：先關掉目前的登入對話框，或按 **Ctrl+G**、點標題列帳號燈。從原始碼執行時請關掉舊視窗後重新 `dotnet run`，確認標題列版號與這次編譯一致。
- 開啟已接 GitHub 的專案時會要求登入。沒裝 `gh`：先裝 [GitHub CLI](https://cli.github.com/)。瀏覽器沒跳出：在登入對話框按「在瀏覽器開啟」，或複製一次性代碼後手動打開 https://github.com/login/device。也可到終端機執行 `gh auth login`。
- 登出出現 `unable to determine which account`：同一主機有多個 `gh` 帳號。操作台會登出標題列那個帳號。若仍失敗，在終端機執行 `gh auth logout --hostname github.com --user 帳號`（企業主機改成該主機名稱）。
- 任務清單是空的：開 GitHub 操作台，確認已登入、倉庫有開放 Issue，並按「刷新」。未指派清單預設可收合。
- 提交需要資料夾是 git 倉，且已安裝 Git。
- 提交出現 `index.lock`／「Another git process」：通常是控制台刷新狀態與提交重疊，或 IDE 也在跑 git。關掉錯誤視窗再提交一次即可；控制台會清殘留鎖檔並重試一次。
- 專案列看不到分支：確認目錄是 git 倉。切換分支前必須先提交或還原未提交變更。
- 不能關閉專案／離開：工作區還有未提交變更，或目前分支尚未 push。請先提交並發布；若仍要走，警示視窗可按「強行關閉」。
- 開啟專案後自動同步失敗：看警告內容。工作區不乾淨會略過同步；網路或衝突請到終端機處理後再按 Pulse「同步」或操作台日常節奏裡的同步。
- 同步／發布／Release／PR 需要完成 GitHub 設定（或從 remote 預填）以及網路。
- 「建立 PR」出現 `argument required when using the --repo flag`：舊版會在 `gh pr view` 加上 `--repo`，這版 GitHub CLI 會誤報。關掉控制台後重開即可；讀 PR 改從目前 git 倉推斷。
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
- 「立即更新」若打開 GitHub 頁而不是安裝程式：最新 Release 缺少你這個平台的安裝檔（Windows 要 `*-win-x64-setup.exe`；macOS／Linux 要檔名含目前 RID 的 `.zip`）。Windows 請用 GitHub 操作台「發行 Release…」補上；macOS／Linux 請跑 Pack Unix 工作流程。
- 發行進度停在「編譯 Windows 執行檔」：這一步可能要 1–3 分鐘，視窗會顯示已過時間與紀錄。找不到 Inno Setup 會立刻失敗並附上安裝連結。
- 可到設定按「檢查更新」（會問是否包含 RC）。仍沒有就打開 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 對照版號。也可「從檔案更新…」選已下載的 setup／zip。

## MCP 審計是空的

不是故障。代表 Agent 還沒呼叫堆疊工具，或控制台尚未寫入專案 `.cursor/mcp.json`。步驟見 [MCP](../agent/mcp.md)。摘要「MCP 拒絕」是對帳單；政策擋下（例如 `stop_all` 缺少 `confirm=true`）不會變黃。點一下可看原因。

## 專案問答沒有回覆

1. 設定 → 專案問答，點「測試連線」。成功會顯示「來源正常」，且目前模型要在可用清單裡。
2. 本機 Ollama 需先啟動。還沒有模型時，在設定按 **pull llama3.2**（或 `qwen2.5`）。Gemma 只能聊天，測試會被擋下。連不上時先開 `ollama serve`。
3. 必須已開啟專案。雲端端點需填 API key。可點來源範本帶入網址，或用「記住目前」保存多組來源。

## 關了控制台服務還在

這是預期行為。請用「停止全部」或各列「停止」。狀態寫在專案 `.ai_project/`（`logs`、`pids`、`build-reports`、`uat-reports`、`product-purposes.md`）。

## 開兩個控制台，選專案時兩個一起變

請更新到含多視窗隔離的版本。每個視窗有自己的 WebView 資料夾；後開的視窗選專案不應改到先開的那個。第二個視窗啟動時不會自動還原上次專案。

帶起 Cursor 時也不會把另一個方案的視窗換成目前資料夾：每個控制台對準自己的 Cursor 視窗。離開時若選擇關閉 IDE，只關這個專案的視窗。

## 從原始碼開第二個控制台建置失敗（檔案被鎖定）

程式**支援**多開。失敗是因為第一個 `dotnet run` 鎖住了 `bin\Debug` 的 DLL，第二次還要複製覆寫。第二個請：

```powershell
dotnet run --no-build --project src/AiProject.Console.App
```

安裝包啟動的 exe 沒有這個限制，再開一份即可。

## 還需要人幫忙時

把這三樣一併提供：環境體檢全文、該服務 Log 尾端、建置輸出（若與編譯有關）。
