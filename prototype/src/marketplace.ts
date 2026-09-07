import { MKT_PERSONAS, ROLES, listingPeriod, periodHint, type Listing, type RoleNeed } from "./data";
import {
  canApply,
  canPublish,
  currentOrg,
  currentTalent,
  getState,
  orgOf,
  setDraft,
  talentOf,
} from "./store";
import { esc, navLink } from "./util";

export function mktNav(r: string[]) {
  const on = (seg: string, extra = false) => r[1] === seg || extra;
  return `
    ${navLink("/mkt", "專案公告", r.length === 1)}
    ${navLink("/mkt/notices", "平台公告", on("notices"))}
    ${navLink("/mkt/wizard", "發布", on("wizard"))}
    ${navLink("/mkt/parties", "兩造", on("parties") || on("kyc"))}
    ${navLink("/mkt/inbox", "撮合", on("inbox"))}
    ${navLink("/mkt/deals", "成交與傭金", on("deals") || on("deal"))}
  `;
}

export function mktPersonaSelect() {
  const s = getState();
  return `<label class="persona">走讀身分
    <select id="persona">
      ${MKT_PERSONAS.map((p) => `<option value="${p.id}" ${s.persona === p.id ? "selected" : ""}>${esc(p.label)}</option>`).join("")}
    </select>
  </label>`;
}

export function viewMktHome(): string {
  const s = getState();
  const list = s.listings.filter((l) => s.filter === "全部" || l.roles.some((r) => r.craft === s.filter));
  return `
    <section class="hero">
      <p class="prd">仲介只做兩造、公告、撮合、傭金 · PRD-MKT-05／12／13</p>
      <h1>專案公告</h1>
      <p class="muted">有軟體需求的公司發布公告；通過認證的工程師應徵或受邀。不是自動配對，不是派遣，不做專案管理。</p>
      <div class="row">
        <a class="btn-primary" href="#/mkt/wizard">發布專案公告</a>
        <a class="btn-sec" href="#/mkt/parties">加入並認證</a>
      </div>
      <div class="row">
        ${["全部", ...ROLES].map((c) => `<button type="button" class="btn-sec ${s.filter === c ? "btn-primary" : ""}" data-filter="${c}">${c}</button>`).join("")}
      </div>
    </section>
    <div class="cards">
      ${list.map((l) => {
        const org = orgOf(l.orgId);
        return `<article class="card">
          <p class="hint">${esc(org?.name ?? "")} · ${esc(listingPeriod(l))}</p>
          <h3><a href="#/mkt/listing/${l.id}">${esc(l.title)}</a></h3>
          <p>${esc(l.problem)}</p>
          <div class="chips">${l.roles.map((r) => `<span class="chip">${r.craft} ×${r.count}</span>`).join("")}</div>
          <p class="hint">${esc(l.budget)} · ${l.onsite ? "需到場" : "遠端可"}</p>
        </article>`;
      }).join("")}
    </div>
  `;
}

export function viewNotices(): string {
  const s = getState();
  return `
    <p class="prd">資訊公告 · 平台規則與維運，不是專案需求精靈</p>
    <h1>平台公告</h1>
    <p class="muted">給兩造看的公開說明。專案需求請走「專案公告」。</p>
    ${s.notices.map((n) => `<article class="card">
      <p class="hint">${esc(n.date)} · ${esc(n.audience)}</p>
      <h3>${esc(n.title)}</h3>
      <p>${esc(n.body)}</p>
    </article>`).join("")}
  `;
}

