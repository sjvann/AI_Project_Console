using AiProject.Company.Application;
using AiProject.Company.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiProject.Company.Infrastructure;

public static class CompanyDemoSeed
{
    public const string MarkerClientName = "晨星銀行";
    public const string SharedPassword = "Demo-Pass-2026";
    public const string CompanyName = "凌波資訊";

    public static async Task ApplyAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        if (!configuration.GetValue("Company:SeedDemoData", false))
            return;
        var clients = services.GetRequiredService<IClientRepository>();
        var accounts = services.GetRequiredService<IStaffAccountRepository>();
        if ((await clients.ListAsync(ct)).Any(c => c.Name == MarkerClientName))
            return;
        if (await accounts.GetByUserNameAsync("exec", ct) is not null)
            return;

        var now = services.GetRequiredService<IClock>().UtcNow;
        var encryptor = services.GetRequiredService<IFieldEncryptor>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var people = services.GetRequiredService<IPersonRepository>();
        var vendors = services.GetRequiredService<IVendorRepository>();
        var invites = services.GetRequiredService<IInvitationRepository>();
        var unmatched = services.GetRequiredService<IUnmatchedUploadRepository>();
        var contracts = services.GetRequiredService<IContractRepository>();
        var projects = services.GetRequiredService<IProjectRepository>();
        var assignments = services.GetRequiredService<IAssignmentRepository>();
        var timesheets = services.GetRequiredService<ITimesheetRepository>();
        var payroll = services.GetRequiredService<IPayrollRepository>();
        var settings = services.GetRequiredService<ISettingsRepository>();
        var uow = services.GetRequiredService<IUnitOfWork>();
        var staffing = new ContractStaffingPolicy();
        var hash = hasher.Hash(SharedPassword);

        var company = await settings.GetAsync(ct);
        company.Update(CompanyName, "Asia/Taipei", "TWD", 25m, 10m, false);
        company.SetExchangeRate("USD", 32.5m, new DateOnly(2026, 9, 1));

        var vendor = Vendor.Create("迅馳科技", "54881234", new DateOnly(2025, 1, 1), null);
        vendor.Update(vendor.Name, vendor.TaxId, null, vendor.ValidFrom, vendor.ValidTo, encryptor.EncryptDecimal(900));
        await vendors.AddAsync(vendor, ct);

        var exec = await StaffAsync(people, accounts, encryptor, "林總經理", "exec", PlatformRole.Exec, EmploymentKind.FullTime, null, hash, now, 180000, null, ["管理"], "經營", ct);
        var delivery = await StaffAsync(people, accounts, encryptor, "黃交付", "delivery", PlatformRole.Delivery, EmploymentKind.FullTime, null, hash, now, 140000, null, ["交付"], "交付", ct);
        var pm = await StaffAsync(people, accounts, encryptor, "周專案", "pm", PlatformRole.Pm, EmploymentKind.FullTime, null, hash, now, 110000, null, ["pm", "分析"], "專案", ct);
        var hr = await StaffAsync(people, accounts, encryptor, "吳人資", "hr", PlatformRole.Hr, EmploymentKind.FullTime, null, hash, now, 85000, null, ["hr"], "人資", ct);
        var finance = await StaffAsync(people, accounts, encryptor, "鄭財務", "finance", PlatformRole.Finance, EmploymentKind.FullTime, null, hash, now, 95000, null, ["財務"], "財務", ct);
        var window = await StaffAsync(people, accounts, encryptor, "張窗口", "vendor", PlatformRole.VendorAdmin, EmploymentKind.VendorStaff, vendor.Id, hash, now, null, 800, ["協調"], "外包", ct);
        vendor.Update(vendor.Name, vendor.TaxId, window.Id, vendor.ValidFrom, vendor.ValidTo, encryptor.EncryptDecimal(900));

        var wang = await EngineerAsync(people, invites, "王工程", "wang-dev", PlatformRole.Engineer, EmploymentKind.FullTime, null, now, ["blazor", "csharp"], "工程", true, ct);
        var li = await EngineerAsync(people, invites, "李析", "li-analyst", PlatformRole.Engineer, EmploymentKind.Freelance, null, now, ["分析", "blazor"], "招募", true, ct);
        var lead = await EngineerAsync(people, invites, "趙領航", "chao-lead", PlatformRole.Lead, EmploymentKind.FullTime, null, now, ["架構"], "工程", true, ct);
        var chen = await EngineerAsync(people, invites, "陳外包", "chen-vendor", PlatformRole.VendorEngineer, EmploymentKind.VendorStaff, vendor.Id, now, ["dba"], "外包", true, ct);
        await EngineerAsync(people, invites, "newhire-dev", "newhire-dev", PlatformRole.Engineer, EmploymentKind.Freelance, null, now, ["frontend"], "招募", false, ct);
        var idle = Person.Create("錢待案", EmploymentKind.FullTime, null, now);
        idle.UpdateProfile("錢待案", "qian@lingbo.test", "工程", wang.Id, 40, ["java"]);
        idle.SetCompensationCiphers(encryptor.EncryptDecimal(70000), null);
        await people.AddAsync(idle, ct);

