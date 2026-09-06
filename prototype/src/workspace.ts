import { WS_ROLES } from "./data";
import { clientOf, getState, pendingHandoffs, personOf, projectOf, weekHours } from "./store";
import { esc, navLink } from "./util";

export function wsNav(r: string[]) {
  const pending = pendingHandoffs().length;
  const seg = r[1] ?? "war";
  return `
    ${navLink("/ws/war", "戰情室", seg === "war")}
    ${navLink("/ws/projects", "專案", seg === "projects" || seg === "project")}
    ${navLink("/ws/clients", "客戶", seg === "clients")}
    ${navLink("/ws/people", "人員", seg === "people")}
    ${navLink("/ws/dispatch", "派工", seg === "dispatch")}
    ${navLink("/ws/hours", "工時確認", seg === "hours")}
    ${navLink("/ws/budget", "預算", seg === "budget")}
    ${navLink("/ws/inbox", "仲介來件", seg === "inbox", pending)}
  `;
}

export function wsRoleSelect() {
  const s = getState();
  return `<label class="persona">工作區角色
    <select id="wsrole">
      ${WS_ROLES.map((p) => `<option value="${p.id}" ${s.wsRole === p.id ? "selected" : ""}>${esc(p.label)}</option>`).join("")}
    </select>
  </label>`;
}

export function viewInbox(): string {
  const s = getState();
  return `
    <p class="prd">從仲介接收成交事件 · 工作區可獨立存在</p>
    <h1>仲介來件</h1>
    <p class="muted">仲介在傭金入帳後把人與範圍交過來。你也可以不經仲介，自己在「專案」立案。</p>
    ${s.handoffs.length === 0 ? `<article class="card"><p>目前沒有來件。可先管自己的內部專案，或到 <a href="#/mkt">仲介平台</a> 走完成交與付款。</p></article>` : ""}
    ${s.handoffs.map((h) => `<article class="card">
      <p class="hint">${esc(h.orgName)} · ${esc(h.dealId)} · github:${esc(h.github)}</p>
      <h3>${esc(h.title)}</h3>
      <p>工程師 ${esc(h.talentName)} · 範圍：${esc(h.inScope)}</p>
      ${h.accepted ? `<p class="okbox">已接手，見 <a href="#/ws/project/P-${h.listingId}">專案</a>。</p>` : `<button class="btn-primary" type="button" data-act="accept-handoff" data-deal="${h.dealId}">接手：寫入名冊與專案</button>`}
    </article>`).join("")}
  `;
}

export function viewWar(): string {
  const s = getState();
  const running = s.projects.filter((p) => p.status === "進行" || p.status === "立案");
  const overdue = s.projects.filter((p) => p.phase === "需求" || p.target < "2026-10-01");
  const pendingTs = s.timesheets.filter((t) => t.status === "待確認").length;
  const overload = s.people.filter((p) => weekHours(p.id) > p.hoursCap);
  const inbox = pendingHandoffs().length;
  return `
    <p class="prd">PRD-WAR · 經營層落地 · 不是仲介首頁</p>
    <div class="row"><h1>戰情室</h1><span class="chip wait">規劃</span></div>
    <p class="muted">大同食品工作區。仲介傭金不在這裡。工程師繪圖在控制台（已出貨）。</p>
    ${inbox ? `<div class="okbox">有 ${inbox} 筆仲介來件未接手。 <a href="#/ws/inbox">去處理</a></div>` : ""}
    <div class="kpi">
      <div class="card"><div class="n">${running.length}</div>進行中</div>
      <div class="card"><div class="n" style="color:var(--danger)">${overdue.length}</div>紅燈（含即將到期）</div>
      <div class="card"><div class="n">${overload.length}</div>人力超載</div>
      <div class="card"><div class="n">${pendingTs}</div>待確認工時</div>
    </div>
    <div class="cards">
      ${s.projects.map((p) => {
        const client = clientOf(p.clientId);
        const red = p.target < "2026-10-01";
        return `<article class="card">
          <p class="hint">${esc(client?.name ?? "")} · ${esc(p.phase)}</p>
          <h3><a href="#/ws/project/${p.id}">${esc(p.name)}</a></h3>
          <p>目標 ${esc(p.target)} ${red ? `<span class="chip bad">逾期風險</span>` : `<span class="chip ok">進行</span>`}</p>
          ${p.fromDealId ? `<p class="hint">來源：仲介成交 ${esc(p.fromDealId)}</p>` : `<p class="hint">來源：公司自行立案</p>`}
        </article>`;
      }).join("")}
    </div>
    <article class="card">
      <h3>例外</h3>
      <ul>
        <li>工時待確認 ${pendingTs} 筆 — <span class="chip wait">注意</span> <a href="#/ws/hours">去確認</a></li>
        ${overload.map((p) => `<li>${esc(p.name)} 本週 ${weekHours(p.id)}h／${p.hoursCap} — <span class="chip bad">超載</span> 不只靠顏色</li>`).join("")}
        ${s.projects.some((p) => !p.repo) ? `<li>有專案尚未掛倉 — 說明句，不是 500</li>` : ""}
      </ul>
    </article>
  `;
}

