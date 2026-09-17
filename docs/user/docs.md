# 專案文件

控制台管的是**目前開啟專案**的 `docs/`（或 `Docs/`），不是控制台自己的說明站。規定：專案文件都放這個目錄，用 Markdown 撰寫，用 [DocFX](https://dotnet.github.io/docfx/) 建靜態站，可發到 GitHub Pages。

文件不齊**不會**擋你關閉專案或離開（離開仍會看 git 是否乾淨並警示，見 [GitHub](github.md)）。摘要列會提醒。畫面位置見 [畫面導覽](interface.md)。

控制台自己的使用文件也在倉庫 `docs/`，線上版：<https://sjvann.github.io/AI_Project_Console/>。那是給控制台使用者看的；你在自己產品倉裡按「建立體系」，寫的是**那份產品**的說明。

## 建議流程

1. 選好專案後看摘要列「文件」。無文件或缺骨架時，工具列 **文件 → 建立／補齊體系…**（或左側「建立體系」）。
2. 切到「文件」頁籤，點一個 `.md`，右側改內容後按 **儲存**。
3. 仍標「待補」的頁可用 **AI 補齊本頁**，或工具列 **文件 → AI 補齊…** 一次處理清單。Agent 改完後按左側「刷新」。
4. 要給別人看整站：右側 **網站預覽本頁**（或工具列「本機網站預覽」）。
5. 要公開：先 **啟用 GitHub Pages**，再 **同步到 GitHub Pages（push）**。推到 `main` 後 Actions 會建置並部署。

## 狀態

摘要列「文件」式片（點一下切到「文件」頁籤與右側編輯）：

| 顯示 | 意思 |
|------|------|
| 無文件 | 還沒有 `docs/`，或裡面還沒有可掃到的 Markdown／DocFX 設定 |
| 缺骨架 | 有 md，但缺 `toc.yml`（或 `toc.yaml`）或 `docfx.json` |
| 待補 N | 骨架在（有 toc 與 docfx），仍有骨架那種全形括號占位句 |
| 就緒 | 骨架齊、沒有上述占位句 |

不是「就緒」時式片是黃色。左側分頁標題旁的數字是掃到的檔案數（`.md`／`.yml`／`.json`）。左側提示句會對應上述狀態。

判定只認骨架寫入的占位句（全形括號包著「待補」）。說明文提到摘要列的「待補」不會算。按左側「刷新」會重掃磁碟，底部工作訊息會顯示目前狀態。

## 工具列「文件」

| 項目 | 作用 |
|------|------|
| **建立／補齊體系…** | 寫入缺的骨架與設定，**不覆蓋**已有內容。會先確認 |
| **開啟 docs 資料夾** | 用檔案總管打開專案的 `docs/`（沒有則建立空目錄） |
| **AI 補齊…** | 把待補清單與專案摘要交給你選的 Agent |
| **本機網站預覽（目前這頁）** | 啟動 DocFX `--serve`，瀏覽器開目前這頁（設定檔則開首頁） |
| **開啟線上文件** | 開 GitHub Pages 網址（需 GitHub 設定） |
| **啟用 GitHub Pages** | 用 `gh` 把倉庫 Pages 設成 GitHub Actions（需 GitHub 設定） |
| **同步到 GitHub Pages（push）** | 推目前分支；推到 `main` 後 workflow 會發佈。會先確認 |

後三項需要已完成 GitHub 設定（owner／repo）。見 [GitHub](github.md)。

## 建立體系

工具列 **文件 → 建立／補齊體系…**（或左側「建立體系」）會寫入缺的檔，**不覆蓋**已有非空檔：

- `docs/README.md`、`toc.yml`、`docfx.json`
- `docs/user/`、`product/`、`engineering/`、`operations/` 的標準頁
- 若缺：`.github/workflows/docs.yml`、`.config/dotnet-tools.json`（鎖定 DocFX 2.78.5）、`docs/product/intake.json`（需求進件空殼）

新頁有 YAML `title`，第一句是全形括號占位。控制台會帶入專案名稱、目前掃到的服務與編譯專案（含一句用途）、倉根 `README.md` 前段摘錄。倉根 `README.md` **不會**搬進 `docs/`；文件地圖會連回去。各專案用途的執行期清單在 `.ai_project/product-purposes.md`。

已齊全時會提示「文件體系已存在，未覆蓋任何檔案。」

## 左側：目錄

左側「文件」是 `docs/` 目錄樹（略過 `_site`、`api`、`obj`、`bin`、`.git`）：

- 資料夾可開合；旁邊的「新增」在**該資料夾**下建立頁面。
- 點檔案才載入右側。待補頁會標「待補」。檔案旁的「刪除」會先確認再移除本機檔。
- **建立體系**、**新增文件**、**刪除**、**刷新**：刷新會重掃磁碟。Agent 在外面改檔後按這個。工具列「刪除」作用於目前選中的檔。
- 尚未選專案時會提示先選目錄。沒有檔時提示按「建立體系」。

**新增文件**（工具列或資料夾「新增」）會跳出對話框：

1. 路徑相對於 `docs/`，例如 `user/new-page.md`。沒寫副檔名會自動加 `.md`。
2. 只能新增 `.md`。資料夾不存在會自動建立。
3. **建立並編修**後右側打開該檔。若路徑已有內容則開啟既有檔，不覆蓋。
4. 新檔範本含 `title` 與一句全形括號占位。

**刪除**（工具列、檔案列或右側編輯列）會先確認，再刪 `docs/` 內該檔。只動本機磁碟，不 commit。刪的是空資料夾裡最後一個檔時，空資料夾會一併拿掉。骨架檔（例如 `toc.yml`）也可刪，之後可用「建立體系」再產生。不能刪到 `docs/` 外面。

控制台只能讀寫 `docs/` 內的 `.md`、`.yml`／`.yaml`、`.json`，不能寫到目錄外。

切換檔案、新增、或按 AI 補齊時，若右側有未儲存修改，會先問要不要放棄。

## 右側：編輯與預覽

1. 標題顯示目前相對路徑；有改過會標「未儲存」。**儲存**只寫 `docs/` 內該路徑（UTF-8、無 BOM）。沒改過時儲存鈕不可用。
2. 勾選 **本頁預覽**：在右側看**目前這頁** Markdown 轉出的 HTML。這不是整站，也還沒跑 DocFX。
3. **網站預覽本頁**（預覽已在跑時變成「開啟本頁網站」）：執行 `dotnet tool restore`（若有 tools manifest）再 `dotnet docfx … --serve`，用瀏覽器開 DocFX 站的**目前這頁**。選的是 `toc.yml`／`docfx.json` 等設定檔則開首頁。
4. 預設埠 `127.0.0.1:8080`。被占用會改用下一格（往後試約 20 個）。都占用時請關掉其他 DocFX／本機網站再試。
5. 第一次 restore／建置可能要等；逾時（約 3 分鐘）會提示稍後再按。關閉專案或卸下工作區時會停掉預覽行程。

本機預覽與線上是同一套 Markdown。控制台自己的 DocFX 進階設定（API 頁、根目錄 `docfx.json`）見 [文件網站（DocFX）](../maintainer/docfx.md)。

## AI 補齊

| 入口 | 範圍 |
|------|------|
| 工具列 **文件 → AI 補齊…** | 待補清單與專案摘要（名稱、服務、編譯專案、README 摘錄、GitHub `owner/repo`） |
| 右側 **AI 補齊本頁** | 只處理目前這檔（需已選檔） |

送出前會預覽提示，確認後交給你選的 Agent（與 [卡住時怎麼辦](help.md) 同一套後端）。Agent 在工作區改檔；控制台不會自動寫入。改完按左側「刷新」再看狀態。

規定（提示裡會寫）：只改文件、保留 front matter 的 title、刪掉全形括號占位句、不知就標尚未確認、不要改程式碼、不要 commit。

## GitHub Pages

文件跟 git 走，用既有提交／發布，沒有另一套上傳：

1. **啟用 GitHub Pages**：需要 [GitHub CLI `gh`](https://cli.github.com/) 已登入。控制台會補上 workflow 與 tools manifest（若缺），再把倉庫 Pages 的 Source 設成 GitHub Actions。失敗就到 GitHub → Settings → Pages，Source 選 GitHub Actions。
2. **同步到 GitHub Pages（push）**：推**目前分支**。推送前會再確認缺的 workflow／manifest。推到 `main` 後，`.github/workflows/docs.yml` 會 `dotnet tool restore`、建置 `docs/docfx.json`，並把 `docs/_site` 部署出去。對 `main` 的 Pull request 只建置、不發佈。也可在 Actions 手動跑「Deploy docs」。
3. **開啟線上文件**：開 `https://{owner}.github.io/{repo}/`（owner 小寫）。尚未啟用時會盡量推測網址或提示先設定。

骨架寫入的 workflow 假設文件設定在 `docs/docfx.json`。若你的倉把 `docfx.json` 放在倉根（控制台本倉就是），請以該倉實際 workflow 為準，不要照抄骨架路徑。

## 環境體檢與需要的工具

工具列「環境體檢」在已選專案時有「文件」區塊：

| 項目 | 顯示 |
|------|------|
| docfx | 已安裝／此專案或控制台的 `dotnet` 本機工具／缺少 |
| docs/ | 與摘要列相同的狀態，以及檔案數 |
| Pages workflow | `.github/workflows/docs.yml` 有或無 |

缺少 DocFX、且已有 .NET SDK 時，體檢可按 **安裝 DocFX**：寫入 `.config/dotnet-tools.json` 並執行 `dotnet tool restore`（需網路）。尚未選專案時，文件檢查與安裝會等你先選目錄。

| 項目 | 用途 |
|------|------|
| [.NET SDK](https://dotnet.microsoft.com/download) | `dotnet tool restore`、`dotnet docfx`、本機預覽 |
| [Git](https://git-scm.com/) | 提交與 push |
| [GitHub CLI `gh`](https://cli.github.com/) | 啟用 Pages、看 Actions、開啟線上文件 |

沒有 DocFX 全域安裝也可以：骨架會放 tools manifest，本機與 CI 都用 `dotnet tool restore`。預覽失敗、Pages 404 見 [常見問題](troubleshooting.md)。
