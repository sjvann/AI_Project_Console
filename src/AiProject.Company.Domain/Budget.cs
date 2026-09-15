namespace AiProject.Company.Domain;

public sealed record MarginResult(decimal PlannedRevenue, decimal ActualRevenue, decimal PlannedCost, decimal ActualCost, decimal OtherPlanned, decimal OtherActual)
{
    public decimal PlannedMargin => PlannedRevenue - PlannedCost - OtherPlanned;
    public decimal ActualMargin => ActualRevenue - ActualCost - OtherActual;
    public decimal? PlannedRate => Rate(PlannedRevenue, PlannedMargin);
    public decimal? ActualRate => Rate(ActualRevenue, ActualMargin);

    public static decimal? Rate(decimal revenue, decimal margin) =>
        revenue == 0 ? null : decimal.Round(margin / revenue * 100m, 1);

    public static string Display(decimal? rate) => rate is null ? "—" : rate.Value.ToString("0.#") + "%";
}

public static class BudgetCsv
{
    public const string Header = "projectId,projectName,clientName,plannedRevenue,actualRevenue,plannedCost,actualCost,otherPlanned,otherActual,plannedMargin,actualMargin,plannedRate,actualRate,currency";

    public static string Render(IEnumerable<BudgetCsvRow> rows)
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine(Header);
        foreach (var row in rows)
        {
            output.Append(row.ProjectId).Append(',')
                .Append(Csv(row.ProjectName)).Append(',')
                .Append(Csv(row.ClientName)).Append(',')
                .Append(N(row.PlannedRevenue)).Append(',')
                .Append(N(row.ActualRevenue)).Append(',')
                .Append(N(row.PlannedCost)).Append(',')
                .Append(N(row.ActualCost)).Append(',')
                .Append(N(row.OtherPlanned)).Append(',')
                .Append(N(row.OtherActual)).Append(',')
                .Append(N(row.PlannedMargin)).Append(',')
                .Append(N(row.ActualMargin)).Append(',')
                .Append(Csv(row.PlannedRate)).Append(',')
                .Append(Csv(row.ActualRate)).Append(',')
                .Append(row.Currency)
                .AppendLine();
        }
        return output.ToString();
    }

    static string N(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

public sealed record BudgetCsvRow(
    Guid ProjectId,
    string ProjectName,
    string ClientName,
    decimal PlannedRevenue,
    decimal ActualRevenue,
    decimal PlannedCost,
    decimal ActualCost,
    decimal OtherPlanned,
    decimal OtherActual,
    decimal PlannedMargin,
    decimal ActualMargin,
    string PlannedRate,
    string ActualRate,
    string Currency);

public static class CostAllocationCsv
{
    public const string Header = "personId,displayName,projectId,projectName,hours,costAmount,weightPercent,source";

    public static string Render(IEnumerable<CostAllocationRow> rows)
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine(Header);
        foreach (var row in rows)
        {
            output.Append(row.PersonId).Append(',')
                .Append(Csv(row.DisplayName)).Append(',')
                .Append(row.ProjectId).Append(',')
                .Append(Csv(row.ProjectName)).Append(',')
                .Append(N(row.Hours)).Append(',')
                .Append(N(row.CostAmount)).Append(',')
                .Append(N(row.WeightPercent)).Append(',')
                .Append(Csv(row.Source))
                .AppendLine();
        }
        return output.ToString();
    }

    static string N(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

public sealed record CostAllocationRow(
    Guid PersonId,
    string DisplayName,
    Guid ProjectId,
    string ProjectName,
    decimal Hours,
    decimal CostAmount,
    decimal WeightPercent,
    string Source);
