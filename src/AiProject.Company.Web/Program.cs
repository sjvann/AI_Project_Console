using System.Security.Claims;
using AiProject.Company.Application;
using AiProject.Company.Contracts;
using AiProject.Company.Domain;
using AiProject.Company.Infrastructure;
using AiProject.Company.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCompanyPlatform(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
var cookie = CookieAuthenticationDefaults.AuthenticationScheme;
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = cookie;
        options.DefaultChallengeScheme = cookie;
    })
    .AddCookie(cookie, options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/denied";
        options.Cookie.Name = "aiproject.company";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

var app = builder.Build();
await CompanyHostStartup.InitializeAsync(app.Services, app.Configuration);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapGet("/health", async (CompanyDbContext db) =>
{
    var ok = await db.Database.CanConnectAsync();
    return ok ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503);
}).AllowAnonymous();
app.MapPost("/login/account", async (HttpContext http, AccountCommands accounts) =>
{
    var userName = http.Request.Form["username"].ToString();
    var password = http.Request.Form["password"].ToString();
    var account = await accounts.AuthenticateAsync(userName, password);
    if (account is null)
        return LoginFailed(http);
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, account.UserName),
        new("account", account.UserName),
        new("role", account.Role.Code()),
        new(ClaimTypes.Role, account.Role.Code()),
    };
    await http.SignInAsync(cookie, new ClaimsPrincipal(new ClaimsIdentity(claims, cookie)));
    return Results.Redirect("/");
}).DisableAntiforgery();
app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(cookie);
    return Results.Redirect("/login");
});
CompanyApi.Map(app);
app.Run();

public partial class Program
{
    internal static IResult LoginFailed(HttpContext http)
    {
        if (http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            return Results.Redirect("/login?error=credentials");
        return Results.Json(new ApiError { Code = ErrorCodes.BadCredentials, Message = Messages.BadCredentials }, statusCode: 401);
    }
}

public static class CompanyApi
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1").DisableAntiforgery().AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers[CompanyApiVersions.Header] = CompanyApiVersions.Current;
            return await next(context);
        });

        api.MapPost("/timesheets/upload", async (TimesheetUploadRequest body, HttpContext http, TimesheetCommands commands, IGitHubDirectory github, ICurrentUser user) =>
        {
            var login = user.GitHubLogin;
            if (string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(body.OnBehalfOfGitHubLogin))
                login = body.OnBehalfOfGitHubLogin.Trim();
            if (string.IsNullOrEmpty(login))
            {
                var bearer = http.Request.Headers.Authorization.ToString();
                if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    login = await github.ResolveLoginAsync(bearer["Bearer ".Length..].Trim()) ?? "";
            }
            if (user.Role == PlatformRole.VendorAdmin && !string.IsNullOrWhiteSpace(body.OnBehalfOfGitHubLogin))
                login = body.OnBehalfOfGitHubLogin.Trim();
            if (string.IsNullOrEmpty(login))
                return Results.Json(new ApiError { Code = ErrorCodes.Unauthenticated, Message = Messages.Unauthenticated }, statusCode: 401);
            var vendorSubmit = user.Role == PlatformRole.VendorAdmin;
            var result = await commands.UploadAsync(body, login, vendorSubmit ? null : user.PersonId, user.VendorId, vendorSubmit);
            if (!result.Ok)
            {
                var status = result.Code is ErrorCodes.Forbidden or ErrorCodes.RateHidden ? 403
                    : result.Code == ErrorCodes.Unauthenticated ? 401
                    : result.Code == ErrorCodes.TimesheetApprovedImmutable ? 409
                    : result.Code == ErrorCodes.UnmatchedPerson ? 202
                    : 400;
                return Results.Json(new ApiError { Code = result.Code, Message = result.Message }, statusCode: status);
            }
            return Results.Json(new TimesheetUploadResponse { TimesheetId = result.Value, Status = "pending_pm", Message = "待 PM 確認" });
        });

        api.MapGet("/me/assignments", async (ICurrentUser user, MeQueries me) =>
        {
            if (!user.IsAuthenticated)
                return Results.Json(new ApiError { Code = ErrorCodes.Unauthenticated, Message = Messages.Unauthenticated }, statusCode: 401);
            return Results.Json(await me.MyAssignmentsAsync());
        });

        api.MapGet("/me/payslip", async (ICurrentUser user, MeQueries me) =>
        {
            if (!user.IsAuthenticated)
                return Results.Json(new ApiError { Code = ErrorCodes.Unauthenticated, Message = Messages.Unauthenticated }, statusCode: 401);
            try
            {
                var slip = await me.LatestPayslipAsync();
                return slip is null ? Results.NotFound() : Results.Json(slip);
            }
            catch (DomainException ex)
            {
                var status = ex.Code is ErrorCodes.Forbidden or ErrorCodes.RateHidden ? 403 : 400;
                return Results.Json(new ApiError { Code = ex.Code, Message = ex.Message }, statusCode: status);
            }
        });
    }
}