export function viewProjects(): string {
  const s = getState();
  return `
    <p class="prd">PRD-PRJ · 可獨立立案，也可接收仲介投影</p>
    <h1>專案</h1>
    <article class="card">
      <label>自行立案（不必先走仲介）</label>
      <div class="row">
        <input id="new-proj" value="${esc(s.newProjectName)}" placeholder="例如：門市庫存盤點小工具" />
        <button class="btn-primary" type="button" data-act="add-project">成立專案</button>
      </div>
    </article>
    <table class="table"><thead><tr><th>專案</th><th>客戶</th><th>階段</th><th>目標</th><th>來源</th></tr></thead>
    <tbody>${s.projects.map((p) => {
      const c = clientOf(p.clientId);
      return `<tr><td><a href="#/ws/project/${p.id}">${esc(p.name)}</a></td><td>${esc(c?.name ?? "")}</td>
        <td>${esc(p.phase)}</td><td>${esc(p.target)}</td><td>${p.fromDealId ? "仲介" : "自行"}</td></tr>`;
    }).join("")}</tbody></table>
  `;
}

export function viewProject(id: string): string {
  const p = projectOf(id);
  if (!p) return `<p>找不到專案。</p>`;
  const s = getState();
  const c = clientOf(p.clientId);
  const crew = s.assignments.filter((a) => a.projectId === p.id).map((a) => personOf(a.personId)?.name).filter(Boolean);
  const unique = [...new Set(crew)];
  const margin = p.revenue === 0 ? "—" : `${Math.round(((p.revenue - p.cost) / p.revenue) * 100)}%`;
  return `
    <p class="prd">專案首頁 · 甘特／進件只讀 · 不做控制台進件表</p>
    <h1>${esc(p.name)}</h1>
    <p class="okbox">客戶＝${esc(c?.name ?? "")}。範圍：${esc(p.inScope)}。進件 Markdown 仍在 Git，此處只讀彙總：已發出 0、待驗收 0。</p>
    <p>倉：${p.repo ? `<code>${esc(p.repo)}</code>` : "尚未掛倉。溝通請開 GitHub Issue，沒有站內聊天。"}</p>
    ${p.fromDealId ? `<p class="hint">從仲介成交 ${esc(p.fromDealId)} 投影，不必重填精靈。</p>` : ""}
    <h3>甘特</h3>
    <div class="gantt">
      <div class="gantt-row"><span>需求</span><div class="bar ${p.phase === "需求" ? "bad" : ""}" style="width:40%"></div></div>
      <div class="gantt-row"><span>設計</span><div class="bar warn" style="width:25%"></div></div>
      <div class="gantt-row"><span>實作</span><div class="bar" style="width:${p.phase === "實作" ? "55" : "10"}%"></div></div>
      <div class="gantt-row"><span>驗證</span><div class="bar" style="width:5%"></div></div>
    </div>
    <p class="hint">目前派工：${unique.length ? unique.map((n) => esc(n!)).join("、") : "尚未派人"} · <a href="#/ws/dispatch">週矩陣</a></p>
    <div class="cols">
      <article class="card"><h3>損益迷你卡</h3><p>計劃收入 ${p.revenue ? "TWD " + p.revenue.toLocaleString() : "內部案 0"}</p>
        <p>人事成本（已確認工時）TWD ${p.cost.toLocaleString()}</p>
        <p>毛利率 ${margin}（收入 0 顯示 — 不當 100%）</p></article>
      <article class="card"><h3>輔助</h3>
        <p>Issue 溝通面在 GitHub。工時由控制台上傳後，在「工時確認」處理。</p>
        <p><a href="#/ws/hours">去確認工時</a> · <a href="#/ws/budget">預算</a></p>
      </article>
    </div>
  `;
}

export function viewClients(): string {
  const s = getState();
  return `
    <p class="prd">PRD-PRJ 客戶生命週期 · 潛在不能派工</p>
    <h1>客戶</h1>
    <table class="table"><thead><tr><th>客戶</th><th>階段</th><th>下次跟進</th><th></th></tr></thead>
    <tbody>${s.clients.map((c) => `<tr>
      <td>${esc(c.name)}</td><td>${esc(c.stage)}</td><td>${esc(c.next)}</td>
      <td>${c.stage === "潛在" ? `<span class="chip wait">不能派工</span>` : "可立案"}</td>
    </tr>`).join("")}</tbody></table>
    <p class="hint">潛在客戶不佔戰情室「進行中」。仲介成交進來的案掛在「本組織」客戶下。</p>
  `;
}

