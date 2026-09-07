namespace AiProject.Company.Domain;

public enum ClientKind
{
    External = 0,
    Internal = 1,
}

public enum PricingKind
{
    FixedPrice = 0,
    TimeAndMaterials = 1,
    Mixed = 2,
}

public enum RevenueMethod
{
    Milestone = 0,
    StraightLine = 1,
    TimeAndMaterials = 2,
}

public enum ProjectStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Acceptance = 3,
    Closed = 4,
}

public enum RecognitionStep
{
    None = 0,
    Invoiced = 1,
    Collected = 2,
}

public enum ClientLifecycle
{
    Lead = 0,
    Discovery = 1,
    Proposal = 2,
    Active = 3,
    Dormant = 4,
}

public enum ClientSource
{
    Existing = 0,
    Referral = 1,
    SelfDeveloped = 2,
    Recruit = 3,
    Other = 4,
}

public enum ClientActivityKind
{
    Call = 0,
    Meeting = 1,
    RequirementNote = 2,
    ProposalSent = 3,
    LifecycleChange = 4,
    ProjectEstablished = 5,
}

public enum ProjectJournalKind
{
    Established = 0,
    RepoLinked = 1,
    PhaseEntered = 2,
    MilestoneAdded = 3,
    Closed = 4,
    Recognized = 5,
    Note = 6,
}

public static class ClientCopy
{
    public static string Display(this ClientLifecycle lifecycle) => lifecycle switch
    {
        ClientLifecycle.Lead => "潛在",
        ClientLifecycle.Discovery => "需求訪談",
        ClientLifecycle.Proposal => "提案議約",
        ClientLifecycle.Active => "合約客戶",
        ClientLifecycle.Dormant => "休眠",
        _ => lifecycle.ToString(),
    };

    public static string Display(this ClientSource source) => source switch
    {
        ClientSource.Existing => "既有客戶",
        ClientSource.Referral => "轉介",
        ClientSource.SelfDeveloped => "自行開發",
        ClientSource.Recruit => "招募轉介",
        ClientSource.Other => "其他",
        _ => source.ToString(),
    };

    public static string Display(this ClientActivityKind kind) => kind switch
    {
        ClientActivityKind.Call => "來電",
        ClientActivityKind.Meeting => "面談",
        ClientActivityKind.RequirementNote => "需求紀錄",
        ClientActivityKind.ProposalSent => "提案送出",
        ClientActivityKind.LifecycleChange => "狀態變更",
        ClientActivityKind.ProjectEstablished => "成立專案",
        _ => kind.ToString(),
    };

    public static string Display(this ProjectJournalKind kind) => kind switch
    {
        ProjectJournalKind.Established => "成立",
        ProjectJournalKind.RepoLinked => "掛倉",
        ProjectJournalKind.PhaseEntered => "進階段",
        ProjectJournalKind.MilestoneAdded => "加里程碑",
        ProjectJournalKind.Closed => "結案",
        ProjectJournalKind.Recognized => "認列",
        ProjectJournalKind.Note => "備註",
        _ => kind.ToString(),
    };
}