export function viewParties(): string {
  const s = getState();
  const t = currentTalent();
  const o = currentOrg();
  return `
    <p class="prd">PRD-MKT-01～03／12／14 · 管理兩造，不是名冊派工</p>
    <h1>兩造會員</h1>
    <p class="muted">加入免費。未認證只能看公告，不能發布、應徵、成交。工作區名冊是另一套機制。</p>
    <div class="cols">
      <article class="card">
        <h3>需求組織</h3>
        <table class="table"><thead><tr><th>公司</th><th>統編</th><th>負責人</th><th>認證</th></tr></thead>
        <tbody>${s.orgs.map((x) => `<tr><td>${esc(x.name)}</td><td>${esc(x.taxId || "—")}</td><td>${esc(x.owner)}</td>
          <td>${x.verified ? `<span class="chip ok">已認證</span>` : `<span class="chip wait">未認證</span>`}</td></tr>`).join("")}</tbody></table>
        ${o ? `<p class="hint">目前身分：${esc(o.name)}</p>
          <button class="btn-primary" type="button" data-act="kyc-org">核對統編與負責人</button>` : `<p class="hint">切到需求窗口身分才能送認證。</p>`}
      </article>
      <article class="card">
        <h3>工程師</h3>
        <table class="table"><thead><tr><th>人</th><th>GitHub</th><th>角色</th><th>認證</th></tr></thead>
        <tbody>${s.talents.map((x) => `<tr><td>${esc(x.name)}</td><td>${esc(x.github || "—")}</td><td>${x.crafts.join("／")}</td>
          <td>${x.verified ? `<span class="chip ok">已認證</span>` : `<span class="chip wait">未認證</span>`}</td></tr>`).join("")}</tbody></table>
        ${t ? `
          <label>我的角色標籤（可複選）</label>
          <div class="chips">${ROLES.map((c) => `<button type="button" class="btn-sec ${t.crafts.includes(c) ? "btn-primary" : ""}" data-act="craft" data-craft="${c}">${c}</button>`).join("")}</div>
          <label class="row"><input type="checkbox" id="ghfail" ${s.githubFail ? "checked" : ""} /> 模擬 GitHub 對不上</label>
          <button class="btn-primary" type="button" data-act="kyc-talent">用 GitHub 完成認證</button>` : `<p class="hint">切到工程師身分才能送認證。</p>`}
      </article>
    </div>
    <p class="hint">同一自然人可兼窗口與自己的工程師檔。第一刀不做押金、eKYC、工程款託管。</p>
  `;
}

function roleOnsite(overall: boolean | undefined, role: RoleNeed) {
  return role.onsite ?? !!overall;
}

function qualSummary(d: Partial<Listing>) {
  const roles = d.roles ?? [];
  return `
    <p class="hint">整體：${esc(d.industry || "產業未填")} · ${esc(d.language || "繁中")} · ${d.onsite ? "需到場" : "遠端可"} · ${esc(d.skills || "共通技能未填")} · 年資 ${esc(d.years || "不拘")}</p>
    ${roles.length === 0 ? "" : `<table class="table"><thead><tr><th>角色</th><th>人數</th><th>技能</th><th>年資</th><th>到場</th></tr></thead><tbody>
      ${roles.map((r) => `<tr><td>${r.craft}</td><td>×${r.count}</td><td>${esc(r.skills || "沿用整體")}</td><td>${esc(r.years || "沿用整體")}</td><td>${roleOnsite(d.onsite, r) ? "需到場" : "遠端可"}</td></tr>`).join("")}
    </tbody></table>`}
  `;
}

