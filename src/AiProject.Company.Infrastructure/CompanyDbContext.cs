using AiProject.Company.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiProject.Company.Infrastructure;

public sealed class CompanyDbContext : DbContext
{
    public CompanyDbContext(DbContextOptions<CompanyDbContext> options) : base(options) { }

    public DbSet<CompanySettings> Settings => Set<CompanySettings>();
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
    public DbSet<AuditRow> Audits => Set<AuditRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CompanySettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.OwnsMany(x => x.ExchangeRates, r =>
            {
                r.ToTable("exchange_rates");
                r.WithOwner().HasForeignKey("CompanySettingsId");
                r.Property<int>("Id").ValueGeneratedOnAdd();
                r.HasKey("Id");
            });
        });
        model.Entity<Person>(e =>
        {
            e.HasKey(x => x.Id);
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
        model.Entity<Vendor>().HasKey(x => x.Id);
        model.Entity<Invitation>().HasKey(x => x.Id);
        model.Entity<UnmatchedUpload>().HasKey(x => x.Id);
        model.Entity<Client>(e =>
        {
            e.HasKey(x => x.Id);
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
            e.PrimitiveCollection(x => x.AuthorizedVendorIds);
        });
        model.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
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
            e.PrimitiveCollection(x => x.IssueNumbers);
        });
        model.Entity<Timesheet>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.LocalSlotId).IsUnique();
            e.PrimitiveCollection(x => x.IssueNumbers);
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
        });
        model.Entity<AuditRow>().HasKey(x => x.Id);
    }
}

public sealed class AuditRow
{
    public Guid Id { get; set; }
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