public sealed class Client
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Contact { get; private set; }
    public ClientKind Kind { get; private set; }
    public ClientLifecycle Lifecycle { get; private set; }
    public ClientSource Source { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public DateOnly? NextFollowUp { get; private set; }
    public Guid? OwnerPersonId { get; private set; }
    public bool IsDeleted { get; private set; }
    public List<Contract> Contracts { get; private set; } = [];
    public List<ClientActivity> Activities { get; private set; } = [];

    public static Client Create(string name, ClientKind kind, string? contact, ClientLifecycle lifecycle = ClientLifecycle.Active, ClientSource source = ClientSource.Existing)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("客戶名稱"));
        return new Client
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Kind = kind,
            Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim(),
            Lifecycle = lifecycle,
            Source = source,
        };
    }

    public void Update(string name, ClientKind kind, string? contact)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("客戶名稱"));
        Name = name.Trim();
        Kind = kind;
        Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
    }

    public void UpdateProfile(string name, ClientKind kind, string? contact, string? phone, string? email, ClientSource source, DateOnly? nextFollowUp, Guid? ownerPersonId)
    {
        Update(name, kind, contact);
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Source = source;
        NextFollowUp = nextFollowUp;
        OwnerPersonId = ownerPersonId;
    }

    public Outcome CanEstablish()
    {
        if (IsDeleted)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        if (Lifecycle is ClientLifecycle.Proposal or ClientLifecycle.Active)
            return Outcome.Success();
        return Outcome.Fail(ErrorCodes.ClientCannotEstablish, Messages.ClientCannotEstablish);
    }

    public Outcome EnsureCanDispatch()
    {
        if (IsDeleted || Lifecycle != ClientLifecycle.Active)
            return Outcome.Fail(ErrorCodes.ClientNotActive, Messages.ClientNotActive);
        return Outcome.Success();
    }

    public bool CountsAsActiveDelivery => !IsDeleted && Lifecycle == ClientLifecycle.Active;

    public Outcome ChangeLifecycle(ClientLifecycle to, DateTimeOffset now, string actor, DateOnly? nextFollowUp)
    {
        if (to == Lifecycle)
            return Outcome.Success();
        if (!AllowsTransition(Lifecycle, to))
            return Outcome.Fail(ErrorCodes.InvalidState, "不能跳到這個狀態。");
        var from = Lifecycle;
        Lifecycle = to;
        if (nextFollowUp is not null)
            NextFollowUp = nextFollowUp;
        AddActivity(now, actor, ClientActivityKind.LifecycleChange, $"狀態由 {from.Display()} 改為 {to.Display()}", nextFollowUp);
        return Outcome.Success();
    }

    public void MarkActive() => Lifecycle = ClientLifecycle.Active;

    public ClientActivity AddActivity(DateTimeOffset at, string actor, ClientActivityKind kind, string summary, DateOnly? nextFollowUp)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new DomainException(ErrorCodes.Required, Messages.Required("活動摘要"));
        var item = ClientActivity.Record(at, actor, kind, summary, nextFollowUp);
        Activities.Add(item);
        if (nextFollowUp is not null)
            NextFollowUp = nextFollowUp;
        return item;
    }

    public HealthTone FollowUpTone(DateOnly today)
    {
        if (NextFollowUp is null || Lifecycle is ClientLifecycle.Active or ClientLifecycle.Dormant)
            return HealthTone.Green;
        if (today > NextFollowUp.Value.AddDays(7))
            return HealthTone.Red;
        if (today > NextFollowUp.Value)
            return HealthTone.Yellow;
        return HealthTone.Green;
    }

    public static bool AllowsTransition(ClientLifecycle from, ClientLifecycle to)
    {
        if (to == ClientLifecycle.Dormant)
            return from != ClientLifecycle.Dormant;
        if (from == ClientLifecycle.Dormant)
            return to is ClientLifecycle.Lead or ClientLifecycle.Discovery;
        return (from, to) switch
        {
            (ClientLifecycle.Lead, ClientLifecycle.Discovery) => true,
            (ClientLifecycle.Lead, ClientLifecycle.Proposal) => true,
            (ClientLifecycle.Discovery, ClientLifecycle.Proposal) => true,
            (ClientLifecycle.Proposal, ClientLifecycle.Active) => true,
            _ => false,
        };
    }

    public void SoftDelete() => IsDeleted = true;
}

public sealed class ClientActivity
{
    public Guid Id { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Actor { get; private set; } = "";
    public ClientActivityKind Kind { get; private set; }
    public string Summary { get; private set; } = "";
    public DateOnly? NextFollowUp { get; private set; }

    public static ClientActivity Record(DateTimeOffset at, string actor, ClientActivityKind kind, string summary, DateOnly? nextFollowUp) =>
        new()
        {
            Id = Guid.NewGuid(),
            At = at,
            Actor = string.IsNullOrWhiteSpace(actor) ? "系統" : actor.Trim(),
            Kind = kind,
            Summary = summary.Trim(),
            NextFollowUp = nextFollowUp,
        };
}

public sealed class Contract
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string Name { get; private set; } = "";
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TWD";
    public PricingKind Pricing { get; private set; }
    public List<Guid> AuthorizedVendorIds { get; private set; } = [];
    public bool IsDeleted { get; private set; }

    public static Contract Create(Guid clientId, string name, DateOnly start, DateOnly end, decimal amount, string currency, PricingKind pricing, IEnumerable<Guid> vendorIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("合約名稱"));
        if (end < start)
            throw new DomainException(ErrorCodes.InvalidState, "合約結束日不能早於開始日。");
        return new Contract
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = name.Trim(),
            Start = start,
            End = end,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TWD" : currency.Trim().ToUpperInvariant(),
            Pricing = pricing,
            AuthorizedVendorIds = vendorIds.Distinct().ToList(),
        };
    }

    public void Update(string name, DateOnly start, DateOnly end, decimal amount, string currency, PricingKind pricing, IEnumerable<Guid> vendorIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("合約名稱"));
        if (end < start)
            throw new DomainException(ErrorCodes.InvalidState, "合約結束日不能早於開始日。");
        Name = name.Trim();
        Start = start;
        End = end;
        Amount = amount;
        Currency = string.IsNullOrWhiteSpace(currency) ? "TWD" : currency.Trim().ToUpperInvariant();
        Pricing = pricing;
        AuthorizedVendorIds = vendorIds.Distinct().ToList();
    }

    public bool AllowsVendor(Guid? vendorId) =>
        vendorId is null || AuthorizedVendorIds.Contains(vendorId.Value);

    public void SoftDelete() => IsDeleted = true;
}