function wizardQualStep(d: Partial<Listing>) {
  const roles = d.roles ?? [];
  return `
    <p>先寫<strong>整案</strong>都適用的資格，再依上一步已選角色補各角色條件。未填的角色欄會沿用整體。</p>
    <label>共通技能</label><input id="w-skills" value="${esc(d.skills ?? "")}" placeholder="例如：能讀既有 ERP 文件" />
    <label>共通年資</label><input id="w-years" value="${esc(d.years ?? "")}" placeholder="不拘，或見各角色" />
    <label>產業</label><input id="w-industry" value="${esc(d.industry ?? "")}" />
    <label>語言</label><input id="w-lang" value="${esc(d.language ?? "繁中")}" />
    <label class="row"><input type="checkbox" id="w-onsite" ${d.onsite ? "checked" : ""} /> 整案需要到場（各角色可再改）</label>
    ${roles.length === 0 ? `<div class="flash">還沒選角色。請回上一步勾選分析／設計／開發／測試。</div>` : `<h3 class="qual-h">各角色資格</h3>
      <div class="qual-roles">${roles.map((r) => {
        const on = roleOnsite(d.onsite, r);
        return `<article class="card role-qual">
          <h3>${r.craft} ×${r.count}</h3>
          <label>技能</label><input id="w-r-skills-${r.craft}" value="${esc(r.skills ?? "")}" placeholder="此角色要會什麼" />
          <label>年資</label><input id="w-r-years-${r.craft}" value="${esc(r.years ?? "")}" placeholder="例如：3 年以上" />
          <label class="row"><input type="checkbox" id="w-r-onsite-${r.craft}" ${on ? "checked" : ""} /> 此角色需要到場</label>
        </article>`;
      }).join("")}</div>`}
  `;
}

export function viewWizard(): string {
  const s = getState();
  const d = s.draft;
  const step = s.wizardStep;
  const labels = ["問題", "範圍", "角色", "資格", "預算", "預覽"];
  return `
    <p class="prd">PRD-MKT-04 · 網頁精靈，不寫 Git，不要求控制台</p>
    <h1>發布專案公告</h1>
    ${!canPublish() ? `<div class="flash">${currentOrg() ? "這家公司還沒完成認證，不能發布。你可以先填，按刊登會被拒絕並說人話。" : "請先切到「需求窗口」身分。訪客不能發布。"}</div>` : ""}
    <div class="steps">${labels.map((n, i) => `<span class="step ${step === i + 1 ? "on" : ""}">${i + 1}. ${n}</span>`).join("")}</div>
    <article class="card">
      ${step === 1 ? `<label>一句話標題</label><input id="w-title" value="${esc(d.title ?? "")}" />
        <label>要解決的問題</label><textarea id="w-problem">${esc(d.problem ?? "")}</textarea>` : ""}
      ${step === 2 ? `<label>範圍</label><textarea id="w-in">${esc(d.inScope ?? "")}</textarea>
        <label>非範圍</label><textarea id="w-out">${esc(d.outScope ?? "")}</textarea>` : ""}
      ${step === 3 ? `<p>需要哪些人（可複選）。點選角色後再設定人數，每人至少 1。</p>
        <div class="role-grid">${ROLES.map((c) => {
          const picked = (d.roles ?? []).find((r) => r.craft === c);
          return `<div class="role-pick ${picked ? "is-on" : ""}">
            <button type="button" class="btn-sec ${picked ? "btn-primary" : ""} role-name" data-act="draft-craft" data-craft="${c}">${c}</button>
            ${picked ? `<div class="role-count">
              <button type="button" class="btn-sec" data-act="role-dec" data-craft="${c}" aria-label="${c} 少一人" ${picked.count <= 1 ? "disabled" : ""}>−</button>
              <span class="n">${picked.count}</span>
              <span class="hint">人</span>
              <button type="button" class="btn-sec" data-act="role-inc" data-craft="${c}" aria-label="${c} 多一人" ${picked.count >= 20 ? "disabled" : ""}>+</button>
            </div>` : `<p class="hint">未選</p>`}
          </div>`;
        }).join("")}</div>` : ""}
      ${step === 4 ? wizardQualStep(d) : ""}
      ${step === 5 ? `<div class="date-range">
          <div>
            <label>開始日</label>
            <input type="date" id="w-from" value="${esc(d.periodStart ?? "")}" />
          </div>
          <div>
            <label>結束日</label>
            <input type="date" id="w-to" value="${esc(d.periodEnd ?? "")}" min="${esc(d.periodStart ?? "")}" />
          </div>
        </div>
        <p class="hint">${d.periodStart && d.periodEnd ? periodHint(d.periodStart, d.periodEnd) : "用日曆選預估開工與結束，不要手打「三個月」。"}</p>
        <label>預算或時計區間（可填「面議」）</label><input id="w-budget" value="${esc(d.budget ?? "面議")}" />` : ""}
      ${step === 6 ? `<p><strong>${esc(d.title || "（未填標題）")}</strong></p>
        <p>${esc(d.problem || "")}</p>
        <p>範圍 ${esc(d.inScope || "—")} ／ 非範圍 ${esc(d.outScope || "—")}</p>
        ${qualSummary(d)}
        <p>期間 ${esc(listingPeriod({ period: d.period ?? "", periodStart: d.periodStart, periodEnd: d.periodEnd }))} · 預算 ${esc(d.budget || "—")}</p>
        <p class="hint">刊登後<strong>不寫進 Git</strong>。成交與傭金入帳後，才把事件交給公司工作區。</p>` : ""}
      <div class="row" style="margin-top:12px">
        ${step > 1 ? `<button class="btn-sec" type="button" data-act="wiz" data-n="${step - 1}">上一步</button>` : ""}
        ${step < 6 ? `<button class="btn-primary" type="button" data-act="wiz-next" data-n="${step + 1}">下一步</button>` : `<button class="btn-primary" type="button" data-act="publish">刊登公告</button>`}
      </div>
    </article>
  `;
}

