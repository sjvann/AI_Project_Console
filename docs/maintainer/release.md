# 發版

給控制台維護者。一般使用者請用 [Releases](https://github.com/sjvann/AI_Project_Console/releases) 下載，並用標題列「檢查更新」。

完整指令（改版號、打包、`gh release create`、圖示）寫在 [`scripts/README.md`](../../scripts/README.md)。摘要：

1. 同步改 `AppInfo.Version`、App 專案、`setup.iss`、`pack-win.ps1` 的版號。
2. 提交並推送 `main`（**不可覆寫舊 tag**，已安裝使用者依 latest 更新）。
3. `scripts/pack-win.ps1` 產生 `dist\` 的 setup.exe 與 zip。
4. `gh release create` 附上兩個資產。

本機打包前置：.NET 10 SDK、[Inno Setup 6](https://jrsoftware.org/isinfo.php)、已登入的 GitHub CLI。
