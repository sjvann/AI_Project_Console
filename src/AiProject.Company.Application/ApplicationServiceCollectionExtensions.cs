using AiProject.Company.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AiProject.Company.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationGate, AuthorizationGate>();
        services.AddSingleton<IHomeRouteResolver, HomeRouteResolver>();
        services.AddSingleton<IAvailabilityCalculator, AvailabilityCalculator>();
        services.AddSingleton<IContractStaffingPolicy, ContractStaffingPolicy>();
        services.AddSingleton<ITimesheetIdempotency, TimesheetIdempotency>();
        services.AddSingleton<IAssignmentSuggester, AssignmentSuggester>();
        services.AddSingleton<MonthlySalaryCalculator>();
        services.AddSingleton<HourlyCalculator>();
        services.AddSingleton<ProjectBonusCalculator>();
        services.AddSingleton<IPayrollCalculator>(sp => sp.GetRequiredService<MonthlySalaryCalculator>());
        services.AddSingleton<IPayrollCalculator>(sp => sp.GetRequiredService<HourlyCalculator>());
        services.AddSingleton<IPayrollCalculator>(sp => sp.GetRequiredService<ProjectBonusCalculator>());
        services.AddSingleton<IRevenueRecognizer, MilestoneRevenueRecognizer>();
        services.AddSingleton<IRevenueRecognizer, StraightLineRevenueRecognizer>();
        services.AddSingleton<IRevenueRecognizer, TimeAndMaterialsRevenueRecognizer>();
        services.AddScoped<IProjectHealthPolicy, ProjectHealthPolicyAdapter>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<SettingsCommands>();
        services.AddScoped<AccountCommands>();
        services.AddScoped<PeopleCommands>();
        services.AddScoped<ProjectCommands>();
        services.AddScoped<DispatchCommands>();
        services.AddScoped<TimesheetCommands>();
        services.AddScoped<PayrollCommands>();
        services.AddScoped<BudgetCommands>();
        services.AddScoped<WarRoomQueries>();
        services.AddScoped<MeQueries>();
        services.AddScoped<DirectoryQueries>();
        services.AddScoped<ContributionQueries>();
        services.AddScoped<ApiKeyCommands>();
        return services;
    }
}
