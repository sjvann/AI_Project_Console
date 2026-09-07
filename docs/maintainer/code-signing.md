# 程式碼簽署與 Microsoft Store

給控制台維護者。目標：讓 Windows 下載安裝時不再顯示「不明的發行者」，並說明產品穩定後如何上 Microsoft Store。

Microsoft 已將 **Azure Trusted Signing** 更名為 **Azure Artifact Signing**（功能相同）。下列以官方新名稱為準。

## 先看能不能申請

Public Trust 憑證（SmartScreen 看得到的公開發行者）有地區限制，以 [官方 Quickstart](https://learn.microsoft.com/azure/artifact-signing/quickstart) 為準：

| 身分 | 目前可申請 Public Trust 的地區 |
|------|--------------------------------|
| **組織** | 美國、加拿大、歐盟、英國、澳洲、紐西蘭、日本、韓國、新加坡、瑞士、挪威、以色列 |
| **個人開發者** | **僅美國、加拿大** |

台灣個人開發者目前通常**不能**通過 Public Trust 身分驗證。Azure 訂閱所在區域（例如 East US）**不能**繞過這項限制。Private Trust 沒有同樣的地區限制，但只適用於企業內部信任，**無法**消除一般使用者的 SmartScreen 警告。

若身分驗證過不了，打包腳本仍可產生未簽署安裝包；SmartScreen 處理方式見 [常見問題](../user/troubleshooting.md#smartscreen-擋下安裝程式)。替代方案是向傳統 CA（DigiCert、Sectigo、SSL.com 等）購買 OV／EV 程式碼簽署憑證。

開通前請先在 Azure 入口網站確認你的帳單帳戶類型（Individual／Organization）與驗證身分一致，且帳單上的法定名稱、地址會出現在憑證上。

## Azure 開通（Portal）

需要**付費** Azure 訂閱（Basic 方案大約每月固定費 + 額度內簽署次數，以 Azure 定價頁為準）。

1. 登入 [Azure Portal](https://portal.azure.com/)，訂閱 → Resource providers → 註冊 `Microsoft.CodeSigning`。
2. 搜尋 **Artifact Signing Accounts** → Create。帳號名稱 3–24 字元、全域唯一、字母開頭。地區請選你之後要填進 Endpoint 的區域（例如 East US → `https://eus.codesigning.azure.net/`）。
3. 在該帳號 **Access control (IAM)** 指派兩個角色給你自己（入口網站可能仍顯示舊名 Trusted Signing）：
   - **Artifact Signing Identity Verifier**（才能送身分驗證）
   - **Artifact Signing Certificate Profile Signer**（才能實際簽署）
4. **Identity validations** → 依你是組織或個人建立 **Public** 驗證。個人路徑通常是：先選 Organization，下拉改 Individual，再 New Identity → Public。驗證只能在 Portal 完成（政府證件、Authenticator 等），CLI 做不到。核准後才可建憑證設定檔。
5. **Certificate profiles** → Create → 類型選 **Public Trust**，綁定已通過的身分驗證。

Endpoint 必須與帳號所在區域一致，否則簽署會 `403 Forbidden`。對照表見 [Signing integrations](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations)。

## 本機設定

1. 安裝 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 並 `az login`。
2. 安裝 [.NET 8 Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)（dlib 需要；與控制台的 .NET 10 SDK 並存）。
3. 複製範本並填入帳號／設定檔名稱（此檔已 gitignore，不要提交）：

```powershell
copy installer\windows\trusted-signing.example.json installer\windows\trusted-signing.json
```

```json
{
  "Endpoint": "https://eus.codesigning.azure.net/",
  "CodeSigningAccountName": "你的帳號名",
  "CertificateProfileName": "你的憑證設定檔名"
}
```

也可用環境變數（GitHub Actions 走這條）：

| 變數 | 意義 |
|------|------|
| `TRUSTED_SIGNING_ENDPOINT` | 區域 Endpoint URI |
| `TRUSTED_SIGNING_ACCOUNT` | Artifact Signing 帳號名 |
| `TRUSTED_SIGNING_PROFILE` | Certificate profile 名 |
| `TRUSTED_SIGNING_METADATA` | 可選，指向 JSON 檔路徑 |

## 打包時簽署

`scripts/pack-win.ps1` 會：

1. `dotnet publish` 後簽署 `AI_Project_Console.exe`（zip 裡的主程式才有簽章）
2. 編譯 Inno Setup 後簽署 `*-win-x64-setup.exe`（SmartScreen 看這個檔）

未設定 metadata 時預設**略過簽署**並警告，避免擋下現有發版流程。

```powershell
# 有設定就簽；沒設定就警告後繼續
powershell -ExecutionPolicy Bypass -File scripts/pack-win.ps1 -Version 0.6.10

# 正式發行：沒簽成功就失敗
powershell -ExecutionPolicy Bypass -File scripts/pack-win.ps1 -Version 0.6.10 -RequireSign

# 本機試包、不連 Azure
powershell -ExecutionPolicy Bypass -File scripts/pack-win.ps1 -Version 0.6.10 -SkipSign
```

控制台 GitHub 操作台「發行 Release…」會代跑 `pack-win.ps1`。本機已放 `trusted-signing.json` 且已 `az login` 時，操作台發行也會簽署。

驗證：

```powershell
Get-AuthenticodeSignature dist\AI_Project_Console-*-win-x64-setup.exe |
    Select-Object Status, StatusMessage, @{N='Subject';E={$_.SignerCertificate.Subject}}
```

`Status` 應為 `Valid`，Subject 出現通過驗證的名稱，而不是空白。

簽署**不會**保證第一次下載就完全沒有 SmartScreen 提示；聲譽仍可能要累積下載／安裝量。發行者會從「不明」變成真實名稱，這是使用者願意按「仍要執行」的關鍵。

## GitHub Actions（可選）

[`.github/workflows/release-windows.yml`](../../.github/workflows/release-windows.yml) 可在 Windows runner 打包並簽署，產出 artifact（**不會**自動建立 GitHub Release，以免跟操作台發版重複）。

倉庫 Settings → Secrets and variables：

**Secrets**

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

**Variables**

- `TRUSTED_SIGNING_ENDPOINT`
- `TRUSTED_SIGNING_ACCOUNT`
- `TRUSTED_SIGNING_PROFILE`

在 Entra ID 建立 App registration，加上 **Federated credential**（GitHub OIDC：`repo:sjvann/AI_Project_Console:environment:…` 或 `repo:sjvann/AI_Project_Console:ref:refs/heads/main`），並把該 App 指派 **Artifact Signing Certificate Profile Signer**。詳見 [azure/artifact-signing-action OIDC](https://github.com/Azure/artifact-signing-action/blob/main/docs/OIDC.md)。

然後在 Actions 手動跑 **Release Windows**，填版號。下載 artifact 後，仍用操作台或 `gh release upload` 附到 Release。

## 產品穩定後能不能上 Microsoft Store？

**可以。** 控制台是 Photino／Win32 桌面程式，Store 接受這類應用。建議等 UI、更新、授權與隱私政策穩定再送審。

兩條路：

| | **EXE 上架**（較接近現況） | **MSIX 上架**（Store 完整能力） |
|--|---------------------------|----------------------------------|
| 做法 | Partner Center 填安裝檔 HTTPS 網址（可指向 GitHub Releases 的 `*-setup.exe`） | 另做 MSIX 套件提交；Store 代簽、代發更新 |
| 簽署 | 安裝檔必須是 CA 信任的 Authenticode（本頁的 Public Trust 或傳統 OV／EV） | Store 會用 Microsoft 憑證重簽，你不必為 Store 包自行簽 |
| 更新 | Store **不**幫已安裝使用者推更新；可繼續用控制台「檢查更新」 | Store 負責更新；需關閉或改寫現有 GitHub 自動更新，避免兩套互相覆蓋 |
| 安裝程式限制 | 離線單檔、不可下載其他程式、必須支援靜默安裝 | 需通過 Windows App Certification Kit |
| 靜默參數 | `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES`（Inno Setup，已寫在 `setup.iss`） | 由 MSIX 安裝 |

共通前置：

1. [Microsoft Partner Center](https://partner.microsoft.com/dashboard) 開發人員帳戶（個人約一次性 USD 19，公司約 USD 99，以官網為準）。台灣可以註冊。
2. 隱私權政策網址、年齡分級問卷、商店截圖與說明。
3. 安裝到目前使用者、不強制系統管理員：現有 `PrivilegesRequired=lowest` 與 Local AppData 路徑已符合這項。

**建議順序：** 先把 GitHub Releases 的 setup.exe 簽好、SmartScreen 發行者名稱正確 → 產品穩定後用 **EXE 上架** 掛同一支安裝檔（自動更新可維持）→ 若之後要 Store 內購、展示廣告或企業 MDM，再評估 MSIX。

官方說明：[以未封裝 Win32 上架](https://learn.microsoft.com/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store)、[MSI／EXE 套件需求](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msi/upload-app-packages)。
