import {
  accept,
  acceptHandoff,
  addInternalProject,
  applyTo,
  bumpDraftRoleCount,
  clearToast,
  confirmDeal,
  confirmTimesheet,
  currentTalent,
  cycleAssign,
  emitHandoff,
  getState,
  invite,
  issueInvoice,
  payCommission,
  pendingHandoffs,
  publishDraft,
  resetDemo,
  returnTimesheet,
  setCrafts,
  setFilter,
  setGithubFail,
  setNewProjectName,
  setPersona,
  setWizardStep,
  setWsRole,
  showError,
  toggleDraftCraft,
  verifyOrg,
  verifyTalent,
} from "./store";
import {
  mktNav,
  mktPersonaSelect,
  readWizardFields,
  viewDeal,
  viewDeals,
  viewInbox as viewMktInbox,
  viewListing,
  viewMktHome,
  viewNotices,
  viewParties,
  viewWizard,
} from "./marketplace";
import {
  defaultWsPath,
  viewBudget,
  viewClients,
  viewDispatch,
  viewHours,
  viewInbox,
  viewPeople,
  viewProject,
  viewProjects,
  viewWar,
  wsNav,
  wsRoleSelect,
} from "./workspace";
import { type Craft, type MktPersona, type WsRole } from "./data";

export function route(): string[] {
  const h = (location.hash || "#/").replace(/^#/, "");
  const parts = h.split("/").filter(Boolean);
  const legacy = ["listing", "kyc", "wizard", "inbox", "deals", "deal", "notices", "parties"];
  if (parts[0] === "console") return ["gate"];
  if (parts[0] === "ws") return parts.length === 1 ? ["ws", "war"] : parts;
  if (parts[0] === "mkt") return parts;
  if (legacy.includes(parts[0] ?? "")) return ["mkt", ...parts];
  return parts.length === 0 ? ["gate"] : parts;
}

export function go(path: string) {
  location.hash = path.startsWith("#") ? path : "#" + path;
}

export function layout(body: string): string {
  const s = getState();
  const r = route();
  const product = r[0] === "mkt" ? "mkt" : r[0] === "ws" ? "ws" : "gate";
  const pending = pendingHandoffs().length;
  return `
    <div class="banner"><strong>規劃雛型，不是出貨產品。</strong> 兩套獨立機制；工程師控制台已出貨，本雛型不模擬。資料在此瀏覽器 sessionStorage。</div>
    <header class="top ${product === "ws" ? "is-ws" : ""}">
      ${product === "mkt" ? `<a class="brand" href="#/mkt"><span class="mark">仲</span> AI_Project 仲介</a>` : ""}
      ${product === "ws" ? `<a class="brand" href="#/ws/war"><span class="mark">區</span> 大同食品 · 工作區</a>` : ""}
      ${product === "gate" ? `<a class="brand" href="#/"><span class="mark">A</span> AI_Project</a>` : ""}
      <nav class="nav">
        ${product === "mkt" ? mktNav(r) : ""}
        ${product === "ws" ? wsNav(r) : ""}
      </nav>
      <span class="spacer"></span>
      <nav class="switch">
        <a href="#/mkt" class="${product === "mkt" ? "is-on" : ""}">仲介平台</a>
        <a href="#/ws/war" class="${product === "ws" ? "is-on" : ""}">公司工作區${pending ? `<span class="badge">${pending}</span>` : ""}</a>
      </nav>
      ${product === "mkt" ? mktPersonaSelect() : ""}
      ${product === "ws" ? wsRoleSelect() : ""}
      <button class="btn-sec" type="button" data-act="reset">重設範例</button>
    </header>
    <main class="wrap ${product === "gate" ? "wrap-gate" : ""}">${body}</main>
    <footer class="proto">${product === "mkt" ? "承攬仲介，不是派遣。加入免費，認證要強。只抽成，不代發薪。" : product === "ws" ? "需求公司專案管理。仲介傭金不在這裡。工程師做工請用控制台。" : "請選一套機制走讀。"}</footer>
    ${s.toast ? `<div class="toast ${s.toast.err ? "err" : ""}" data-act="toast">${escToast(s.toast.text)}</div>` : ""}
  `;
}

function escToast(s: string) {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]!));
}

export function viewGate(): string {
  const pending = pendingHandoffs().length;
  return `
    <section class="hero">
      <p class="prd">兩套獨立機制 · 彼此只交換成交事件</p>
      <h1>選要走讀的產品</h1>
      <p class="muted">工程師承接後的本機控制台已經出貨，這裡不做雛型。</p>
    </section>
    <div class="gate">
      <a class="card gate-card" href="#/mkt">
        <p class="hint">下一實作</p>
        <h2>仲介平台</h2>
        <p>管理兩造會員與認證、發布專案公告、撮合應徵／邀請、合同、抽取傭金金流。</p>
        <p class="hint">做到入帳就結束。不管派工、甘特、工時。</p>
        <span class="btn-primary">進入仲介</span>
      </a>
      <a class="card gate-card" href="#/ws/war">
        <p class="hint">成交後回到自己的工具</p>
        <h2>公司工作區</h2>
        <p>需求公司的專案管理：戰情、客戶、專案、人員、派工、工時確認、毛利。可獨立開帳。</p>
        <p class="hint">${pending ? `有 ${pending} 筆仲介來件可接手。` : "也可不經仲介，自己立案。"}</p>
        <span class="btn-primary">進入工作區</span>
      </a>
    </div>
  `;
}

