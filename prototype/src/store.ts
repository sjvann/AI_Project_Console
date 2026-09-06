import {
  seedListings,
  seedNotices,
  seedOrgs,
  seedTalents,
  seedWsAssignments,
  seedWsClients,
  seedWsPeople,
  seedWsProjects,
  seedWsTimesheets,
  type Craft,
  type Listing,
  type MktPersona,
  type Notice,
  type Org,
  type Talent,
  type WsAssignment,
  type WsClient,
  type WsPerson,
  type WsProject,
  type WsRole,
  type WsTimesheet,
} from "./data";

export type Application = { id: string; listingId: string; talentId: string; status: "applied" | "invited" | "accepted" };
export type PayState = "待合同" | "待開立" | "待付款" | "已入帳";
export type Deal = {
  id: string;
  listingId: string;
  talentId: string;
  orgConfirmed: boolean;
  talentConfirmed: boolean;
  receivable: number;
  pay: PayState;
  paidAt?: string;
  handedOff: boolean;
};
export type Handoff = {
  dealId: string;
  listingId: string;
  talentId: string;
  orgName: string;
  title: string;
  inScope: string;
  github: string;
  talentName: string;
  accepted: boolean;
};

export type State = {
  persona: MktPersona;
  wsRole: WsRole;
  filter: Craft | "全部";
  wizardStep: number;
  draft: Partial<Listing>;
  orgs: Org[];
  talents: Talent[];
  listings: Listing[];
  notices: Notice[];
  applications: Application[];
  deals: Deal[];
  githubFail: boolean;
  toast: { text: string; err?: boolean } | null;
  people: WsPerson[];
  clients: WsClient[];
  projects: WsProject[];
  assignments: WsAssignment[];
  timesheets: WsTimesheet[];
  handoffs: Handoff[];
  newProjectName: string;
};

const KEY = "ai-project-prototype-v2";

function initial(): State {
  return {
    persona: "visitor",
    wsRole: "exec",
    filter: "全部",
    wizardStep: 1,
    draft: { roles: [], onsite: false, budget: "面議" },
    orgs: seedOrgs(),
    talents: seedTalents(),
    listings: seedListings(),
    notices: seedNotices(),
    applications: [{ id: "A-1", listingId: "L-green", talentId: "t-wang", status: "applied" }],
    deals: [],
    githubFail: false,
    toast: null,
    people: seedWsPeople(),
    clients: seedWsClients(),
    projects: seedWsProjects(),
    assignments: seedWsAssignments(),
    timesheets: seedWsTimesheets(),
    handoffs: [],
    newProjectName: "",
  };
}

let state: State = load();
const listeners = new Set<() => void>();

function load(): State {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) return initial();
    return { ...initial(), ...JSON.parse(raw), toast: null };
  } catch {
    return initial();
  }
}

function persist() {
  const { toast: _t, ...rest } = state;
  sessionStorage.setItem(KEY, JSON.stringify(rest));
}

export function getState(): State {
  return state;
}

export function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

function commit(patch: Partial<State> | ((s: State) => State), toast?: { text: string; err?: boolean }) {
  state = typeof patch === "function" ? patch(state) : { ...state, ...patch };
  if (toast) state = { ...state, toast };
  persist();
  listeners.forEach((f) => f());
}

export function setPersona(persona: MktPersona) {
  commit((s) => ({
    ...s,
    persona,
    toast: null,
    talents: s.talents.map((t) =>
      t.id === "t-lin"
        ? { ...t, verified: persona === "talent_raw" ? false : persona === "talent" ? true : t.verified }
        : t,
    ),
    orgs: s.orgs.map((o) =>
      o.id === "org-tatung"
        ? { ...o, verified: persona === "demand_raw" ? false : persona === "demand" ? true : o.verified }
        : o,
    ),
  }));
}

export function setWsRole(wsRole: WsRole) {
  commit({ wsRole });
}

export function setFilter(filter: State["filter"]) {
  commit({ filter });
}

export function clearToast() {
  commit({ toast: null });
}

export function showError(text: string) {
  commit({}, { text, err: true });
}

export function orgOf(id: string) {
  return state.orgs.find((o) => o.id === id);
}

export function talentOf(id: string) {
  return state.talents.find((t) => t.id === id);
}

export function currentTalent() {
  if (state.persona === "talent_raw" || state.persona === "talent") {
    return state.talents.find((t) => t.id === "t-lin")!;
  }
  return null;
}

export function currentOrg() {
  if (state.persona === "demand_raw" || state.persona === "demand") {
    return state.orgs.find((o) => o.id === "org-tatung")!;
  }
  return null;
}

export function canPublish() {
  return currentOrg()?.verified === true;
}

export function canApply() {
  return currentTalent()?.verified === true;
}

