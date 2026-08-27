---
_layout: landing
_disableBreadcrumb: true
_disableAffix: true
_disableContribution: true
_disableNextArticle: true
---

# AI_Project 控制台

<div class="home-hero">
  <p class="home-kicker">本機堆疊控制台</p>
  <h1>選專案、編譯、一鍵啟動</h1>
  <p class="home-lead">掃描服務與需重編項目，看 Log、提交 GitHub。卡住時把說明交給本機 Agent。一般使用者請用安裝包，不必編譯原始碼。</p>
  <div class="home-actions">
    <a class="home-btn home-btn-primary" href="docs/user/getting-started.md">開始使用</a>
    <a class="home-btn home-btn-ghost" href="https://github.com/sjvann/AI_Project_Console/releases">下載安裝包</a>
    <a class="home-btn home-btn-ghost" href="docs/README.md">文件地圖</a>
  </div>
</div>

<section class="home-section" aria-labelledby="home-start">
<h2 id="home-start">三分鐘開始</h2>
<ol class="home-steps">
  <li>從 <a href="https://github.com/sjvann/AI_Project_Console/releases">Releases</a> 下載 <code>*-win-x64-setup.exe</code> 或 zip。Windows 10／11 需 <a href="https://developer.microsoft.com/microsoft-edge/webview2/">WebView2</a>（通常已內建）。</li>
  <li>開啟控制台，按「選擇專案目錄…」或「從 GitHub 開啟…」。</li>
  <li>有「需重編」先「建置 → 編譯過期項目」，再按「啟動」，然後「開啟前端」。</li>
</ol>
</section>

<section class="home-section" aria-labelledby="home-roles">
<h2 id="home-roles">依角色閱讀</h2>
<div class="home-cards">
  <a class="home-card" href="docs/README.md"><strong>一般使用者</strong><span>安裝、開專案、啟動、編譯、看 Log。</span></a>
  <a class="home-card" href="docs/workspace/ai-project-json.md"><strong>工作區負責人</strong><span>服務掃不到、多個倉要一起管時補 ai-project.json。</span></a>
  <a class="home-card" href="docs/user/github.md"><strong>GitHub</strong><span>clone、提交、同步、Release。</span></a>
  <a class="home-card" href="docs/user/help.md"><strong>卡住時</strong><span>UAT／編譯求救與專案問答。</span></a>
  <a class="home-card" href="docs/agent/mcp.md"><strong>Agent／MCP</strong><span>讓 Agent 回呼控制台查堆疊與編譯。</span></a>
  <a class="home-card" href="docs/maintainer/develop.md"><strong>維護者</strong><span>從原始碼編譯、打包，或查 API 參考。</span></a>
  <a class="home-card" href="docs/salekit/README.md"><strong>業務／採購</strong><span>經營層投影片：角色定位、專案與人員管理。</span></a>
</div>
</section>

<p class="home-note">關閉控制台<strong>不會</strong>停止已啟動的服務。請用「停止全部」。更細的畫面說明見 <a href="docs/user/interface.md">畫面導覽</a>、<a href="docs/user/daily-use.md">日常操作</a> 與 <a href="docs/user/troubleshooting.md">常見問題</a>。</p>
