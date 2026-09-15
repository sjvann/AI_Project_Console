using AiProject.Company.Application;
using AiProject.Company.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiProject.Company.Infrastructure;

public static class CompanyHostStartup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyDbContext>();
        await db.Database.EnsureCreatedAsync();
        await EnsureStaffAccountsTableAsync(db);
        await EnsureClientCrmSchemaAsync(db);
        await EnsureTenantSchemaAsync(db);
        await EnsureDefaultTenantAsync(db);
        await EnsureReportingApiKeysAndProjectCodeAsync(db);
        await EnsureAssignmentSyncColumnsAsync(db);
        await EnsureThemeColumnAsync(db);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.GetAsync();
        var accounts = scope.ServiceProvider.GetRequiredService<IStaffAccountRepository>();
        if ((await accounts.ListAsync()).Count == 0)
        {
            var seed = scope.ServiceProvider.GetRequiredService<LocalOwnerCredentials>();
            if (seed.Enabled)
            {
                var people = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var now = DateTimeOffset.UtcNow;
                var person = Person.Create(seed.DisplayName, EmploymentKind.FullTime, null, now);
                await people.AddAsync(person);
                var created = StaffAccount.Create(seed.UserName, person.Id, PlatformRole.Owner, hasher.Hash(ReadSeedPassword(configuration)), now);
                if (!created.Ok)
                    throw new DomainException(created.Code, created.Message);
                await accounts.AddAsync(created.Value!);
                await uow.SaveChangesAsync();
            }
        }
        await CompanyDemoSeed.ApplyAsync(scope.ServiceProvider, configuration);
    }

    static string ReadSeedPassword(IConfiguration configuration)
    {
        var password = configuration["Company:Auth:LocalOwner:Password"] ?? "";
        if (password.Length < 8)
            throw new DomainException(ErrorCodes.InvalidState, Messages.WeakPassword);
        return password;
    }

    static async Task EnsureStaffAccountsTableAsync(CompanyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS staff_accounts (
                    Id TEXT NOT NULL CONSTRAINT PK_staff_accounts PRIMARY KEY,
                    TenantId TEXT NOT NULL,
                    PersonId TEXT NOT NULL,
                    UserName TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    Role INTEGER NOT NULL,
                    Enabled INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    PasswordChangedAt TEXT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_staff_accounts_UserName ON staff_accounts (UserName);");
            return;
        }
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS staff_accounts (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "PersonId" uuid NOT NULL,
                "UserName" text NOT NULL,
                "PasswordHash" text NOT NULL,
                "Role" integer NOT NULL,
                "Enabled" boolean NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "PasswordChangedAt" timestamptz NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_staff_accounts_UserName" ON staff_accounts ("UserName");""");
    }

    static async Task EnsureClientCrmSchemaAsync(CompanyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await TryAlterAsync(db, "ALTER TABLE Clients ADD COLUMN Lifecycle INTEGER NOT NULL DEFAULT 3;");
            await TryAlterAsync(db, "ALTER TABLE Clients ADD COLUMN Source INTEGER NOT NULL DEFAULT 0;");
            await TryAlterAsync(db, "ALTER TABLE Clients ADD COLUMN Phone TEXT NULL;");
            await TryAlterAsync(db, "ALTER TABLE Clients ADD COLUMN Email TEXT NULL;");
            await TryAlterAsync(db, "ALTER TABLE Clients ADD COLUMN NextFollowUp TEXT NULL;");
            await TryAlterAsync(db, "ALTER TABLE Clients ADD COLUMN OwnerPersonId TEXT NULL;");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS client_activities (
                    Id TEXT NOT NULL CONSTRAINT PK_client_activities PRIMARY KEY,
                    At TEXT NOT NULL,
                    Actor TEXT NOT NULL,
                    Kind INTEGER NOT NULL,
                    Summary TEXT NOT NULL,
                    NextFollowUp TEXT NULL,
                    ClientId TEXT NOT NULL,
                    FOREIGN KEY (ClientId) REFERENCES Clients (Id) ON DELETE CASCADE
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS project_journals (
                    Id TEXT NOT NULL CONSTRAINT PK_project_journals PRIMARY KEY,
                    At TEXT NOT NULL,
                    Actor TEXT NOT NULL,
                    Kind INTEGER NOT NULL,
                    Summary TEXT NOT NULL,
                    ProjectId TEXT NOT NULL,
                    FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
                );
                """);
            return;
        }

        await TryAlterAsync(db, """ALTER TABLE "Clients" ADD COLUMN IF NOT EXISTS "Lifecycle" integer NOT NULL DEFAULT 3;""");
        await TryAlterAsync(db, """ALTER TABLE "Clients" ADD COLUMN IF NOT EXISTS "Source" integer NOT NULL DEFAULT 0;""");
        await TryAlterAsync(db, """ALTER TABLE "Clients" ADD COLUMN IF NOT EXISTS "Phone" text NULL;""");
        await TryAlterAsync(db, """ALTER TABLE "Clients" ADD COLUMN IF NOT EXISTS "Email" text NULL;""");
        await TryAlterAsync(db, """ALTER TABLE "Clients" ADD COLUMN IF NOT EXISTS "NextFollowUp" date NULL;""");
        await TryAlterAsync(db, """ALTER TABLE "Clients" ADD COLUMN IF NOT EXISTS "OwnerPersonId" uuid NULL;""");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS client_activities (
                "Id" uuid NOT NULL PRIMARY KEY,
                "At" timestamptz NOT NULL,
                "Actor" text NOT NULL,
                "Kind" integer NOT NULL,
                "Summary" text NOT NULL,
                "NextFollowUp" date NULL,
                "ClientId" uuid NOT NULL REFERENCES "Clients" ("Id") ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS project_journals (
                "Id" uuid NOT NULL PRIMARY KEY,
                "At" timestamptz NOT NULL,
                "Actor" text NOT NULL,
                "Kind" integer NOT NULL,
                "Summary" text NOT NULL,
                "ProjectId" uuid NOT NULL REFERENCES "Projects" ("Id") ON DELETE CASCADE
            );
            """);
    }

    static async Task EnsureTenantSchemaAsync(CompanyDbContext db)
    {
        var defaultId = TenantIds.Default.ToString();
        string[] tables =
        [
            "CompanySettings",
            "People",
            "Vendors",
            "Invitations",
            "UnmatchedUploads",
            "Clients",
            "Contracts",
            "Projects",
            "Assignments",
            "Timesheets",
            "PayrollPeriods",
            "staff_accounts",
            "Audits",
        ];

        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Tenants (
                    Id TEXT NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            foreach (var table in tables)
                await TryAlterAsync(db, $"ALTER TABLE {table} ADD COLUMN TenantId TEXT NOT NULL DEFAULT '{defaultId}';");
            return;
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Tenants" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "DisplayName" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL
            );
            """);
        foreach (var table in tables)
        {
            var quoted = table == "staff_accounts" ? "staff_accounts" : $"\"{table}\"";
            await TryAlterAsync(db, $"""ALTER TABLE {quoted} ADD COLUMN IF NOT EXISTS "TenantId" uuid NOT NULL DEFAULT '{defaultId}';""");
        }
    }

    static async Task EnsureDefaultTenantAsync(CompanyDbContext db)
    {
        if (await db.Tenants.AnyAsync(t => t.Id == TenantIds.Default))
            return;
        db.Tenants.Add(Tenant.CreateDefault(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    static async Task EnsureReportingApiKeysAndProjectCodeAsync(CompanyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await TryAlterAsync(db, "ALTER TABLE Projects ADD COLUMN ProjectCode TEXT NULL;");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS reporting_api_keys (
                    Id TEXT NOT NULL CONSTRAINT PK_reporting_api_keys PRIMARY KEY,
                    TenantId TEXT NOT NULL,
                    PersonId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    KeyPrefix TEXT NOT NULL,
                    KeyHash TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    RevokedAt TEXT NULL,
                    LastUsedAt TEXT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_reporting_api_keys_KeyHash ON reporting_api_keys (KeyHash);");
            return;
        }

        await TryAlterAsync(db, """ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "ProjectCode" text NULL;""");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS reporting_api_keys (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "PersonId" uuid NOT NULL,
                "Name" text NOT NULL,
                "KeyPrefix" text NOT NULL,
                "KeyHash" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "RevokedAt" timestamptz NULL,
                "LastUsedAt" timestamptz NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_reporting_api_keys_KeyHash" ON reporting_api_keys ("KeyHash");""");
    }

    static async Task EnsureAssignmentSyncColumnsAsync(CompanyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await TryAlterAsync(db, "ALTER TABLE Assignments ADD COLUMN SyncNote TEXT NULL;");
            return;
        }
        await TryAlterAsync(db, """ALTER TABLE "Assignments" ADD COLUMN IF NOT EXISTS "SyncNote" text NULL;""");
    }

    static async Task EnsureThemeColumnAsync(CompanyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await TryAlterAsync(db, "ALTER TABLE CompanySettings ADD COLUMN ThemeId TEXT NULL;");
            return;
        }
        await TryAlterAsync(db, """ALTER TABLE "CompanySettings" ADD COLUMN IF NOT EXISTS "ThemeId" character varying(32) NULL;""");
    }

    static async Task TryAlterAsync(CompanyDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch
        {
            // 欄已存在或舊庫結構不同時略過
        }
    }
}
