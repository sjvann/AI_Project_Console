export const ROLES = ["分析", "設計", "開發", "測試"] as const;
export type Craft = (typeof ROLES)[number];

export type ProductId = "gate" | "mkt" | "ws";
export type MktPersona = "visitor" | "talent_raw" | "talent" | "demand_raw" | "demand";
export type WsRole = "exec" | "pm" | "hr";

export type RoleNeed = {
  craft: Craft;
  count: number;
  skills?: string;
  years?: string;
  onsite?: boolean;
};

export type Listing = {
  id: string;
  orgId: string;
  title: string;
  problem: string;
  inScope: string;
  outScope: string;
  roles: RoleNeed[];
  skills: string;
  years: string;
  industry: string;
  language: string;
  onsite: boolean;
  period: string;
  periodStart?: string;
  periodEnd?: string;
  budget: string;
  published: boolean;
};

export type Notice = {
  id: string;
  title: string;
  body: string;
  audience: "全員" | "需求公司" | "工程師";
  date: string;
};

export type Org = { id: string; name: string; taxId: string; owner: string; verified: boolean; industry: string };
export type Talent = { id: string; name: string; github: string; verified: boolean; crafts: Craft[]; intro: string };

export function listingPeriod(l: Pick<Listing, "period"> & { periodStart?: string; periodEnd?: string }) {
  if (l.periodStart && l.periodEnd) return `${l.periodStart} ～ ${l.periodEnd}`;
  return l.period || "";
}

export function periodHint(start?: string, end?: string) {
  if (!start || !end) return "";
  const a = new Date(`${start}T00:00:00`);
  const b = new Date(`${end}T00:00:00`);
  const days = Math.round((b.getTime() - a.getTime()) / 86400000) + 1;
  if (Number.isNaN(days) || days < 1) return "結束日不可早於開始日";
  if (days >= 27) {
    const months = Math.max(1, Math.round(days / 30.44));
    return `約 ${months} 個月（${days} 天）`;
  }
  return `約 ${days} 天`;
}

export const MKT_PERSONAS: { id: MktPersona; label: string; hint: string }[] = [
  { id: "visitor", label: "訪客", hint: "只能看公開公告" },
  { id: "talent_raw", label: "工程師·未認證", hint: "林忻尚未過 GitHub" },
  { id: "talent", label: "工程師·已認證", hint: "林忻 · 測試／開發" },
  { id: "demand_raw", label: "需求窗口·未認證", hint: "不能發布" },
  { id: "demand", label: "需求窗口·已認證", hint: "大同食品 周經理" },
];

export const WS_ROLES: { id: WsRole; label: string; hint: string }[] = [
  { id: "exec", label: "經營層", hint: "戰情室" },
  { id: "pm", label: "專案經理", hint: "專案／派工／工時確認" },
  { id: "hr", label: "人資", hint: "人員／薪資匯出" },
];

export function seedOrgs(): Org[] {
  return [
    { id: "org-tatung", name: "大同食品", taxId: "12345678", owner: "周經理", verified: true, industry: "零售／食品" },
    { id: "org-green", name: "綠能內部 IT", taxId: "87654321", owner: "許課長", verified: true, industry: "製造" },
    { id: "org-raw", name: "尚未認證的新創", taxId: "", owner: "小陳", verified: false, industry: "新創" },
  ];
}

export function seedTalents(): Talent[] {
  return [
    { id: "t-lin", name: "林忻", github: "lin-qa", verified: true, crafts: ["測試", "開發"], intro: "測試為主，能寫回歸與 API 情境。" },
    { id: "t-chen", name: "陳析", github: "chen-sa", verified: true, crafts: ["分析"], intro: "零售流程與庫存對帳。" },
    { id: "t-wang", name: "王程", github: "wang-dev", verified: true, crafts: ["開發"], intro: "C#／Blazor，能進廠區。" },
    { id: "t-raw", name: "未認證工程師", github: "", verified: false, crafts: ["開發"], intro: "尚未完成 GitHub 認證。" },
  ];
}