export function readWizardFields() {
  const g = (id: string) => (document.getElementById(id) as HTMLInputElement | HTMLTextAreaElement | null)?.value;
  const onsite = (document.getElementById("w-onsite") as HTMLInputElement | null)?.checked;
  const prev = getState().draft;
  const roles = (prev.roles ?? []).map((r) => {
    const box = document.getElementById(`w-r-onsite-${r.craft}`) as HTMLInputElement | null;
    return {
      ...r,
      skills: g(`w-r-skills-${r.craft}`) ?? r.skills,
      years: g(`w-r-years-${r.craft}`) ?? r.years,
      onsite: box ? box.checked : r.onsite,
    };
  });
  const from = g("w-from");
  const to = g("w-to");
  const periodStart = from ?? prev.periodStart;
  const periodEnd = to ?? prev.periodEnd;
  setDraft({
    title: g("w-title") ?? prev.title,
    problem: g("w-problem") ?? prev.problem,
    inScope: g("w-in") ?? prev.inScope,
    outScope: g("w-out") ?? prev.outScope,
    skills: g("w-skills") ?? prev.skills,
    years: g("w-years") ?? prev.years,
    industry: g("w-industry") ?? prev.industry,
    language: g("w-lang") ?? prev.language,
    periodStart,
    periodEnd,
    period: periodStart && periodEnd ? `${periodStart} ～ ${periodEnd}` : prev.period,
    budget: g("w-budget") ?? prev.budget,
    onsite: onsite ?? prev.onsite,
    roles,
  });
}

