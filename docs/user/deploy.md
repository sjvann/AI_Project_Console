# 部署

工具列「部署」用來記住**這份專案要發到哪裡**，並查看狀態、開啟線上網址。控制台本身不做完整 CI 代管；雲端實際發佈仍走各平台的 workflow 或你既有的流程。

本機開發不必填。目標選「不下發」即可。

## 設定

「部署設定…」先選發佈目標，再填該目標的欄位。

| 目標 | 主要欄位 |
|------|----------|
| 不下發（僅本機） | 不需遠端欄位 |
| Google Cloud (GCP) | Project ID、Zone、Instance；可選 Host、Workflow |
| 自家機房／遠端伺服器 | 協定（SSH／WinRM／IIS／SMB）、主機、連接埠、帳號、遠端路徑、金鑰、對外網址 |
| Microsoft Azure | Subscription、Resource Group、App Name、Region、對外網址、Workflow |

儲存位置與 GitHub 相同：本機 settings，或寫入 `ai-project.json` 給團隊。

既有清單裡的 `gcp` 區塊仍可讀寫。完整欄位見 [工作區設定](../workspace/ai-project-json.md) 與 [`schema/ai-project.example.json`](../../schema/ai-project.example.json)。

## 選單

| 動作 | 說明 |
|------|------|
| **部署設定…** | 選目標與填欄位 |
| **部署狀態** | 依目前設定摘要（需已選非「不下發」或已填完整） |
| **開啟線上** | 用設定裡的對外網址或主機開瀏覽器 |
| **部署說明** | 該目標建議的下一步（例如對應的 workflow 檔名） |

未填對外網址時，「開啟線上」會提示先完成設定。