export function pendingHandoffs() {
  return state.handoffs.filter((h) => !h.accepted);
}

export function verifyTalent() {
  if (state.githubFail) {
    showError("GitHub 帳號對不上這位工程師。請用 gh auth 登入後再試，不要只填暱稱。");
    return;
  }
  const t = currentTalent();
  if (!t) return;
  commit(
    (s) => ({
      ...s,
      talents: s.talents.map((x) => (x.id === t.id ? { ...x, verified: true, github: x.github || "lin-qa" } : x)),
      persona: "talent",
    }),
    { text: "認證通過。現在可以應徵。認證已寫進審計（雛型紀錄）。" },
  );
}

export function verifyOrg() {
  const o = currentOrg();
  if (!o) {
    showError("請先切到需求窗口身分，再核對這家公司。");
    return;
  }
  commit(
    (s) => ({
      ...s,
      orgs: s.orgs.map((x) => (x.id === o.id ? { ...x, taxId: x.taxId || "12345678", verified: true } : x)),
      persona: "demand",
    }),
    { text: "統編與負責人已核對。現在可以發布專案公告。" },
  );
}

export function setGithubFail(v: boolean) {
  commit({ githubFail: v });
}

export function setDraft(partial: Partial<Listing>) {
  commit({ draft: { ...state.draft, ...partial } });
}

export function setWizardStep(n: number) {
  commit({ wizardStep: n });
}

export function toggleDraftCraft(craft: Craft) {
  const roles = [...(state.draft.roles ?? [])];
  const i = roles.findIndex((r) => r.craft === craft);
  if (i >= 0) roles.splice(i, 1);
  else roles.push({ craft, count: 1 });
  setDraft({ roles });
}

export function bumpDraftRoleCount(craft: Craft, delta: number) {
  const roles = [...(state.draft.roles ?? [])];
  const i = roles.findIndex((r) => r.craft === craft);
  if (i < 0) return;
  roles[i] = { ...roles[i], count: Math.min(20, Math.max(1, roles[i].count + delta)) };
  setDraft({ roles });
}

export function publishDraft() {
  if (!canPublish()) {
    showError("這家公司還沒完成統編與負責人認證，不能發布公告。加入免費，但認證要過。");
    return;
  }
  const d = state.draft;
  if (!d.title || !d.problem || !d.inScope || !d.outScope || !d.roles?.length || !d.budget) {
    showError("精靈還有必填欄沒寫完：問題、範圍／非範圍、角色、期間、預算或面議。");
    return;
  }
  if (!d.periodStart || !d.periodEnd) {
    showError("期間請用日曆選開始日與結束日，不要只打文字。");
    return;
  }
  if (d.periodEnd < d.periodStart) {
    showError("結束日不可早於開始日。");
    return;
  }
  const period = `${d.periodStart} ～ ${d.periodEnd}`;
  const listing: Listing = {
    id: "L-" + Date.now().toString(36),
    orgId: currentOrg()!.id,
    title: d.title,
    problem: d.problem,
    inScope: d.inScope,
    outScope: d.outScope,
    roles: d.roles,
    skills: d.skills || "未填",
    years: d.years || "不拘",
    industry: d.industry || "未填",
    language: d.language || "繁中",
    onsite: !!d.onsite,
    period,
    periodStart: d.periodStart,
    periodEnd: d.periodEnd,
    budget: d.budget,
    published: true,
  };
  commit(
    (s) => ({
      ...s,
      listings: [listing, ...s.listings],
      draft: { roles: [], onsite: false, budget: "面議" },
      wizardStep: 1,
    }),
    { text: "已刊登專案公告。沒有寫進 Git。工程師可在公告板看到。" },
  );
  location.hash = "#/mkt/listing/" + listing.id;
}

export function applyTo(listingId: string) {
  const t = currentTalent();
  if (!t) {
    showError("請先以工程師身分加入。訪客只能看公開公告，不能應徵。");
    return;
  }
  if (!t.verified) {
    showError("你的 GitHub 還沒認證通過，不能應徵。請到兩造／認證頁完成。");
    return;
  }
  if (state.applications.some((a) => a.listingId === listingId && a.talentId === t.id)) {
    showError("這則公告你已經應徵或受邀了。");
    return;
  }
  commit(
    (s) => ({
      ...s,
      applications: [...s.applications, { id: "A-" + Date.now().toString(36), listingId, talentId: t.id, status: "applied" }],
    }),
    { text: "已應徵。等公司接受後才算成交，不是自動媒合。" },
  );
}