        wang.SetCompensationCiphers(encryptor.EncryptDecimal(90000), encryptor.EncryptDecimal(650));
        li.SetCompensationCiphers(null, encryptor.EncryptDecimal(1200));
        lead.SetCompensationCiphers(encryptor.EncryptDecimal(120000), null);
        chen.SetCompensationCiphers(null, encryptor.EncryptDecimal(900));
        li.ReplaceUnavailable([new UnavailableRange(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 19), "教育訓練")]);

        var bank = Client.Create(MarkerClientName, ClientKind.External, "林協理");
        bank.UpdateProfile(bank.Name, bank.Kind, bank.Contact, "02-5555-1000", "lin@morningstar.test", ClientSource.Existing, null, pm.Id);
        bank.AddActivity(now.AddDays(-40), "周專案", ClientActivityKind.Meeting, "需求凍結會議，確認帳務批次窗口。", null);
        bank.AddActivity(now.AddDays(-10), "周專案", ClientActivityKind.Call, "上線前週會，催驗收環境。", null);
        var retail = Client.Create("綠洲零售", ClientKind.External, "高店長");
        retail.UpdateProfile(retail.Name, retail.Kind, retail.Contact, "07-333-2000", "kao@oasis.test", ClientSource.Referral, null, delivery.Id);
        var internalIt = Client.Create("內部資訊室", ClientKind.Internal, "資訊長");
        internalIt.UpdateProfile(internalIt.Name, internalIt.Kind, internalIt.Contact, null, "cio@lingbo.test", ClientSource.Existing, null, pm.Id);
        var leadClient = Client.Create("北辰證券", ClientKind.External, "李襄理", ClientLifecycle.Lead, ClientSource.SelfDeveloped);
        leadClient.UpdateProfile(leadClient.Name, leadClient.Kind, leadClient.Contact, "02-8888-3000", "li@northstar.test", ClientSource.SelfDeveloped, new DateOnly(2026, 8, 1), pm.Id);
        leadClient.AddActivity(now.AddDays(-20), "周專案", ClientActivityKind.Call, "來電詢問核心帳務改版經驗，約下週訪談。", new DateOnly(2026, 8, 1));
        leadClient.AddActivity(now.AddDays(-5), "周專案", ClientActivityKind.RequirementNote, "對方想先看需求工作台與進件流程示範。", new DateOnly(2026, 8, 1));
        var proposal = Client.Create("雲端農場", ClientKind.External, "陳執行長", ClientLifecycle.Proposal, ClientSource.Recruit);
        proposal.UpdateProfile(proposal.Name, proposal.Kind, proposal.Contact, "04-222-1100", "chen@cloudfarm.test", ClientSource.Recruit, new DateOnly(2026, 9, 10), pm.Id);
        proposal.AddActivity(now.AddDays(-12), "周專案", ClientActivityKind.Meeting, "需求訪談完成，盤點會員與倉儲兩條線。", new DateOnly(2026, 8, 28));
        proposal.AddActivity(now.AddDays(-3), "周專案", ClientActivityKind.ProposalSent, "已送出會員中台提案，等對方回報時程。", new DateOnly(2026, 9, 10));
        await clients.AddAsync(bank, ct);
        await clients.AddAsync(retail, ct);
        await clients.AddAsync(internalIt, ct);
        await clients.AddAsync(leadClient, ct);
        await clients.AddAsync(proposal, ct);

        var bankContract = Contract.Create(bank.Id, "核心帳務改版", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31), 4_800_000, "TWD", PricingKind.FixedPrice, [vendor.Id]);
        var retailContract = Contract.Create(retail.Id, "會員中台建置", new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31), 80_000, "USD", PricingKind.TimeAndMaterials, []);
        var internalContract = Contract.Create(internalIt.Id, "戰情儀表", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), 1_200_000, "TWD", PricingKind.Mixed, []);
        await contracts.AddAsync(bankContract, ct);
        await contracts.AddAsync(retailContract, ct);
        await contracts.AddAsync(internalContract, ct);

        var core = Project.Create(bankContract.Id, "晨星．核心帳務", new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 15), RevenueMethod.Milestone, now);
        core.Update(core.Name, core.Start, core.TargetEnd, core.RevenueMethod, null, false, encryptor.EncryptDecimal(4200));
        var freeze = core.AddMilestone("需求凍結", new DateOnly(2026, 4, 15), 800_000, null);
        freeze.MarkCompleted(true);
        freeze.RecognizeAll();
        var goLive = core.AddMilestone("上線", new DateOnly(2026, 8, 1), 2_000_000, freeze.Id);
        core.AddMilestone("保固結束", new DateOnly(2026, 12, 15), 2_000_000, goLive.Id);
        core.AddOtherExpense("雲端主機", 120_000, 98_000, true);
        var implement = core.Phases.FirstOrDefault(p => p.Name == "實作");
        if (implement is not null)
            core.EnterPhase(implement.Id, now);
        core.AddJournal(now.AddDays(-90), "周專案", ProjectJournalKind.Established, "從晨星銀行成立專案 晨星．核心帳務");
        core.AddJournal(now.AddDays(-2), "周專案", ProjectJournalKind.PhaseEntered, "進入階段 實作");

        var member = Project.Create(retailContract.Id, "綠洲．會員中台", new DateOnly(2026, 6, 1), new DateOnly(2026, 11, 30), RevenueMethod.TimeAndMaterials, now);
        member.Update(member.Name, member.Start, member.TargetEnd, member.RevenueMethod, null, false, encryptor.EncryptDecimal(1800));
        var design = member.Phases.FirstOrDefault(p => p.Name == "設計");
        if (design is not null)
            member.EnterPhase(design.Id, now);

        var dashboard = Project.Create(internalContract.Id, "內部．戰情儀表", new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 31), RevenueMethod.StraightLine, now);
        dashboard.Update(dashboard.Name, dashboard.Start, dashboard.TargetEnd, dashboard.RevenueMethod, null, true, null);
        var verify = dashboard.Phases.FirstOrDefault(p => p.Name == "驗證");
        if (verify is not null)
            dashboard.EnterPhase(verify.Id, now);

        await projects.AddAsync(core, ct);
        await projects.AddAsync(member, ct);
        await projects.AddAsync(dashboard, ct);

        var weekStart = new DateOnly(2026, 9, 1);
        var weekEnd = new DateOnly(2026, 9, 12);
        var hours = new Dictionary<Guid, decimal>();
        await AddAssignment(assignments, staffing, hours, wang, core, bankContract, weekStart, weekEnd, 32, AssignmentRole.Engineer, null, now, ct);
        await AddAssignment(assignments, staffing, hours, chen, core, bankContract, weekStart, weekEnd, 20, AssignmentRole.Engineer, null, now, ct);
        await AddAssignment(assignments, staffing, hours, pm, core, bankContract, weekStart, weekEnd, 8, AssignmentRole.Pm, null, now, ct);
        await AddAssignment(assignments, staffing, hours, wang, dashboard, internalContract, weekStart, weekEnd, 16, AssignmentRole.Engineer, "晨星趕驗收，內部儀表並行", now, ct);
        await AddAssignment(assignments, staffing, hours, li, member, retailContract, weekStart, weekEnd, 30, AssignmentRole.Analyst, null, now, ct);
        await AddAssignment(assignments, staffing, hours, lead, dashboard, internalContract, weekStart, weekEnd, 20, AssignmentRole.Lead, null, now, ct);
        await AddAssignment(assignments, staffing, hours, delivery, member, retailContract, weekStart, weekEnd, 6, AssignmentRole.Pm, null, now, ct);

        await AddSheet(timesheets, "demo-wang-0901", wang.Id, core.Id, new DateOnly(2026, 9, 1), 8, [101], true, now, ct);
        await AddSheet(timesheets, "demo-wang-0902", wang.Id, core.Id, new DateOnly(2026, 9, 2), 8, [101], true, now, ct);
        await AddSheet(timesheets, "demo-wang-0903", wang.Id, dashboard.Id, new DateOnly(2026, 9, 3), 8, [], false, now, ct);
        await AddSheet(timesheets, "demo-chen-0901", chen.Id, core.Id, new DateOnly(2026, 9, 1), 8, [102], true, now, ct);
        await AddSheet(timesheets, "demo-li-0901", li.Id, member.Id, new DateOnly(2026, 9, 1), 7, [201], false, now, ct);
        var returned = Timesheet.Upload("demo-li-0902", li.Id, member.Id, new DateOnly(2026, 9, 2), 8, [201], [], false, null, now);
        returned.ReturnToEngineer("請補狀態圖強度。");
        await timesheets.AddAsync(returned, ct);
        await unmatched.AddAsync(UnmatchedUpload.Capture("ghost-coder", "slot-ghost", """{"localSlotId":"slot-ghost","hours":8}""", now), ct);

        var period = PayrollPeriod.Open(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        period.ReplaceLines(
        [
            new PayrollLine(wang.Id, PayrollLineKind.Monthly, 90000, 90000, null, 16, "正職月薪", true),
            new PayrollLine(wang.Id, PayrollLineKind.Monthly, 0, 60000, core.Id, 16, "正職成本分攤", false),
            new PayrollLine(li.Id, PayrollLineKind.Hourly, 8400, 8400, member.Id, 7, "核准小時 × 費率（待 PM 確認後會重算）", true),
            new PayrollLine(chen.Id, PayrollLineKind.Hourly, 7200, 7200, core.Id, 8, "外包時計", true),
        ]);
        await payroll.AddAsync(period, ct);
        await uow.SaveChangesAsync(ct);
    }

    static async Task<Person> StaffAsync(
        IPersonRepository people,
        IStaffAccountRepository accounts,
        IFieldEncryptor encryptor,
        string name,
        string userName,
        PlatformRole role,
        EmploymentKind kind,
        Guid? vendorId,
        string hash,
        DateTimeOffset now,
        decimal? salary,
        decimal? hourly,
        IEnumerable<string> skills,
        string department,
        CancellationToken ct)
    {
        var person = Person.Create(name, kind, vendorId, now);
        person.UpdateProfile(name, userName + "@lingbo.test", department, null, 40, skills);
        person.SetCompensationCiphers(
            salary is decimal s ? encryptor.EncryptDecimal(s) : null,
            hourly is decimal h ? encryptor.EncryptDecimal(h) : null);
        await people.AddAsync(person, ct);
        var created = StaffAccount.Create(userName, person.Id, role, hash, now);
        if (!created.Ok)
            throw new DomainException(created.Code, created.Message);
        await accounts.AddAsync(created.Value!, ct);
        return person;
    }

    static async Task<Person> EngineerAsync(
        IPersonRepository people,
        IInvitationRepository invites,
        string name,
        string github,
        PlatformRole role,
        EmploymentKind kind,
        Guid? vendorId,
        DateTimeOffset now,
        IEnumerable<string> skills,
        string department,
        bool accepted,
        CancellationToken ct)
    {
        var person = Person.Create(name, kind, vendorId, now);
        person.UpdateProfile(name, github + "@lingbo.test", department, null, 40, skills);
        person.BindGitHub(github, _ => null).ThrowIfFailed();
        await people.AddAsync(person, ct);
        var invite = Invitation.Create(github, role, vendorId, now);
        invite.BindPerson(person.Id);
        if (accepted)
            invite.Accept(now);
        await invites.AddAsync(invite, ct);
        return person;
    }

    static async Task AddAssignment(
        IAssignmentRepository assignments,
        IContractStaffingPolicy staffing,
        Dictionary<Guid, decimal> hours,
        Person person,
        Project project,
        Contract contract,
        DateOnly start,
        DateOnly end,
        decimal weekly,
        AssignmentRole role,
        string? forceReason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        hours.TryGetValue(person.Id, out var already);
        var cap = person.WeeklyHourCap;
        var projected = already + weekly;
        var avail = new PersonAvailability(person.Id, cap, already, cap - already, projected > cap);
        var created = Assignment.Create(person, project, contract, start, end, weekly, role, AssignmentSource.Manual, staffing, avail, forceReason, forceReason is not null, false, now);
        if (!created.Ok)
            throw new DomainException(created.Code, created.Message);
        created.Value!.AttachIssues(role == AssignmentRole.Engineer ? [101] : []);
        await assignments.AddAsync(created.Value, ct);
        hours[person.Id] = projected;
    }

    static async Task AddSheet(
        ITimesheetRepository timesheets,
        string slot,
        Guid personId,
        Guid projectId,
        DateOnly date,
        decimal hours,
        IEnumerable<int> issues,
        bool confirm,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var chart = new[] { new StatusChartCell(projectId, date, confirm ? 2 : 1, confirm ? "進行中" : "待補") };
        var sheet = Timesheet.Upload(slot, personId, projectId, date, hours, issues, chart, false, null, now);
        if (confirm)
            sheet.Confirm(now);
        await timesheets.AddAsync(sheet, ct);
    }
}
