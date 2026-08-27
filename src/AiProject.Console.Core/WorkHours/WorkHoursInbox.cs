using System.Globalization;
using AiProject.Console.Core.Intake;

namespace AiProject.Console.Core.WorkHours;

public sealed record HoursInboxRow(
    string PersonKey,
    string PersonLabel,
    string ProjectName,
    string GithubSlug,
    string Kind,
    int Number,
    string Title,
    double Hours,
    string Date);

public sealed record HoursInboxMatch(
    HoursInboxRow Row,
    string? IntakeId,
    string IntakeTitle);

public static class WorkHoursInbox
{
    public static IReadOnlyList<HoursInboxRow> ParseItemsCsv(string csv)
    {
        var rows = new List<HoursInboxRow>();
        foreach (var cols in ReadCsv(csv).Skip(1))
        {
            if (cols.Count < 10)
                continue;
            _ = int.TryParse(cols[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
            rows.Add(new HoursInboxRow(
                cols[0], cols[1], cols[2], cols[3], cols[4], number, cols[6], 0, cols.Count > 9 ? cols[9] : ""));
        }
        return rows;
    }

    public static IReadOnlyList<HoursInboxRow> ParseHoursCsv(string csv)
    {
        var rows = new List<HoursInboxRow>();
        foreach (var cols in ReadCsv(csv).Skip(1))
        {
            if (cols.Count < 8)
                continue;
            _ = int.TryParse(cols[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours);
            _ = int.TryParse(cols[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes);
            rows.Add(new HoursInboxRow(
                cols[0], cols[1], cols[2], cols[4], "hours", 0, "",
                hours + minutes / 60.0, cols[5]));
        }
        return rows;
    }

    public static IReadOnlyList<HoursInboxMatch> Match(
        IEnumerable<HoursInboxRow> items,
        IEnumerable<IntakeRecord> intakes)
    {
        var byIssue = intakes
            .SelectMany(i => i.Items.Where(w => w.HasIssue).Select(w => (Issue: w.IssueNumber!.Value, Intake: i)))
            .GroupBy(x => x.Issue)
            .ToDictionary(g => g.Key, g => g.First().Intake);
        return items.Select(row =>
        {
            byIssue.TryGetValue(row.Number, out var intake);
            return new HoursInboxMatch(row, intake?.Id, intake?.Title ?? "");
        }).ToList();
    }

    public static IReadOnlyList<IReadOnlyList<string>> ReadCsv(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(text))
            return rows;
        var src = text.TrimStart('\uFEFF');
        var i = 0;
        while (i < src.Length)
        {
            var cols = new List<string>();
            while (true)
            {
                if (i < src.Length && src[i] == '"')
                {
                    i++;
                    var sb = new System.Text.StringBuilder();
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            i++;
                            if (i < src.Length && src[i] == '"')
                            {
                                sb.Append('"');
                                i++;
                                continue;
                            }
                            break;
                        }
                        sb.Append(src[i++]);
                    }
                    cols.Add(sb.ToString());
                }
                else
                {
                    var start = i;
                    while (i < src.Length && src[i] != ',' && src[i] != '\n' && src[i] != '\r')
                        i++;
                    cols.Add(src[start..i]);
                }
                if (i >= src.Length || src[i] == '\n' || src[i] == '\r')
                    break;
                if (src[i] == ',')
                    i++;
            }
            rows.Add(cols);
            if (i < src.Length && src[i] == '\r')
                i++;
            if (i < src.Length && src[i] == '\n')
                i++;
        }
        return rows;
    }
}