export function invite(listingId: string, talentId: string) {
  if (!canPublish()) {
    showError("未認證的需求窗口不能邀請。");
    return;
  }
  commit(
    (s) => ({
      ...s,
      applications: [
        ...s.applications.filter((a) => !(a.listingId === listingId && a.talentId === talentId)),
        { id: "I-" + Date.now().toString(36), listingId, talentId, status: "invited" },
      ],
    }),
    { text: "已送出邀請。對方接受後才成交。" },
  );
}

function commissionOf(listing: Listing) {
  if (listing.budget === "面議") return 24000;
  const n = listing.budget.replace(/[^\d]/g, "");
  const amount = Number(n);
  if (!amount) return 14400;
  return Math.max(14400, Math.round(amount * 0.08));
}

export function accept(appId: string) {
  const app = state.applications.find((a) => a.id === appId);
  if (!app) return;
  const listing = state.listings.find((l) => l.id === app.listingId)!;
  const deal: Deal = {
    id: "D-" + Date.now().toString(36),
    listingId: app.listingId,
    talentId: app.talentId,
    orgConfirmed: false,
    talentConfirmed: false,
    receivable: commissionOf(listing),
    pay: "待合同",
    handedOff: false,
  };
  commit(
    (s) => ({
      ...s,
      applications: s.applications.map((a) => (a.id === appId ? { ...a, status: "accepted" } : a)),
      deals: [...s.deals, deal],
    }),
    { text: `成交 ${deal.id}。當事人是公司 ↔ 工程師。接下來確認合同、開立仲介費、付款。平台不代發薪。` },
  );
  location.hash = "#/mkt/deal/" + deal.id;
}

export function confirmDeal(dealId: string, side: "org" | "talent") {
  commit((s) => ({
    ...s,
    deals: s.deals.map((d) => {
      if (d.id !== dealId) return d;
      const next = {
        ...d,
        orgConfirmed: side === "org" ? true : d.orgConfirmed,
        talentConfirmed: side === "talent" ? true : d.talentConfirmed,
      };
      const both = next.orgConfirmed && next.talentConfirmed;
      return { ...next, pay: both && next.pay === "待合同" ? "待開立" : next.pay };
    }),
  }), { text: "已在平台留下確認紀錄。電子簽可接第三方，第一刀這樣算。" });
}

export function issueInvoice(dealId: string) {
  const d = state.deals.find((x) => x.id === dealId);
  if (!d) return;
  if (!(d.orgConfirmed && d.talentConfirmed)) {
    showError("雙方還沒確認合同，不能開立仲介費。");
    return;
  }
  commit(
    (s) => ({
      ...s,
      deals: s.deals.map((x) => (x.id === dealId ? { ...x, pay: "待付款" } : x)),
    }),
    { text: `已開立仲介費應收 TWD ${d.receivable.toLocaleString()}。這是平台抽成，不是工程款。` },
  );
}

export function payCommission(dealId: string) {
  const d = state.deals.find((x) => x.id === dealId);
  if (!d || d.pay !== "待付款") {
    showError("請先開立仲介費應收，再模擬付款。");
    return;
  }
  commit(
    (s) => ({
      ...s,
      deals: s.deals.map((x) =>
        x.id === dealId ? { ...x, pay: "已入帳", paidAt: "2026-09-06 21:40" } : x,
      ),
    }),
    { text: "傭金已入帳（雛型模擬第三方金流）。仲介主流程結束。請回公司工作區接手專案管理。" },
  );
}

export function emitHandoff(dealId: string) {
  const d = state.deals.find((x) => x.id === dealId);
  if (!d) return;
  if (d.pay !== "已入帳") {
    showError("傭金尚未入帳。仲介在抽成入帳後才把成交事件交給工作區。");
    return;
  }
  if (d.handedOff) {
    location.hash = "#/ws/inbox";
    return;
  }
  const listing = state.listings.find((l) => l.id === d.listingId)!;
  const t = talentOf(d.talentId)!;
  const org = orgOf(listing.orgId)!;
  const handoff: Handoff = {
    dealId: d.id,
    listingId: listing.id,
    talentId: t.id,
    orgName: org.name,
    title: listing.title,
    inScope: listing.inScope,
    github: t.github,
    talentName: t.name,
    accepted: false,
  };
  commit(
    (s) => ({
      ...s,
      deals: s.deals.map((x) => (x.id === dealId ? { ...x, handedOff: true } : x)),
      handoffs: s.handoffs.some((h) => h.dealId === dealId) ? s.handoffs : [...s.handoffs, handoff],
    }),
    { text: "已把成交事件交給公司工作區。仲介不再管派工與工時。請到工作區「仲介來件」接手。" },
  );
  location.hash = "#/ws/inbox";
}

