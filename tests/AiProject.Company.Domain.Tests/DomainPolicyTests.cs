using AiProject.Company.Domain;

namespace AiProject.Company.Domain.Tests;

public class DomainPolicyTests
{
    [Fact]
    public void Monthly_salary_payable_equals_salary_cost_allocated()
    {
        var person = Person.Create("王工程", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.SetCompensationCiphers("x", null);
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var sheets = new[]
        {
            Timesheet.Upload("a", person.Id, projectA, new DateOnly(2026, 9, 1), 20, [], [], false, null, DateTimeOffset.UtcNow),
            Timesheet.Upload("b", person.Id, projectB, new DateOnly(2026, 9, 2), 20, [], [], false, null, DateTimeOffset.UtcNow),
        };
        foreach (var s in sheets)
            s.Confirm(DateTimeOffset.UtcNow);
        var calc = new MonthlySalaryCalculator();
        var line = calc.CalculateWithSalary(person, 80000, sheets);
        Assert.Equal(80000, line.Amount);
        var cost = calc.AllocateCost(person, 80000, sheets, [], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(2, cost.Count);
        Assert.Equal(80000, cost.Sum(c => c.CostAmount));
        Assert.All(cost, c => Assert.False(c.Payable));
    }

    [Fact]
    public void Exempt_staff_not_allocated()
    {
        var person = Person.Create("管理", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.SetCostAllocationExempt(true);
        var calc = new MonthlySalaryCalculator();
        var cost = calc.AllocateCost(person, 90000, [], [], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Empty(cost);
    }

    [Fact]
    public void Hourly_unapproved_not_paid_overtime_pending()
    {
        var person = Person.Create("外包", EmploymentKind.Freelance, null, DateTimeOffset.UtcNow);
        var approved = Timesheet.Upload("ok", person.Id, Guid.NewGuid(), new DateOnly(2026, 9, 1), 6, [], [], false, null, DateTimeOffset.UtcNow);
        approved.Confirm(DateTimeOffset.UtcNow);
        var pending = Timesheet.Upload("wait", person.Id, Guid.NewGuid(), new DateOnly(2026, 9, 2), 8, [], [], false, null, DateTimeOffset.UtcNow);
        var overtimeDay = Timesheet.Upload("ot", person.Id, Guid.NewGuid(), new DateOnly(2026, 9, 3), 11, [], [], false, null, DateTimeOffset.UtcNow);
        overtimeDay.Confirm(DateTimeOffset.UtcNow);
        var calc = new HourlyCalculator();
        var (payable, overtime) = calc.CalculateWithRate(person, 1000, [approved, overtimeDay], 8);
        Assert.Equal(6 + 8, payable.Hours);
        Assert.Equal(14000, payable.Amount);
        Assert.NotNull(overtime);
        Assert.Equal(3, overtime!.Hours);
        Assert.False(overtime.Payable);
        Assert.Equal(0, calc.CalculateWithRate(person, 1000, [pending], 8).Payable.Amount);
    }

    [Fact]
    public void Bonus_not_in_pay_until_payable()
    {
        var person = Person.Create("王", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        var calc = new ProjectBonusCalculator();
        var lines = calc.Expand(person, [new ProjectBonusGrant(person.Id, Guid.NewGuid(), 20000, false)]);
        Assert.False(lines[0].Payable);
        Assert.Equal(0, lines[0].Amount);
    }

    [Fact]
    public void Exchange_rates_convert_foreign_amount_to_company_currency()
    {
        var settings = CompanySettings.CreateDefault();
        settings.Update("測", "Asia/Taipei", "TWD", 25, 10, true);
        settings.SetExchangeRate("USD", 32, new DateOnly(2026, 9, 1));
        settings.SetExchangeRate("EUR", 36, new DateOnly(2026, 9, 1));
        settings.SetExchangeRate("USD", 31.5m, new DateOnly(2026, 9, 6));
        Assert.Equal(2, settings.ExchangeRates.Count);
        Assert.Equal(10000, settings.ToCompanyCurrency(10000, "TWD"));
        Assert.Equal(315000, settings.ToCompanyCurrency(10000, "USD"));
        Assert.Equal(36000, settings.ToCompanyCurrency(1000, "EUR"));
        var sameCurrency = Assert.Throws<DomainException>(() => settings.SetExchangeRate("TWD", 1, new DateOnly(2026, 9, 6)));
        Assert.Equal(ErrorCodes.InvalidState, sameCurrency.Code);
        var missing = Assert.Throws<DomainException>(() => settings.ToCompanyCurrency(1, "JPY"));
        Assert.Equal(ErrorCodes.NotFound, missing.Code);
    }

    [Fact]
    public void Margin_zero_revenue_is_dash()
    {
        var margin = new MarginResult(0, 0, 10, 10, 0, 0);
        Assert.Null(margin.ActualRate);
        Assert.Equal("—", MarginResult.Display(margin.ActualRate));
        var settings = CompanySettings.CreateDefault();
        settings.Update("測", "Asia/Taipei", "TWD", 25, 10, true);
        Assert.Equal("—", settings.Classify(null));
        Assert.Equal("紅", settings.Classify(5));
        Assert.Equal("黃", settings.Classify(20));
        Assert.Equal("綠", settings.Classify(40));
    }

    [Fact]
    public void Overload_requires_reason_and_delivery_force()
    {
        var person = Person.Create("超載", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.UpdateProfile("超載", null, null, null, 8, []);
        var client = Client.Create("客", ClientKind.External, null);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        var availability = new PersonAvailability(person.Id, 8, 8, 0, true);
        var denied = Assignment.Create(person, project, contract, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 8, AssignmentRole.Engineer, AssignmentSource.Manual, new ContractStaffingPolicy(), availability, null, true, false, DateTimeOffset.UtcNow);
        Assert.False(denied.Ok);
        Assert.Equal(ErrorCodes.OverloadRequiresReason, denied.Code);
        var forced = Assignment.Create(person, project, contract, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 8, AssignmentRole.Engineer, AssignmentSource.Manual, new ContractStaffingPolicy(), availability, "客戶現場支援", true, false, DateTimeOffset.UtcNow);
        Assert.True(forced.Ok);
    }

    [Fact]
    public void Vendor_not_on_contract_is_rejected_in_chinese()
    {
        var vendorId = Guid.NewGuid();
        var person = Person.Create("外包", EmploymentKind.VendorStaff, vendorId, DateTimeOffset.UtcNow);
        var client = Client.Create("客", ClientKind.External, null);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, [Guid.NewGuid()]);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        var result = new ContractStaffingPolicy().CanAssign(person, project, contract);
        Assert.False(result.Ok);
        Assert.Equal(Messages.VendorNotAuthorized, result.Message);
    }

    [Fact]
    public void Inactive_and_closed_cannot_assign()
    {
        var person = Person.Create("停", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.SetStatus(PersonStatus.Inactive);
        Assert.Equal(ErrorCodes.PersonInactive, person.EnsureCanAssign().Code);
        var client = Client.Create("客", ClientKind.External, null);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        project.SetStatus(ProjectStatus.Closed);
        Assert.Equal(ErrorCodes.ProjectClosed, project.EnsureCanAssign().Code);
    }

    [Fact]
    public void Approved_timesheet_cannot_be_overwritten()
    {
        var sheet = Timesheet.Upload("slot-1", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), 8, [], [], false, null, DateTimeOffset.UtcNow);
        sheet.Confirm(DateTimeOffset.UtcNow);
        var policy = new TimesheetIdempotency();
        var blocked = policy.Decide(sheet, false);
        Assert.Equal(ErrorCodes.TimesheetApprovedImmutable, blocked.Code);
        Assert.True(policy.Decide(sheet, true).Ok);
        Assert.Throws<DomainException>(() => sheet.ReplacePending(Guid.NewGuid(), new DateOnly(2026, 9, 1), 1, [], [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Soft_delete_blocked_when_history_exists()
    {
        var person = Person.Create("人", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        Assert.Equal(ErrorCodes.SoftDeleteBlocked, person.EnsureCanHardDelete(true).Code);
        Assert.True(person.EnsureCanHardDelete(false).Ok);
    }

    [Fact]
    public void Github_bind_unique_among_active()
    {
        var a = Person.Create("A", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        var b = Person.Create("B", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        Assert.True(a.BindGitHub("alice", _ => null).Ok);
        var clash = b.BindGitHub("alice", login => a.Id);
        Assert.Equal(ErrorCodes.GithubBound, clash.Code);
        Assert.Equal(Messages.GithubBound, clash.Message);
    }

    [Fact]
    public void Health_turns_red_when_target_yesterday()
    {
        var client = Client.Create("客", ClientKind.External, null);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 5), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        var settings = CompanySettings.CreateDefault();
        settings.Update("公司", "Asia/Taipei", "TWD", 25, 10, true);
        var health = new ProjectHealthPolicy(settings).Evaluate(project, new DateOnly(2026, 9, 6), 0, false, 30);
        Assert.Equal(HealthTone.Red, health.Schedule);
        Assert.Equal("逾期", health.ScheduleLabel);
    }

    [Fact]
    public void Home_route_by_role()
    {
        var resolver = new HomeRouteResolver();
        Assert.Equal("/war-room", resolver.PathFor(PlatformRole.Exec));
        Assert.Equal("/war-room", resolver.PathFor(PlatformRole.Delivery));
        Assert.Equal("/projects", resolver.PathFor(PlatformRole.Pm));
        Assert.Equal("/people", resolver.PathFor(PlatformRole.Hr));
        Assert.Equal("/me", resolver.PathFor(PlatformRole.Engineer));
        Assert.Equal("/me", resolver.PathFor(PlatformRole.VendorAdmin));
    }

    [Fact]
    public void Upload_contract_rejects_source_path()
    {
        var req = new AiProject.Company.Contracts.TimesheetUploadRequest
        {
            Extra = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["sourcePath"] = System.Text.Json.JsonSerializer.SerializeToElement("C:\\code"),
            },
        };
        Assert.True(req.HasForbiddenFields());
    }

    [Fact]
    public void Payroll_csv_header_is_stable()
    {
        Assert.Equal("personId,displayName,kind,amount,costAmount,projectId,hours,payable,note", PayrollCsv.Header);
        Assert.Contains("plannedRevenue", BudgetCsv.Header);
    }

    [Fact]
    public void Locked_period_cannot_change()
    {
        var period = PayrollPeriod.Open(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        period.Lock(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => period.MarkPmConfirming());
    }

    [Fact]
    public void Availability_single_口径()
    {
        var person = Person.Create("人", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.UpdateProfile("人", null, null, null, 40, []);
        var assignment = Assignment.Create(person, Project.Create(Guid.NewGuid(), "P", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), RevenueMethod.Milestone, DateTimeOffset.UtcNow), Contract.Create(Guid.NewGuid(), "C", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []), new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), 20, AssignmentRole.Engineer, AssignmentSource.Manual, new ContractStaffingPolicy(), new PersonAvailability(person.Id, 40, 0, 40, false), null, false, false, DateTimeOffset.UtcNow).Value!;
        var week = new AvailabilityCalculator().ForWeek(person, new DateOnly(2026, 9, 7), [assignment]);
        Assert.Equal(20, week.AssignedHours);
        Assert.Equal(20, week.RemainingHours);
        Assert.False(week.Overload);
    }

    [Fact]
    public void Company_managers_cannot_be_github_invites()
    {
        Assert.Throws<DomainException>(() => Invitation.Create("boss", PlatformRole.Owner, null, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => Invitation.Create("hr1", PlatformRole.Hr, null, DateTimeOffset.UtcNow));
        var account = StaffAccount.Create("hr1", Guid.NewGuid(), PlatformRole.Hr, "hash", DateTimeOffset.UtcNow);
        Assert.True(account.Ok);
        var engineerAccount = StaffAccount.Create("dev1", Guid.NewGuid(), PlatformRole.Engineer, "hash", DateTimeOffset.UtcNow);
        Assert.False(engineerAccount.Ok);
        Assert.Equal(Messages.GitHubInviteInstead, engineerAccount.Message);
        Assert.True(PlatformRole.Engineer.UsesGitHubSignIn());
        Assert.True(PlatformRole.Lead.UsesGitHubSignIn());
        Assert.True(PlatformRole.Owner.UsesCompanyAccount());
        Assert.True(PlatformRole.Finance.UsesCompanyAccount());
    }

    [Fact]
    public void Lead_client_cannot_establish_or_dispatch()
    {
        var lead = Client.Create("潛在", ClientKind.External, "窗口", ClientLifecycle.Lead);
        Assert.Equal(ErrorCodes.ClientCannotEstablish, lead.CanEstablish().Code);
        Assert.Equal(Messages.ClientCannotEstablish, lead.CanEstablish().Message);
        Assert.Equal(ErrorCodes.ClientNotActive, lead.EnsureCanDispatch().Code);
        Assert.False(lead.CountsAsActiveDelivery);
        lead.UpdateProfile(lead.Name, lead.Kind, lead.Contact, null, null, ClientSource.SelfDeveloped, new DateOnly(2026, 8, 1), null);
        Assert.Equal(HealthTone.Red, lead.FollowUpTone(new DateOnly(2026, 9, 6)));
    }

    [Fact]
    public void Proposal_can_establish_then_active()
    {
        var client = Client.Create("議約", ClientKind.External, "窗口", ClientLifecycle.Proposal);
        Assert.True(client.CanEstablish().Ok);
        Assert.True(client.ChangeLifecycle(ClientLifecycle.Active, DateTimeOffset.UtcNow, "周專案", null).Ok);
        Assert.Equal(ClientLifecycle.Active, client.Lifecycle);
        Assert.Contains(client.Activities, a => a.Kind == ClientActivityKind.LifecycleChange);
        client.AddActivity(DateTimeOffset.UtcNow, "周專案", ClientActivityKind.ProjectEstablished, "成立專案 示範", null);
        Assert.True(client.EnsureCanDispatch().Ok);
    }

    [Fact]
    public void New_project_starts_in_discovery_phase()
    {
        var project = Project.Create(Guid.NewGuid(), "P", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        Assert.Equal("需求", project.CurrentPhase(DateTimeOffset.UtcNow)?.Name);
        project.AddJournal(DateTimeOffset.UtcNow, "pm", ProjectJournalKind.Established, "成立專案 P");
        Assert.Single(project.Journals);
    }

    [Fact]
    public void Health_yellow_when_requirements_catalog_missing()
    {
        var project = Project.Create(Guid.NewGuid(), "P", new DateOnly(2026, 9, 1), new DateOnly(2027, 3, 1), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        var settings = CompanySettings.CreateDefault();
        settings.Update("公司", "Asia/Taipei", "TWD", 25, 10, true);
        var missing = new ProjectHealthPolicy(settings).Evaluate(project, new DateOnly(2026, 9, 6), 0, false, 40, false);
        Assert.Equal(HealthTone.Yellow, missing.Schedule);
        Assert.Equal("需求分析未就緒", missing.ScheduleLabel);
        var ready = new ProjectHealthPolicy(settings).Evaluate(project, new DateOnly(2026, 9, 6), 0, false, 40, true);
        Assert.Equal(HealthTone.Green, ready.Schedule);
    }
}
