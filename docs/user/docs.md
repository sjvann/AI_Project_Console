# 專案文件

控制台管的是**目前開啟專案**的 `docs/`，不是控制台自己的說明站。規定：專案文件都放這個目錄，Markdown 撰寫，用 DocFX 建靜態站，可發到 GitHub Pages。

不會因為文件不齊就擋你關閉專案或離開。摘要列會提醒。

## 狀態

摘要列「文件」式片：

| 顯示 | 意思 |
|------|------|
| 無文件 | 還沒有 `docs/` |
| 缺骨架 | 有 md，但缺 `toc.yml` 或 `docfx.json` |
| 待補 N | 骨架在，仍有「待補」占位 |
| 就緒 | 骨架齊、沒有待補 |

點式片會打開左側「文件」與右側編輯。環境體檢也會列出 docs／DocFX／Pages workflow。

## 建立體系

工具列 **文件 → 建立／補齊體系…**（或左側「建立體系」）會寫入缺的檔，**不覆蓋**已有內容：

- `docs/README.md`、`toc.yml`、`docfx.json`
- `docs/user/`、`product/`、`engineering/`、`operations/` 的標準頁
- 若缺：`.github/workflows/docs.yml`、`.config/dotnet-tools.json`（鎖定 DocFX）

每頁有 title 與「待補」。控制台會帶入專案名稱、服務清單、倉根 README 摘錄。

## 編輯與預覽

1. 左側「文件」是 `docs/` 目錄樹：資料夾可開合，點檔案才到右側編修。可用「新增文件」在目錄下建立 `.md`。
2. 右側「文件」用 Markdown 編輯；勾選「本頁預覽」只看**目前這頁**轉出的 HTML（不是網站）。
3. **儲存**只寫 `docs/` 內該相對路徑。
4. **網站預覽本頁**會跑 `dotnet docfx … --serve`，用瀏覽器開 DocFX 站的**目前這頁**。若選的是設定檔（`toc.yml`／`docfx.json`）則開首頁。埠被占用會改用下一格。關閉專案時會停掉。

倉根 `README.md` 不搬；文件地圖會連回去。

## AI 補齊

**文件 → AI 補齊…** 把待補清單與專案摘要交給你選的 Agent。右側「AI 補齊本頁」只處理目前這檔。Agent 改完後按左側「刷新」。

## GitHub Pages

文件跟 git 走，用既有提交／發布：

1. **啟用 GitHub Pages**：用 `gh` 把倉庫 Pages 設成 GitHub Actions。失敗就到 GitHub → Settings → Pages，Source 選 GitHub Actions。
2. **同步到 GitHub Pages（push）**：推目前分支。推到 `main` 後 workflow 會建置 `docs/docfx.json` 並部署。
3. **開啟線上文件**：開 `https://{owner}.github.io/{repo}/`。

本機預覽與線上是同一套 Markdown。

## 需要的工具

| 項目 | 用途 |
|------|------|
| .NET SDK | `dotnet tool restore`、`dotnet docfx` |
| Git | 提交與 push |
| GitHub CLI `gh` | 啟用 Pages、看 Actions |

沒有 DocFX 全域安裝也可以：骨架會放 tools manifest，本機與 CI 都用 `dotnet tool restore`。