public sealed class Project
{
    public Guid Id { get; private set; }
    public Guid ContractId { get; private set; }
    public string Name { get; private set; } = "";
    public ProjectStatus Status { get; private set; }
    public DateOnly Start { get; private set; }
    public DateOnly TargetEnd { get; private set; }
    public RevenueMethod RevenueMethod { get; private set; }
    public string? WorkspacePath { get; private set; }
    public bool ExcludeFromMarginKpi { get; private set; }
    public string? ClientRateCipher { get; private set; }
    public bool IsDeleted { get; private set; }
    public List<Phase> Phases { get; private set; } = [];
    public List<Milestone> Milestones { get; private set; } = [];
    public List<RepoLink> Repos { get; private set; } = [];
    public List<OtherExpense> OtherExpenses { get; private set; } = [];
    public List<ProjectJournalEntry> Journals { get; private set; } = [];

    public static readonly string[] DefaultPhaseNames = ["需求", "設計", "實作", "驗證", "驗收", "維運"];

    public static Project Create(Guid contractId, string name, DateOnly start, DateOnly targetEnd, RevenueMethod method, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("專案名稱"));
        if (targetEnd < start)
            throw new DomainException(ErrorCodes.InvalidState, "目標結束日不能早於開始日。");
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            Name = name.Trim(),
            Status = ProjectStatus.Active,
            Start = start,
            TargetEnd = targetEnd,
            RevenueMethod = method,
        };
        var cursor = start;
        var first = true;
        foreach (var phaseName in DefaultPhaseNames)
        {
            var end = cursor.AddDays(13);
            project.Phases.Add(Phase.Plan(phaseName, cursor, end, first ? now : null));
            cursor = end.AddDays(1);
            first = false;
        }
        return project;
    }

    public void Update(string name, DateOnly start, DateOnly targetEnd, RevenueMethod method, string? workspacePath, bool excludeFromMargin, string? clientRateCipher)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("專案名稱"));
        Name = name.Trim();
        Start = start;
        TargetEnd = targetEnd;
        RevenueMethod = method;
        WorkspacePath = string.IsNullOrWhiteSpace(workspacePath) ? null : workspacePath.Trim();
        ExcludeFromMarginKpi = excludeFromMargin;
        ClientRateCipher = clientRateCipher;
    }

    public void SetStatus(ProjectStatus status) => Status = status;

    public void AddJournal(DateTimeOffset at, string actor, ProjectJournalKind kind, string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new DomainException(ErrorCodes.Required, Messages.Required("紀錄摘要"));
        Journals.Add(ProjectJournalEntry.Record(at, actor, kind, summary));
    }

    public Outcome EnsureCanAssign()
    {
        if (IsDeleted || Status == ProjectStatus.Closed)
            return Outcome.Fail(ErrorCodes.ProjectClosed, Messages.ProjectClosed);
        return Outcome.Success();
    }

    public Outcome EnsureCanHardDelete(bool hasTimesheetsOrAssignments)
    {
        if (hasTimesheetsOrAssignments)
            return Outcome.Fail(ErrorCodes.SoftDeleteBlocked, Messages.SoftDeleteBlocked);
        return Outcome.Success();
    }

    public void SoftDelete() => IsDeleted = true;

    public void AddRepo(string ownerRepo)
    {
        var slug = ownerRepo.Trim();
        if (string.IsNullOrWhiteSpace(slug) || !slug.Contains('/'))
            throw new DomainException(ErrorCodes.InvalidState, "倉名稱要是 owner/repo。");
        if (Repos.Any(r => string.Equals(r.OwnerRepo, slug, StringComparison.OrdinalIgnoreCase)))
            return;
        Repos.Add(new RepoLink(slug));
    }

    public void RemoveRepo(string ownerRepo) =>
        Repos.RemoveAll(r => string.Equals(r.OwnerRepo, ownerRepo, StringComparison.OrdinalIgnoreCase));

    public Milestone AddMilestone(string name, DateOnly due, decimal billingAmount, Guid? dependsOnId)
    {
        if (dependsOnId is Guid dep && Milestones.All(m => m.Id != dep))
            throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("前置里程碑"));
        var item = Milestone.Create(name, due, billingAmount, dependsOnId);
        Milestones.Add(item);
        return item;
    }

    public Phase? CurrentPhase(DateTimeOffset now) =>
        Phases.LastOrDefault(p => p.EnteredAt is not null && p.LeftAt is null)
        ?? Phases.FirstOrDefault();

    public void EnterPhase(Guid phaseId, DateTimeOffset now)
    {
        foreach (var phase in Phases.Where(p => p.EnteredAt is not null && p.LeftAt is null))
            phase.Leave(now);
        var target = Phases.FirstOrDefault(p => p.Id == phaseId)
            ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("階段"));
        target.Enter(now);
    }

    public void AddOtherExpense(string label, decimal planned, decimal actual, bool isActual)
    {
        OtherExpenses.Add(new OtherExpense(label, planned, actual, isActual));
    }
}