export function renderPage(): string {
  const r = route();
  if (r[0] === "mkt") {
    if (r[1] === "listing" && r[2]) return viewListing(r[2]);
    if (r[1] === "kyc" || r[1] === "parties") return viewParties();
    if (r[1] === "notices") return viewNotices();
    if (r[1] === "wizard") return viewWizard();
    if (r[1] === "inbox") return viewMktInbox();
    if (r[1] === "deals") return viewDeals();
    if (r[1] === "deal" && r[2]) return viewDeal(r[2]);
    return viewMktHome();
  }
  if (r[0] === "ws") {
    if (r[1] === "inbox") return viewInbox();
    if (r[1] === "projects") return viewProjects();
    if (r[1] === "project" && r[2]) return viewProject(r[2]);
    if (r[1] === "clients") return viewClients();
    if (r[1] === "people") return viewPeople();
    if (r[1] === "dispatch") return viewDispatch();
    if (r[1] === "hours") return viewHours();
    if (r[1] === "budget") return viewBudget();
    return viewWar();
  }
  return viewGate();
}

let clickBound = false;

export function bind(root: HTMLElement) {
  root.querySelector("#persona")?.addEventListener("change", (e) => {
    setPersona((e.target as HTMLSelectElement).value as MktPersona);
  });
  root.querySelector("#wsrole")?.addEventListener("change", (e) => {
    const role = (e.target as HTMLSelectElement).value as WsRole;
    setWsRole(role);
    const r = route();
    if (r[0] === "ws" && (r[1] === "war" || !r[1])) go(defaultWsPath(role));
  });
  root.querySelectorAll("[data-filter]").forEach((el) =>
    el.addEventListener("click", () => setFilter((el as HTMLElement).dataset.filter as Craft | "全部")),
  );
  root.querySelector("#ghfail")?.addEventListener("change", (e) => setGithubFail((e.target as HTMLInputElement).checked));
  root.querySelector("#w-from")?.addEventListener("change", () => readWizardFields());
  root.querySelector("#w-to")?.addEventListener("change", () => readWizardFields());
  root.querySelector("#new-proj")?.addEventListener("input", (e) => setNewProjectName((e.target as HTMLInputElement).value));
  if (clickBound) return;
  clickBound = true;
  root.addEventListener("click", (e) => {
    const t = (e.target as HTMLElement).closest("[data-act]") as HTMLElement | null;
    if (!t) return;
    const act = t.dataset.act;
    if (act === "reset") resetDemo();
    if (act === "toast") clearToast();
    if (act === "apply") applyTo(t.dataset.id!);
    if (act === "invite") invite(t.dataset.listing!, t.dataset.talent!);
    if (act === "accept") accept(t.dataset.id!);
    if (act === "kyc-talent") verifyTalent();
    if (act === "kyc-org") verifyOrg();
    if (act === "draft-craft") toggleDraftCraft(t.dataset.craft as Craft);
    if (act === "role-inc") bumpDraftRoleCount(t.dataset.craft as Craft, 1);
    if (act === "role-dec") bumpDraftRoleCount(t.dataset.craft as Craft, -1);
    if (act === "craft") {
      const tal = currentTalent();
      if (!tal) {
        showError("請先切到工程師身分再改角色標籤。");
        return;
      }
      const c = t.dataset.craft as Craft;
      setCrafts(tal.id, tal.crafts.includes(c) ? tal.crafts.filter((x) => x !== c) : [...tal.crafts, c]);
    }
    if (act === "wiz") {
      readWizardFields();
      setWizardStep(Number(t.dataset.n));
    }
    if (act === "wiz-next") {
      readWizardFields();
      setWizardStep(Number(t.dataset.n));
    }
    if (act === "publish") {
      readWizardFields();
      publishDraft();
    }
    if (act === "confirm") confirmDeal(t.dataset.deal!, t.dataset.side as "org" | "talent");
    if (act === "invoice") issueInvoice(t.dataset.deal!);
    if (act === "pay") payCommission(t.dataset.deal!);
    if (act === "handoff") emitHandoff(t.dataset.deal!);
    if (act === "accept-handoff") acceptHandoff(t.dataset.deal!);
    if (act === "confirm-ts") confirmTimesheet(t.dataset.id!);
    if (act === "return-ts") returnTimesheet(t.dataset.id!);
    if (act === "assign") cycleAssign(t.dataset.person!, Number(t.dataset.day));
    if (act === "add-project") addInternalProject();
  });
}
