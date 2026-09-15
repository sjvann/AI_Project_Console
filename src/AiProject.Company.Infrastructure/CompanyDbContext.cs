using AiProject.Company.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiProject.Company.Infrastructure;

public sealed class CompanyDbContext : DbContext
{
    public CompanyDbContext(DbContextOptions<CompanyDbContext> options) : base(options) { }

    public DbSet<CompanySettings> Settings => Set<CompanySettings>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<UnmatchedUpload> UnmatchedUploads => Set<UnmatchedUpload>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Timesheet> Timesheets => Set<Timesheet>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<StaffAccount> StaffAccounts => Set<StaffAccount>();
    public DbSet<ReportingApiKey> ReportingApiKeys => Set<ReportingApiKey>();
    public DbSet<AuditRow> Audits => Set<AuditRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CompanySettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.OwnsMany(x => x.ExchangeRates, r =>
            {
                r.ToTable("exchange_rates");
                r.WithOwner().HasForeignKey("CompanySettingsId");
                r.Property<int>("Id").ValueGeneratedOnAdd();
                r.HasKey("Id");
            });
        });
        model.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });
        model.Entity<Person>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.PrimitiveCollection(x => x.Skills);
            e.OwnsMany(x => x.Unavailable, r =>
            {
                r.ToTable("person_unavailable");
                r.WithOwner().HasForeignKey("PersonId");
                r.Property<int>("Id").ValueGeneratedOnAdd();
                r.HasKey("Id");
            });
            e.HasIndex(x => x.GitHubLogin);
        });
        model.Entity<Vendor>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
        });
        model.Entity<Invitation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
        });
        model.Entity<UnmatchedUpload>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
        });
        model.Entity<Client>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.OwnsMany(x => x.Activities, r =>
            {
                r.ToTable("client_activities");
                r.WithOwner().HasForeignKey("ClientId");
                r.HasKey(x => x.Id);
            });
            e.Ignore(x => x.Contracts);
        });
        model.Entity<Contract>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.PrimitiveCollection(x => x.AuthorizedVendorIds);
        });
        model.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.Property(x => x.ProjectCode).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.ProjectCode });
            e.OwnsMany(x => x.Phases, r =>
            {
                r.ToTable("project_phases");
                r.WithOwner().HasForeignKey("ProjectId");
                r.HasKey(x => x.Id);
            });
            e.OwnsMany(x => x.Milestones, r =>
            {
                r.ToTable("project_milestones");
                r.WithOwner().HasForeignKey("ProjectId");
                r.HasKey(x => x.Id);
            });
            e.OwnsMany(x => x.Repos, r =>
            {
                r.ToTable("project_repos");
                r.WithOwner().HasForeignKey("ProjectId");
                r.Property<int>("Id").ValueGeneratedOnAdd();
                r.HasKey("Id");
            });
            e.OwnsMany(x => x.OtherExpenses, r =>
            {
                r.ToTable("project_expenses");
                r.WithOwner().HasForeignKey("ProjectId");
                r.HasKey(x => x.Id);
            });
            e.OwnsMany(x => x.Journals, r =>
            {
                r.ToTable("project_journals");
                r.WithOwner().HasForeignKey("ProjectId");
                r.HasKey(x => x.Id);
            });
        });
        model.Entity<Assignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.PrimitiveCollection(x => x.IssueNumbers);
        });
        model.Entity<Timesheet>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.LocalSlotId).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.PrimitiveCollection(x => x.IssueNumbers);
            e.PrimitiveCollection(x => x.ContributionTypes);
            e.OwnsMany(x => x.Chart, r =>
            {
                r.ToTable("timesheet_chart");
                r.WithOwner().HasForeignKey("TimesheetId");
                r.Property<int>("Id").ValueGeneratedOnAdd();
                r.HasKey("Id");
            });
        });
        model.Entity<PayrollPeriod>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.OwnsMany(x => x.Lines, r =>
            {
                r.ToTable("payroll_lines");
                r.WithOwner().HasForeignKey("PayrollPeriodId");
                r.HasKey(x => x.Id);
            });
        });
        model.Entity<StaffAccount>(e =>
        {
            e.ToTable("staff_accounts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.TenantId);
        });
        model.Entity<ReportingApiKey>(e =>
        {
            e.ToTable("reporting_api_keys");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.PersonId);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.KeyPrefix).HasMaxLength(32);
            e.Property(x => x.KeyHash).HasMaxLength(64);
        });
        model.Entity<AuditRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
        });
    }
}

public sealed class AuditRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; } = TenantIds.Default;
    public DateTimeOffset At { get; set; }
    public Guid? ActorPersonId { get; set; }
    public string ActorLogin { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public bool IsSensitiveRead { get; set; }
}