export function viewListing(id: string): string {
  const s = getState();
  const l = s.listings.find((x) => x.id === id);
  if (!l) return `<p>找不到這則公告。</p>`;
  const org = orgOf(l.orgId)!;
  const apps = s.applications.filter((a) => a.listingId === id);
  const inviteable = s.talents.filter((t) => t.verified);
  return `
    <p class="prd">公告＋撮合 · MKT-05／06／13</p>
    <article class="card">
      <p class="hint">${esc(org.name)}（統編 ${esc(org.taxId || "未認證")}）</p>
      <h1>${esc(l.title)}</h1>
      <p>${esc(l.problem)}</p>
      <div class="cols">
        <div><label>範圍</label><p>${esc(l.inScope)}</p></div>
        <div><label>非範圍</label><p>${esc(l.outScope)}</p></div>
      </div>
      <div class="chips">${l.roles.map((r) => `<span class="chip">${r.craft} ×${r.count}</span>`).join("")}</div>
      ${qualSummary(l)}
      <p>期間 ${esc(listingPeriod(l))} · 預算 ${esc(l.budget)}</p>
      <p class="hint">此公告<strong>沒有</strong>寫進 Git。開工後由工程師用控制台落到 intake.json（控制台已出貨，本雛型不模擬）。</p>
      <div class="row">
        <button class="btn-primary" type="button" data-act="apply" data-id="${l.id}">應徵這則專案</button>
        <span class="hint">${canApply() ? "你已認證，可以應徵。" : "未認證或訪客：按鈕仍可按，會說人話拒絕。"}</span>
      </div>
    </article>
    ${canPublish() && currentOrg()?.id === l.orgId ? `
      <article class="card">
        <h3>邀請已認證工程師（不是自動媒合）</h3>
        <div class="row">
          ${inviteable.map((t) => `<button class="btn-sec" type="button" data-act="invite" data-listing="${l.id}" data-talent="${t.id}">邀請 ${esc(t.name)}（${t.crafts.join("／")}）</button>`).join("")}
        </div>
      </article>` : ""}
    <article class="card">
      <h3>這則的應徵／邀請</h3>
      ${apps.length === 0 ? `<p class="muted">還沒有人。</p>` : `<table class="table"><thead><tr><th>人</th><th>狀態</th><th></th></tr></thead><tbody>
        ${apps.map((a) => {
          const t = talentOf(a.talentId)!;
          const canAccept = canPublish() && currentOrg()?.id === l.orgId && a.status !== "accepted";
          const talentCanAccept = canApply() && currentTalent()?.id === a.talentId && a.status === "invited";
          return `<tr><td>${esc(t.name)}</td><td>${a.status === "accepted" ? "已成交" : a.status === "invited" ? "邀請中" : "已應徵"}</td><td>
            ${canAccept || talentCanAccept ? `<button class="btn-primary" type="button" data-act="accept" data-id="${a.id}">雙方接受</button>` : ""}
          </td></tr>`;
        }).join("")}
      </tbody></table>`}
    </article>
  `;
}

export function viewInbox(): string {
  const s = getState();
  return `
    <p class="prd">PRD-MKT-05／06 · 撮合兩造</p>
    <h1>應徵與邀請</h1>
    <p class="muted">不是自動配對。雙方接受才成交，然後走合同與傭金，不在這裡開甘特。</p>
    <table class="table"><thead><tr><th>公告</th><th>工程師</th><th>狀態</th><th></th></tr></thead><tbody>
      ${s.applications.map((a) => {
        const l = s.listings.find((x) => x.id === a.listingId)!;
        const t = talentOf(a.talentId)!;
        return `<tr><td><a href="#/mkt/listing/${l.id}">${esc(l.title)}</a></td><td>${esc(t.name)}</td><td>${a.status === "accepted" ? "已成交" : a.status === "invited" ? "邀請中" : "已應徵"}</td>
          <td>${a.status !== "accepted" ? `<button class="btn-primary" type="button" data-act="accept" data-id="${a.id}">雙方接受</button>` : `<a href="#/mkt/deals">看傭金</a>`}</td></tr>`;
      }).join("")}
    </tbody></table>
  `;
}

function payChip(pay: string) {
  if (pay === "已入帳") return `<span class="chip ok">已入帳</span>`;
  if (pay === "待付款") return `<span class="chip wait">待付款</span>`;
  if (pay === "待開立") return `<span class="chip wait">待開立</span>`;
  return `<span class="chip">待合同</span>`;
}

