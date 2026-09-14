---
title: 公開回報契約
---

# 公開回報契約

**規劃**。公司工作區（P2）對外接收工程師貢獻的 HTTPS 契約。**任何符合格式的用戶端都可以送**，不限 AI_Project 控制台。衝突時以 [總規格・四套產品](spec.md#四套產品) 為準。

控制台是第一個符合的用戶端，不是唯一用戶端。

## 為什麼要有公開契約

公司端要合併各工程師在「這個專案」上的時間與解決的 Issue。若只認自家桌面套件，外包、工作室、第三方工時工具都進不來。因此接收口必須是**文件化、可版本化的契約**，而不是控制台私有協定。

## 國際標準：採用／對映／不實作

沒有「工程師貢獻回報」的單一 ISO 檔案格式。本契約採**語意對映**，第一刀出 JSON OpenAPI profile，不強制對方實作完整 XML／RDF。

| 標準 | 角色 | 本契約怎麼用 |
|------|------|--------------|
| **ISO 8601 / RFC 3339** | 日期與時間 | 工作日、時段起迄、時區偏移一律用此格式 |
| **HR-XML TimeCard**（HR-XML Consortium） | 工時語意 | 對映 reported resource、period、interval、assignment、approver。**不**強制 HR-XML XML 傳輸 |
| **CHAOSS Types of Contributions** | 貢獻分類詞彙 | `code`、`review`、`issue`、`test`、`docs` 等；是分類，不是傳輸格式 |
| **OASIS OSLC Change Management** | 工作項語意 | Issue／變更請求對齊 change request 概念。第一刀用 GitHub `owner/repo` + number，**不**實作 RDF |
| **OMG ReqIF / OSLC RM / ISO/IEC/IEEE 29148** | 需求工程 | 屬 [系統分析輔助](analysis.md)（P3），不在本契約範圍 |

禁止把「我們自創、無人對映的私有 XML」當標準。擴充欄走 `extra` 並文件化；禁止夾帶原始碼欄位。

## 契約範圍

### 必備能力

| 端點（語意） | 用途 |
|--------------|------|
| `GET /api/v1/me` | **握手**：確認呼叫者可對此租戶申報 |
| `POST /api/v1/timesheets/upload` | 上傳時段＋狀態圖＋ Issue 參照 |
| `GET /api/v1/me/assignments` | 只讀：這家公司認為你這週在哪些專案 |
| `GET /api/v1/me/payslip` | 只讀：薪資條（可後期） |

版本以標頭 `X-Company-Api-Version`（或同等）宣告。實作時再定正式 OpenAPI；本頁約束**語意與欄位**。

### 認證

| 方式 | 誰用 |
|------|------|
| GitHub 權杖／OAuth，對應 `github:{login}` | 控制台與個人工程師 |
| 公司核發的 API 金鑰（對應 `personId` 或外包窗口） | 第三方工具、腳本、CI |

握手失敗必須回人話（例如「名冊沒有此人／待歸戶」），不是只有 401。

### 上傳欄位（JSON profile，對映 HR-XML）

以現有 `TimesheetUploadRequest` 為種子，擴成公開契約：

| 欄位 | 必填 | HR-XML／標準對映 | 說明 |
|------|------|------------------|------|
| `localSlotId` | 是 | TimeInterval ID | 用戶端時段冪等鍵；已核准不可默默覆蓋 |
| `workDate` | 是 | Period / date（ISO 8601 date） | 工作日 |
| `hours` | 是 | TimeInterval quantity | 十進位小時；亦可另提供 ISO 8601 duration |
| `project` | 是 | Assignment | 見下「專案識別」 |
| `issues` | 否 | Change request 參照 | 見下「工作項」 |
| `contributionTypes` | 否 | CHAOSS | 字串陣列，如 `["code","issue"]` |
| `chart` | 否 | （本產品延伸） | 執行狀態圖格子 |
| `isCorrection` / `correctsLocalSlotId` | 否 | 更正鏈 | 只能新增更正，不可改已核准列 |
| `onBehalfOfGitHubLogin` | 否 | Reported resource | 外包窗口代送 |
| `reportedAt` | 建議 | 時間戳（RFC 3339） | 用戶端送出時間 |
| `timeZone` | 建議 | | 預設採用租戶公司時區 |

**專案識別（不可只認內部 Guid）：**

用戶端至少提供下列之一，伺服器解析成內部 `projectId`：

- `projectId`（UUID，既有）
- `projectCode`（公司對外專案碼）
- `repos[]`：GitHub `owner/repo` 列表（對應工作區專案綁定的倉）

**工作項：**

```json
{ "repo": "owner/repo", "number": 42 }
```

相容現況：僅 `issueNumbers` 且專案已綁單一 repo 時，伺服器可推斷 repo。新用戶端應帶完整參照。

### 禁止欄位

請求中不得出現（含 `extra`／extension data）：本機路徑、原始碼、檔案 blob、檔名列表、可還原原始碼的內容。伺服器偵測到應拒絕並留審計。

### 回應語意

- 成功：時段入「待 PM 確認」；回傳伺服器 `timesheetId` 與狀態。
- 409：已核准時段不可覆蓋。
- 名冊／專案解析失敗：人話錯誤碼。

## 專案貢獻彙總（公司端）

工作區收到回報後，依**專案**合併：

| 維度 | 怎麼看 |
|------|--------|
| 誰 | `github:{login}` 或 `personId` |
| 多久 | 加總 `hours`（扣更正） |
| 做了什麼 | Issue 關閉／參照次數；`contributionTypes` 分布 |
| 何時 | 按 `workDate` 週／月 |

PM 確認 → 人資鎖定 → 進入薪資／毛利。細節見 [薪資](modules/payroll.md)、[專案與客戶](modules/projects-clients.md)。

## 與控制台的關係

| | 控制台 | 其他用戶端 |
|--|--------|------------|
| 本機工時真相 | `work-hours.json` | 各自實作 |
| 申報 | 申報目的地清單 → 本契約 | 直打同一 HTTPS |
| 堆疊／編譯／Agent | 有 | 無（不需要） |

未設定申報目的地時，控制台行為維持現況：本機匯出，不上傳。

## 非目標（本契約第一刀）

- 完整 HR-XML XML 進出
- OSLC RDF 端點
- 上傳原始碼或建置產物
- 金流／請款
- 取代 GitHub Issue 本體（本契約只帶參照）

實作與 OpenAPI 檔案在工程階段產出；本頁是產品語意權威。
