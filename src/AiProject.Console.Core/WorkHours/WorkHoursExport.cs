using System.Globalization;
using System.Text;

namespace AiProject.Console.Core.WorkHours;

public static class WorkHoursExport
{
    public const string HoursHeader = "person_key,person_label,project_name,project_root,github_slug,date,hours,minutes,session_count";
    public const string ItemsHeader = "person_key,person_label,project_name,github_slug,kind,number,title,url,state,at";

    static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string FilePrefix(DateOnly start, DateOnly end, string personKey) =>
        $"timesheet-{start:yyyyMMdd}-{end:yyyyMMdd}-{WorkHoursProject.FileSlug(personKey)}";

    public static string Markdown(WorkTimesheet sheet)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 工時報告單");
        sb.AppendLine();
        sb.AppendLine($"- 人員：{sheet.PersonLabel}");
        sb.AppendLine($"- 期間：{sheet.RangeStart:yyyy-MM-dd}～{sheet.RangeEnd:yyyy-MM-dd}（{sheet.Title}）");
        sb.AppendLine($"- 可報帳總時數：{WorkHoursFormat.Duration(sheet.BillableTotal)}");
        if (sheet.UnallocatedTotal > TimeSpan.Zero)
            sb.AppendLine($"- 未掛專案（不列入報帳）：{WorkHoursFormat.Duration(sheet.UnallocatedTotal)}");
        sb.AppendLine();
        if (sheet.Projects.Count == 0)
        {
            sb.AppendLine("這個期間沒有已掛專案的工時。");
            return sb.ToString();
        }

        foreach (var project in sheet.Projects)
        {
            var slug = string.IsNullOrWhiteSpace(project.GithubSlug) ? "" : $"（{project.GithubSlug}）";
            sb.AppendLine($"## {project.Name}{slug}");
            sb.AppendLine();
            sb.AppendLine($"- 時數：{WorkHoursFormat.Duration(project.Duration)}");
            if (project.Days.Count > 0)
            {
                var days = string.Join("、", project.Days.Select(d =>
                    $"{WorkHoursFormat.DayTitle(d.Date)} {WorkHoursFormat.Duration(d.Duration)}"));
                sb.AppendLine($"- 每日：{days}");
            }
            sb.AppendLine();

            if (project.Issues.Count > 0)
            {
                sb.AppendLine("### 已關閉 Issue");
                sb.AppendLine();
                foreach (var item in project.Issues)
                    sb.AppendLine($"- {ItemLine(item)}");
                sb.AppendLine();
            }

            if (project.PullRequests.Count > 0)
            {
                sb.AppendLine("### 開啟／合併的 PR");
                sb.AppendLine();
                foreach (var item in project.PullRequests)
                    sb.AppendLine($"- {ItemLine(item)}");
                sb.AppendLine();
            }

            if (project.Issues.Count == 0 && project.PullRequests.Count == 0)
            {
                sb.AppendLine(string.IsNullOrWhiteSpace(project.ContributionNote)
                    ? "這個期間沒有可列出的 Issue／PR。"
                    : project.ContributionNote);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string HoursCsv(WorkTimesheet sheet)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HoursHeader);
        foreach (var project in sheet.Projects)
        {
            foreach (var day in project.Days.Where(d => d.HasWork))
            {
                var minutes = (int)Math.Floor(day.Duration.TotalMinutes);
                sb.AppendLine(string.Join(",",
                    Csv(sheet.PersonKey),
                    Csv(sheet.PersonLabel),
                    Csv(project.Name),
                    Csv(project.ProjectRoot),
                    Csv(project.GithubSlug),
                    Csv(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv((minutes / 60).ToString(CultureInfo.InvariantCulture)),
                    Csv((minutes % 60).ToString(CultureInfo.InvariantCulture)),
                    Csv(day.Periods.Count.ToString(CultureInfo.InvariantCulture))));
            }
        }
        return sb.ToString();
    }

    public static string ItemsCsv(WorkTimesheet sheet)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ItemsHeader);
        foreach (var item in sheet.Items)
        {
            sb.AppendLine(string.Join(",",
                Csv(sheet.PersonKey),
                Csv(sheet.PersonLabel),
                Csv(item.ProjectName),
                Csv(item.GithubSlug),
                Csv(item.Kind),
                Csv(item.Number.ToString(CultureInfo.InvariantCulture)),
                Csv(item.Title),
                Csv(item.Url),
                Csv(item.State),
                Csv(item.At is { } at ? at.ToString("o") : "")));
        }
        return sb.ToString();
    }

    public static IReadOnlyList<string> Write(string folder, WorkTimesheet sheet)
    {
        Directory.CreateDirectory(folder);
        var prefix = FilePrefix(sheet.RangeStart, sheet.RangeEnd, sheet.PersonKey);
        var md = Path.Combine(folder, prefix + ".md");
        var hours = Path.Combine(folder, prefix + "-hours.csv");
        var items = Path.Combine(folder, prefix + "-items.csv");
        File.WriteAllText(md, Markdown(sheet), Utf8Bom);
        File.WriteAllText(hours, HoursCsv(sheet), Utf8Bom);
        File.WriteAllText(items, ItemsCsv(sheet), Utf8Bom);
        return [md, hours, items];
    }

    static string ItemLine(TimesheetItem item)
    {
        var when = string.IsNullOrEmpty(item.AtText) ? "" : $"（{item.AtText}）";
        var title = string.IsNullOrWhiteSpace(item.Title) ? "" : " " + item.Title;
        if (!string.IsNullOrWhiteSpace(item.Url))
            return $"{item.NumberText}{title}{when} — {item.Url}";
        return $"{item.NumberText}{title}{when}";
    }

    static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}