export function viewDeals(): string {
  const s = getState();
  if (s.deals.length === 0) return `<h1>成交與傭金</h1><p class="muted">還沒有成交。從一則公告按「雙方接受」。</p>`;
  return `<h1>成交與傭金</h1><p class="prd">PRD-MKT-07／08 · 需求方應付 8%（面議有最低應收）</p>
    <table class="table"><thead><tr><th>成交</th><th>專案</th><th>工程師</th><th>仲介費</th><th>金流</th></tr></thead><tbody>
    ${s.deals.map((d) => {
      const l = s.listings.find((x) => x.id === d.listingId)!;
      const t = talentOf(d.talentId)!;
      return `<tr><td><a href="#/mkt/deal/${d.id}">${d.id}</a></td><td>${esc(l.title)}</td><td>${esc(t.name)}</td>
        <td>TWD ${d.receivable.toLocaleString()}</td>
        <td>${payChip(d.pay)}</td></tr>`;
    }).join("")}
    </tbody></table>
    <p class="hint">這是平台抽成金流，不是工程款、不是代發薪、不做電子發票引擎。</p>`;
}

export function viewDeal(id: string): string {
  const s = getState();
  const d = s.deals.find((x) => x.id === id);
  if (!d) return `<p>找不到成交。</p>`;
  const l = s.listings.find((x) => x.id === d.listingId)!;
  const t = talentOf(d.talentId)!;
  const org = orgOf(l.orgId)!;
  const steps = ["雙方接受", "合同確認", "開立仲介費", "付款", "入帳結束"];
  const idx = d.pay === "已入帳" ? 4 : d.pay === "待付款" ? 3 : d.pay === "待開立" ? 2 : d.orgConfirmed && d.talentConfirmed ? 1 : 0;
  return `
    <p class="prd">合同＋傭金金流 · 仲介到這裡結束</p>
    <article class="card">
      <h1>成交 ${esc(d.id)}</h1>
      <p>當事人：<strong>${esc(org.name)}</strong> ↔ <strong>${esc(t.name)}</strong>。平台是仲介與抽成方，<strong>不是派遣雇主</strong>。</p>
      <div class="steps">${steps.map((n, i) => `<span class="step ${i <= idx ? "on" : ""}">${i + 1}. ${n}</span>`).join("")}</div>
      <div class="cols">
        <div class="card"><h3>保密</h3><p>雙方不把對方業務資料與原始碼交給無關第三人。原始碼不上仲介雲。</p></div>
        <div class="card"><h3>承攬範圍</h3><p>${esc(l.title)}：${esc(l.inScope)} 非範圍：${esc(l.outScope)}</p></div>
      </div>
      <div class="row">
        <button class="btn-sec" type="button" data-act="confirm" data-deal="${d.id}" data-side="org" ${d.orgConfirmed ? "disabled" : ""}>${d.orgConfirmed ? "公司已確認" : "公司在平台確認"}</button>
        <button class="btn-sec" type="button" data-act="confirm" data-deal="${d.id}" data-side="talent" ${d.talentConfirmed ? "disabled" : ""}>${d.talentConfirmed ? "工程師已確認" : "工程師在平台確認"}</button>
      </div>
    </article>
    <article class="card">
      <h3>仲介費金流</h3>
      <p>應收 <strong>TWD ${d.receivable.toLocaleString()}</strong> · 目前 ${payChip(d.pay)}${d.paidAt ? ` · ${esc(d.paidAt)}` : ""}</p>
      <p class="hint">範例規則：需求方應付。不託管工程款、不自動扣工程師薪水。</p>
      <div class="row">
        <button class="btn-primary" type="button" data-act="invoice" data-deal="${d.id}" ${d.pay !== "待開立" ? "disabled" : ""}>開立仲介費應收</button>
        <button class="btn-primary" type="button" data-act="pay" data-deal="${d.id}" ${d.pay !== "待付款" ? "disabled" : ""}>模擬付款（第三方金流）</button>
      </div>
      ${d.pay === "已入帳" ? `<div class="okbox" style="margin-top:12px">
        <p><strong>仲介主流程結束。</strong> 專案管理請回公司工作區；工程師做工請用本機控制台（已出貨）。</p>
        <div class="row">
          <button class="btn-primary" type="button" data-act="handoff" data-deal="${d.id}">把成交事件交給工作區</button>
          <a class="btn-sec" href="#/ws/inbox">打開公司工作區</a>
        </div>
      </div>` : ""}
    </article>
  `;
}