export function viewPeople(): string {
  const s = getState();
  return `
    <p class="prd">PRD-PPL · 仲介成交預設專案外包，可改</p>
    <h1>人員</h1>
    <table class="table"><thead><tr><th>名</th><th>類型</th><th>GitHub</th><th>本週</th><th>狀態</th><th>來源</th></tr></thead>
    <tbody>${s.people.map((p) => `<tr>
      <td>${esc(p.name)}</td><td>${esc(p.type)}</td><td>${esc(p.github || "—")}</td>
      <td>${weekHours(p.id)}／${p.hoursCap}</td><td>${esc(p.status)}</td>
      <td>${p.fromDealId ? "仲介 " + esc(p.fromDealId) : "公司自建"}</td>
    </tr>`).join("")}</tbody></table>
    <p class="hint">工程師看不到同事月薪。費率預設遮罩。本雛型不重做控制台。</p>
  `;
}

export function viewDispatch(): string {
  const s = getState();
  const days = [1, 2, 3, 4, 5];
  const labels = ["一", "二", "三", "四", "五"];
  return `
    <p class="prd">PRD-DSP · 點格子改小時（0→6→8→10）。超載紅底。</p>
    <h1>派工週矩陣</h1>
    <p class="muted">人力面在這裡；任務面在 GitHub Issue。工程師本機堆疊不在這站。</p>
    <div class="matrix"><table class="table"><thead><tr><th>人</th>${labels.map((d) => `<th class="cell">${d}</th>`).join("")}<th>合計</th></tr></thead>
    <tbody>
      ${s.people.filter((p) => p.status === "在職").map((p) => {
        const total = weekHours(p.id);
        const over = total > p.hoursCap;
        return `<tr><td>${esc(p.name)}<div class="hint">${esc(p.type)}</div></td>
          ${days.map((d) => {
            const cell = s.assignments.find((a) => a.personId === p.id && a.day === d);
            const h = cell?.hours ?? 0;
            const proj = cell ? projectOf(cell.projectId)?.name ?? "" : "";
            return `<td class="cell ${h > 8 ? "over" : ""}"><button type="button" class="btn-sec" data-act="assign" data-person="${p.id}" data-day="${d}">${h ? `${h}h` : "—"}</button><div class="hint">${esc(proj)}</div></td>`;
          }).join("")}
          <td>${total}h ${over ? `<span class="chip bad">超載</span>` : ""}</td></tr>`;
      }).join("")}
    </tbody></table></div>
    <p class="hint">側欄未派 Issue：#12 庫存對帳。寫回 GitHub 失敗會標待同步，不假裝已派。</p>
  `;
}

export function viewHours(): string {
  const s = getState();
  return `
    <p class="prd">PRD-PAY-01／02 · 工時從控制台來，這裡只確認</p>
    <h1>工時確認</h1>
    <p class="muted">繪圖與計時在工程師控制台（已出貨）。PM 只確認歸屬，不核定薪水金額。仲介不代發薪。</p>
    <table class="table"><thead><tr><th>人</th><th>專案</th><th>時數</th><th>狀態</th><th></th></tr></thead>
    <tbody>${s.timesheets.map((t) => {
      const person = personOf(t.personId);
      const proj = projectOf(t.projectId);
      const chip = t.status === "已確認" ? "ok" : t.status === "退回" ? "bad" : "wait";
      return `<tr><td>${esc(person?.name ?? "")}</td><td>${esc(proj?.name ?? "")}</td><td>${t.hours}</td>
        <td><span class="chip ${chip}">${esc(t.status)}</span><div class="hint">${esc(t.note)}</div></td>
        <td>${t.status === "待確認" ? `<div class="row">
          <button class="btn-primary" type="button" data-act="confirm-ts" data-id="${t.id}">確認</button>
          <button class="btn-sec" type="button" data-act="return-ts" data-id="${t.id}">退回</button>
        </div>` : ""}</td></tr>`;
    }).join("")}</tbody></table>
  `;
}

export function viewBudget(): string {
  const s = getState();
  return `
    <p class="prd">PRD-BDG · 專案毛利，不是總帳</p>
    <h1>預算與毛利</h1>
    <table class="table"><thead><tr><th>專案</th><th>收入</th><th>人事成本</th><th>毛利率</th></tr></thead>
    <tbody>${s.projects.map((p) => {
      const rate = p.revenue === 0 ? "—" : `${Math.round(((p.revenue - p.cost) / p.revenue) * 100)}%`;
      return `<tr><td>${esc(p.name)}</td><td>${p.revenue ? "TWD " + p.revenue.toLocaleString() : "內部 0"}</td>
        <td>TWD ${p.cost.toLocaleString()}</td><td>${rate}</td></tr>`;
    }).join("")}</tbody></table>
    <p class="hint">門檻低於 25% 變黃。內部無收入專案毛利率顯示「—」。匯出 CSV 給既有財務，不做報稅。</p>
  `;
}

export function defaultWsPath(role: string) {
  if (role === "hr") return "/ws/people";
  if (role === "pm") return "/ws/projects";
  return "/ws/war";
}