export function seedListings(): Listing[] {
  return [
    {
      id: "L-food",
      orgId: "org-tatung",
      title: "電商改版：訂單與庫存對得上",
      problem: "門市與官網庫存晚上對不上，客服每天對帳兩小時。",
      inScope: "訂單狀態、庫存同步、後台報表。",
      outScope: "不換金流、不做 App。",
      roles: [
        { craft: "分析", count: 1, skills: "零售流程、庫存對帳、能畫出對帳規則", years: "5 年以上" },
        { craft: "開發", count: 2, skills: "C#、PostgreSQL、既有 ERP 串接", years: "3 年以上" },
        { craft: "測試", count: 1, skills: "API／回歸、庫存與訂單情境", years: "2 年以上" },
      ],
      skills: "能讀既有 ERP 文件；週會用繁中",
      years: "見各角色",
      industry: "零售／食品",
      language: "繁中",
      onsite: false,
      period: "2026-10-01 ～ 2027-01-31",
      periodStart: "2026-10-01",
      periodEnd: "2027-01-31",
      budget: "面議",
      published: true,
    },
    {
      id: "L-green",
      orgId: "org-green",
      title: "廠區工單小系統",
      problem: "紙本工單找不到、無法追逾期。",
      inScope: "開單、派工、逾期燈。",
      outScope: "不接 SCADA。",
      roles: [
        { craft: "設計", count: 1, skills: "廠區工單畫面、現場操作動線", years: "3 年以上", onsite: true },
        { craft: "開發", count: 1, skills: "Blazor、既有 AD", years: "2 年以上", onsite: true },
      ],
      skills: "能進廠區、遵守工安簡報",
      years: "見各角色",
      industry: "製造",
      language: "繁中",
      onsite: true,
      period: "2026-09-01 ～ 2026-11-30",
      periodStart: "2026-09-01",
      periodEnd: "2026-11-30",
      budget: "時計 1,800～2,400／時",
      published: true,
    },
    {
      id: "L-clinic",
      orgId: "org-tatung",
      title: "門市預約頁（第二案）",
      problem: "電話預約漏接。",
      inScope: "日曆預約、簡訊提醒。",
      outScope: "不做看診系統。",
      roles: [{ craft: "測試", count: 1, skills: "E2E、無障礙、簡訊發送情境", years: "不拘" }],
      skills: "能對門市人員做驗收",
      years: "不拘",
      industry: "服務",
      language: "繁中",
      onsite: false,
      period: "2026-10-05 ～ 2026-11-15",
      periodStart: "2026-10-05",
      periodEnd: "2026-11-15",
      budget: "固定 18 萬",
      published: true,
    },
  ];
}

export function seedNotices(): Notice[] {
  return [
    {
      id: "N-1",
      title: "抽成規則（需求方應付）",
      body: "成交時按當時費率開立仲介費應收。範例：需求方應付專案報價的 8%；面議案以平台最低應收 TWD 24,000 計。平台不代發工程款、不託管原始碼。",
      audience: "全員",
      date: "2026-09-01",
    },
    {
      id: "N-2",
      title: "認證門檻",
      body: "加入免費。工程師須身分＋已驗證 GitHub；需求組織須統編＋負責人。未認證不能發布、應徵、成交。",
      audience: "全員",
      date: "2026-09-01",
    },
    {
      id: "N-3",
      title: "成交後請回自己的工作區",
      body: "仲介在合同確認與傭金入帳後結束。專案管理、派工、工時確認請到公司工作區。工程師做工請用本機控制台（已出貨，本雛型不模擬）。",
      audience: "需求公司",
      date: "2026-09-04",
    },
  ];
}

export type HireType = "正職" | "專案外包" | "承攬派駐";
export type WsPerson = {
  id: string;
  name: string;
  type: HireType;
  github: string;
  hoursUsed: number;
  hoursCap: number;
  status: "在職" | "停用";
  fromDealId?: string;
};
export type WsClient = { id: string; name: string; stage: "潛在" | "議約" | "合約" | "休眠"; next: string };
export type WsProject = {
  id: string;
  name: string;
  clientId: string;
  status: "立案" | "進行" | "驗收" | "結案";
  phase: string;
  start: string;
  target: string;
  inScope: string;
  repo: string;
  fromDealId?: string;
  revenue: number;
  cost: number;
};
export type WsAssignment = { personId: string; projectId: string; day: number; hours: number };
export type WsTimesheet = {
  id: string;
  personId: string;
  projectId: string;
  hours: number;
  status: "待確認" | "已確認" | "退回";
  note: string;
};

export function seedWsPeople(): WsPerson[] {
  return [
    { id: "p-chou", name: "周經理", type: "正職", github: "", hoursUsed: 0, hoursCap: 40, status: "在職" },
    { id: "p-su", name: "蘇開發", github: "su-dev", type: "正職", hoursUsed: 32, hoursCap: 40, status: "在職" },
  ];
}

export function seedWsClients(): WsClient[] {
  return [
    { id: "c-self", name: "大同食品（本組織）", stage: "合約", next: "—" },
    { id: "c-store", name: "北區門市群", stage: "議約", next: "2026-09-12" },
    { id: "c-lead", name: "南部經銷商入口", stage: "潛在", next: "2026-09-20" },
  ];
}

export function seedWsProjects(): WsProject[] {
  return [
    {
      id: "P-report",
      name: "內部週報自動化",
      clientId: "c-self",
      status: "進行",
      phase: "實作",
      start: "2026-08-01",
      target: "2026-09-30",
      inScope: "從既有 ERP 拉週報，寄給主管。",
      repo: "tatung-food/weekly-report",
      revenue: 0,
      cost: 180000,
    },
  ];
}

export function seedWsAssignments(): WsAssignment[] {
  return [
    { personId: "p-su", projectId: "P-report", day: 1, hours: 8 },
    { personId: "p-su", projectId: "P-report", day: 2, hours: 8 },
    { personId: "p-su", projectId: "P-report", day: 3, hours: 8 },
    { personId: "p-su", projectId: "P-report", day: 4, hours: 8 },
  ];
}

export function seedWsTimesheets(): WsTimesheet[] {
  return [{ id: "TS-1", personId: "p-su", projectId: "P-report", hours: 32, status: "待確認", note: "控制台上傳（本機真相）" }];
}