export function acceptHandoff(dealId: string) {
  const h = state.handoffs.find((x) => x.dealId === dealId);
  if (!h || h.accepted) return;
  const personId = "p-" + h.talentId;
  const projectId = "P-" + h.listingId;
  commit(
    (s) => ({
      ...s,
      handoffs: s.handoffs.map((x) => (x.dealId === dealId ? { ...x, accepted: true } : x)),
      people: s.people.some((p) => p.id === personId)
        ? s.people
        : [
            ...s.people,
            {
              id: personId,
              name: h.talentName,
              type: "專案外包",
              github: h.github,
              hoursUsed: 0,
              hoursCap: 40,
              status: "在職",
              fromDealId: dealId,
            },
          ],
      projects: s.projects.some((p) => p.id === projectId)
        ? s.projects
        : [
            ...s.projects,
            {
              id: projectId,
              name: h.title,
              clientId: "c-self",
              status: "立案",
              phase: "需求",
              start: "2026-10-01",
              target: "2027-01-31",
              inScope: h.inScope,
              repo: "tatung-food/" + h.listingId.toLowerCase(),
              fromDealId: dealId,
              revenue: 800000,
              cost: 0,
            },
          ],
      timesheets: s.timesheets.some((t) => t.id === "TS-" + dealId)
        ? s.timesheets
        : [
            ...s.timesheets,
            {
              id: "TS-" + dealId,
              personId: personId,
              projectId,
              hours: 6,
              status: "待確認",
              note: "控制台上傳（本機真相）。本雛型不模擬工程師繪圖。",
            },
          ],
    }),
    { text: "已接手。專案與名冊已建立。工程師請用控制台做工；這裡只做管理。" },
  );
  location.hash = "#/ws/project/" + projectId;
}

export function confirmTimesheet(id: string) {
  commit(
    (s) => ({
      ...s,
      timesheets: s.timesheets.map((t) => (t.id === id ? { ...t, status: "已確認" } : t)),
    }),
    { text: "PM 已確認歸屬。不核定薪水金額。人資可進薪資週期匯出。" },
  );
}

export function returnTimesheet(id: string) {
  commit(
    (s) => ({
      ...s,
      timesheets: s.timesheets.map((t) => (t.id === id ? { ...t, status: "退回", note: "請到控制台改狀態圖後再送" } : t)),
    }),
    { text: "已退回。工程師在控制台改完再上傳。工作區不重做繪圖。" },
  );
}

export function cycleAssign(personId: string, day: number) {
  const hoursCycle = [0, 6, 8, 10];
  commit((s) => {
    const cur = s.assignments.find((a) => a.personId === personId && a.day === day);
    const idx = cur ? hoursCycle.indexOf(cur.hours) : 0;
    const nextH = hoursCycle[(idx + 1) % hoursCycle.length];
    const rest = s.assignments.filter((a) => !(a.personId === personId && a.day === day));
    const projectId = s.projects.find((p) => p.status === "進行" || p.status === "立案")?.id ?? s.projects[0]?.id;
    const next = nextH === 0 || !projectId ? rest : [...rest, { personId, projectId, day, hours: nextH }];
    const used = next.filter((a) => a.personId === personId).reduce((n, a) => n + a.hours, 0);
    return {
      ...s,
      assignments: next,
      people: s.people.map((p) => (p.id === personId ? { ...p, hoursUsed: used } : p)),
    };
  });
}

export function setNewProjectName(newProjectName: string) {
  commit({ newProjectName });
}

export function addInternalProject() {
  const name = state.newProjectName.trim();
  if (!name) {
    showError("請填專案名稱。這是公司自己開的案，不必先走仲介。");
    return;
  }
  const id = "P-" + Date.now().toString(36);
  commit(
    (s) => ({
      ...s,
      projects: [
        ...s.projects,
        {
          id,
          name,
          clientId: "c-self",
          status: "立案",
          phase: "需求",
          start: "2026-09-06",
          target: "2026-12-31",
          inScope: "公司自行立案。",
          repo: "",
          revenue: 0,
          cost: 0,
        },
      ],
      newProjectName: "",
    }),
    { text: "已立案。不必經仲介。工程師承接後用控制台做工。" },
  );
}

export function resetDemo() {
  sessionStorage.removeItem(KEY);
  state = initial();
  listeners.forEach((f) => f());
  location.hash = "#/";
}

export function setCrafts(talentId: string, crafts: Craft[]) {
  commit({
    talents: state.talents.map((t) => (t.id === talentId ? { ...t, crafts } : t)),
  });
}

export function personOf(id: string) {
  return state.people.find((p) => p.id === id);
}

export function projectOf(id: string) {
  return state.projects.find((p) => p.id === id);
}

export function clientOf(id: string) {
  return state.clients.find((c) => c.id === id);
}

export function weekHours(personId: string) {
  return state.assignments.filter((a) => a.personId === personId).reduce((n, a) => n + a.hours, 0);
}
