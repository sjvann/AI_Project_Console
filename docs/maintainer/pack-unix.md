# macOS／Linux 安裝包

給控制台維護者。一般使用者請看 [安裝與第一次使用](../user/getting-started.md)。Windows 安裝包仍見 [發版](release.md)。

沒有 Mac 時，**不要**把本機 Windows 交叉編譯的 zip 當成正式發行檔。Windows 產出的 zip 會丢掉 Unix 執行權限，也不能 ad-hoc 簽署。正式資產一律由 GitHub Actions 的 `macos-latest`／`ubuntu-latest` 打包並驗證。

## 資產（與自動更新檔名對齊）

自動更新依 RID 找檔名含該 RID 且以 `.zip` 結尾的資產（與 Windows 的 `*-win-x64.zip` 同一規則）。

| RID | 檔名 | 內容 |
|-----|------|------|
| `osx-arm64` | `AI_Project_Console-{ver}-osx-arm64.zip` | `AI_Project_Console.app`（Apple Silicon） |
| `osx-x64` | `AI_Project_Console-{ver}-osx-x64.zip` | 同上，Intel Mac |
| `linux-x64` | `AI_Project_Console-{ver}-linux-x64.zip` | 免安裝目錄＋`install.sh`；另可有 `.deb` |
| `linux-arm64` | `AI_Project_Console-{ver}-linux-arm64.zip` | 同上 |

每個 zip／deb 旁有 `.sha256`。

## 在 GitHub 打包（沒有 Mac 時的正確做法）

1. 把含 `scripts/pack-unix.sh` 與 `.github/workflows/pack-unix.yml` 的提交推上遠端。
2. GitHub → Actions → **Pack Unix** → Run workflow。
   - `version`：目前版號（例如 `0.6.16`，不含 `v`）
   - `upload_release`：若 `v0.6.16` 已經存在，勾選後會把 Unix 資產附加到該 Release（不覆寫 `*-win-x64-setup.exe`）
3. 工作流程會：
   - 在 **macOS** runner 為 `osx-arm64`／`osx-x64` 做 `dotnet publish`、組 `.app`、ad-hoc `codesign`、zip
   - 在 **Ubuntu** runner 為 `linux-x64`／`linux-arm64` 做 publish、zip，並用 `dpkg-deb` 產 `.deb`
   - 跑 `scripts/verify-unix-package.sh`（結構、原生庫、SHA256、架構、`--mcp --list-tools`）
4. 未勾選上傳時，從該次 run 的 Artifacts 下載，人工核對後再 `gh release upload`。

```powershell
gh workflow run pack-unix.yml -f version=0.6.16 -f upload_release=true
gh run watch
```

## 驗證清單（沒有 Mac 也能確認「安裝程式結構正確」）

下列在 Actions log 必須全綠，才算這次 Unix 包可附上 Release：

| 檢查 | 誰跑 | 證明什麼 |
|------|------|----------|
| zip 存在且 SHA256 旁檔相符 | macOS／Ubuntu | 檔沒壞 |
| `.app/Contents/Info.plist` 有 `sjvann.AIProjectConsole` 與版號 | macOS | bundle 識別正確 |
| `Contents/MacOS/AI_Project_Console` 是 Mach-O，且 arm64／x86_64 與 RID 相符 | macOS `file` | 不是 Windows 交叉編譯混進去的 ELF |
| `Photino.Native.dylib` 存在，`otool -L` 看得到 WebKit | macOS | Photino 原生庫有進包 |
| 主程式有 Unix 執行權限 | macOS／Linux | zip 不是在 Windows 壓的 |
| ad-hoc `codesign -v` | macOS | 至少有本機簽章（**不是** Developer ID 公證） |
| `--mcp --list-tools` 能印出工具 | 架構相符的 runner | apphost 與 .NET 執行環境可啟動（不開視窗） |
| Linux：ELF 架構、`Photino.Native.so`、`install.sh` | Ubuntu | 可解壓執行 |
| `.deb` 含 `/opt/AI_Project_Console` 與 `.desktop` | Ubuntu `dpkg-deb` | 安裝程式內容正確 |

**這份清單不能證明：** Photino 視窗在真實 Mac／Linux 桌面能畫出來。那需要 WKWebView／WebKitGTK 的 GUI。沒有 Mac 時請請人用下載的 `.app` 走一次：拖到應用程式 → 右鍵打開 → 選專案目錄。Linux 請在有桌面的 Ubuntu 裝 WebKitGTK 後執行。

## 本機 Windows 交叉編譯（只查原生庫，非正式檔）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-unix.ps1 -Version 0.6.16 -Runtime osx-arm64
```

成功只表示 `dotnet publish -r osx-arm64` 有帶出 `Photino.Native.dylib`。產出的 zip **禁止**上傳 Release。

## 未完成：Apple 公證

沒有 Apple Developer ID 憑證與公證（notarytool）時，使用者第一次開啟會看到「無法確認開發者」。文件已寫右鍵打開／`xattr -cr`。這不是安裝包結構錯誤，而是發行者身分未向 Apple 註冊。要當正式 Mac 軟體發行，需另備憑證後在 `macos-latest` 改為 Developer ID 簽署並 `notarytool submit`。在完成前不得把 Mac 包寫成「已公證」。

## 自動更新

macOS／Linux 沒有 Inno Setup。已安裝（`.app` 或 `console-installed.marker`）與 zip 免安裝都下載對應 RID 的 `.zip`，程式結束後覆蓋再重開。Windows 的 `*-setup.exe` 流程不變。
