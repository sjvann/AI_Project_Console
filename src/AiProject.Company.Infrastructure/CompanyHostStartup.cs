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
