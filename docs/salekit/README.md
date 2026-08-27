---
title: 銷售套件
---

# 銷售套件

給**外部軟體公司經營層**的採購說明材料。一般使用者請走 [安裝與第一次使用](../user/getting-started.md)，不必從這裡開始。

## 這份套件有什麼

| 檔案 | 用途 |
|------|------|
| [經營層投影片（PDF）](executive-pitch.pdf) | 12 頁橫向簡報，寄信或投影都用這份。 |
| [經營層投影片（HTML）](executive-pitch.html) | 瀏覽器全螢幕投影，鍵盤左右鍵翻頁。 |
| [講者重點](talking-points.md) | 每一頁要講什麼、不要誇什麼、被問時怎麼回。 |

控制台文件樹只會列出 Markdown。PDF 與 HTML 請從本頁連結開啟，或用檔案總管打開 `docs/salekit/`。

## 投影怎麼開

1. **寄給對方：** 用 `docs/salekit/executive-pitch.pdf`（橫向 16:9，12 頁）。
2. **現場投影：** 用瀏覽器開 `docs/salekit/executive-pitch.html`，按 `F11` 全螢幕。`→`／空白鍵下一頁，`←` 上一頁。
3. 線上：文件站建置後，同一路徑也會被複製到網站（見 [文件網站](../maintainer/docfx.md)）。
4. 圖表標「示意」：用來對照痛點，**不是**客戶實測數字。勿當成保證成效。
5. 若改過 HTML、要重出 PDF：用 Edge 開啟 `executive-pitch.html?print=1`，再「列印 → 另存為 PDF」，或用無頭 Edge `--print-to-pdf`。

建議聽眾：總經理、技術長、專案／交付主管、人資或財務（工時對帳）。時間約 20 分鐘簡報 + 10 分鐘問答。

## 一句定位

AI_Project 控制台不是另一套雲端專案管理軟體，也不是取代 Cursor 或 Copilot。它是坐在 **Git、GitHub、本機 Agent** 之上的 **Agentic 指揮台**：把專案治理、人員課責、堆疊真相接到你們已有的 AI，並留下審計。

下一步：用對方自己的倉走完 [90 分鐘導入](../team/trial-90min.md)。