public sealed class ProjectJournalEntry
{
    public Guid Id { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Actor { get; private set; } = "";
    public ProjectJournalKind Kind { get; private set; }
    public string Summary { get; private set; } = "";

    public static ProjectJournalEntry Record(DateTimeOffset at, string actor, ProjectJournalKind kind, string summary) =>
        new()
        {
            Id = Guid.NewGuid(),
            At = at,
            Actor = string.IsNullOrWhiteSpace(actor) ? "系統" : actor.Trim(),
            Kind = kind,
            Summary = summary.Trim(),
        };
}

public sealed class Phase
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public DateOnly PlanStart { get; private set; }
    public DateOnly PlanEnd { get; private set; }
    public DateTimeOffset? EnteredAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    public static Phase Plan(string name, DateOnly start, DateOnly end, DateTimeOffset? enteredAt)
    {
        return new Phase
        {
            Id = Guid.NewGuid(),
            Name = name,
            PlanStart = start,
            PlanEnd = end,
            EnteredAt = enteredAt,
        };
    }

    public void Reschedule(DateOnly start, DateOnly end)
    {
        PlanStart = start;
        PlanEnd = end;
    }

    public void Enter(DateTimeOffset now)
    {
        EnteredAt ??= now;
        LeftAt = null;
    }

    public void Leave(DateTimeOffset now) => LeftAt ??= now;

    public bool IsOverdue(DateOnly today) => LeftAt is null && today > PlanEnd;
}

public sealed class Milestone
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public DateOnly Due { get; private set; }
    public decimal BillingAmount { get; private set; }
    public Guid? DependsOnId { get; private set; }
    public bool Completed { get; private set; }
    public bool BonusPayable { get; private set; }
    public RecognitionStep Recognition { get; private set; }

    public static Milestone Create(string name, DateOnly due, decimal billingAmount, Guid? dependsOnId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("里程碑名稱"));
        return new Milestone
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Due = due,
            BillingAmount = billingAmount,
            DependsOnId = dependsOnId,
        };
    }

    public void Update(string name, DateOnly due, decimal billingAmount, Guid? dependsOnId)
    {
        Name = name.Trim();
        Due = due;
        BillingAmount = billingAmount;
        DependsOnId = dependsOnId;
    }

    public void MarkCompleted(bool completed) => Completed = completed;

    public void MarkBonusPayable(bool payable) => BonusPayable = payable;

    public void SetRecognition(RecognitionStep step) => Recognition = step;

    public void RecognizeAll() => Recognition = RecognitionStep.Collected;

    public bool IsOverdue(DateOnly today) => !Completed && today > Due;
}

public sealed class RepoLink
{
    public string OwnerRepo { get; private set; }

    public RepoLink(string ownerRepo) => OwnerRepo = ownerRepo;
}

public sealed class OtherExpense
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Label { get; private set; }
    public decimal Planned { get; private set; }
    public decimal Actual { get; private set; }
    public bool IsActual { get; private set; }

    public OtherExpense(string label, decimal planned, decimal actual, bool isActual)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException(ErrorCodes.Required, Messages.Required("費用名稱"));
        Label = label.Trim();
        Planned = planned;
        Actual = actual;
        IsActual = isActual;
    }
}

public sealed record IntakeSummary(int Issued, int PendingAcceptance);

public sealed record ProductDocItem(string Name, bool Present, string? Href);

public sealed record ProjectDocsCatalog(
    bool WorkspaceLinked,
    string? WorkspacePath,
    string? PagesUrl,
    bool PagesPublished,
    IReadOnlyList<ProductDocItem> ProductDocs,
    IntakeSummary? Intake,
    string? EmptyHint)
{
    public static readonly string[] SkeletonNames = ["README.md", "vision.md", "prd.md", "spec.md"];

    public bool SkeletonReady => ProductDocs.Any(d => d.Present);
    public bool RequirementsReady => PagesPublished || SkeletonReady || Intake is not null;

    public static ProjectDocsCatalog Empty(string hint, bool workspaceLinked = false, string? workspacePath = null) =>
        new(workspaceLinked, workspacePath, null, false, [], null, hint);
}
