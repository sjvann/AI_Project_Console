using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.WorkHours;

namespace AiProject.Console.Core.Tests;

public class WorkHoursTests
{
    static readonly TimeSpan Tz = TimeSpan.FromHours(8);

    static DateTimeOffset At(int month, int day, int hour, int minute = 0, int year = 2026) =>
        new(year, month, day, hour, minute, 0, Tz);

    static WorkSession Session(
        string id,
        DateTimeOffset start,
        DateTimeOffset? end,
        DateTimeOffset? seen = null,
        string projectRoot = "",
        string projectName = "",
        string githubSlug = "") =>
        new(id, start, end, seen ?? end ?? start, "", "", projectRoot, projectName, githubSlug);

    [Fact]
    public void Format_BandWindows()
    {
        Assert.Equal(WorkHoursBand.Night, WorkHoursFormat.BandOfHour(0));
        Assert.Equal(WorkHoursBand.Morning, WorkHoursFormat.BandOfHour(9));
        Assert.Equal(WorkHoursBand.Afternoon, WorkHoursFormat.BandOfHour(12));
        Assert.Equal(WorkHoursBand.Evening, WorkHoursFormat.BandOfHour(20));
        Assert.Equal("晚上", WorkHoursFormat.BandLabel(WorkHoursBand.Evening));
        Assert.Equal("18–24 時", WorkHoursFormat.BandWindow(WorkHoursBand.Evening));
    }

    [Fact]
    public void Format_DurationAndTone()
    {
        Assert.Equal("0 分", WorkHoursFormat.Duration(TimeSpan.Zero));
        Assert.Equal("42 分", WorkHoursFormat.Duration(TimeSpan.FromMinutes(42)));
        Assert.Equal("2 時", WorkHoursFormat.Duration(TimeSpan.FromHours(2)));
        Assert.Equal("2 時 15 分", WorkHoursFormat.Duration(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15)));
        Assert.Equal("1:05", WorkHoursFormat.Compact(TimeSpan.FromMinutes(65)));
        Assert.Equal("ok", WorkHoursFormat.Tone(TimeSpan.FromHours(7.9)));
        Assert.Equal("warn", WorkHoursFormat.Tone(TimeSpan.FromHours(8)));
        Assert.Equal("over", WorkHoursFormat.Tone(TimeSpan.FromHours(10)));
    }

    [Fact]
    public void Range_WeekStartsMonday()
    {
        var (start, end) = WorkHoursAggregator.Range(WorkHoursView.Week, new DateOnly(2026, 8, 26));
        Assert.Equal(new DateOnly(2026, 8, 24), start);
        Assert.Equal(new DateOnly(2026, 8, 30), end);
    }

    [Fact]
    public void Range_MonthQuarterYear()
    {
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            WorkHoursAggregator.Range(WorkHoursView.Month, new DateOnly(2026, 8, 26)));
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)),
            WorkHoursAggregator.Range(WorkHoursView.Quarter, new DateOnly(2026, 8, 26)));
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            WorkHoursAggregator.Range(WorkHoursView.Year, new DateOnly(2026, 8, 26)));
    }

    [Fact]
    public void Shift_MovesByViewUnit()
    {
        var week = new DateOnly(2026, 8, 26);
        Assert.Equal(new DateOnly(2026, 8, 31), WorkHoursAggregator.Shift(WorkHoursView.Week, week, 1));
        Assert.Equal(new DateOnly(2026, 9, 1), WorkHoursAggregator.Shift(WorkHoursView.Month, week, 1));
        Assert.Equal(new DateOnly(2026, 10, 1), WorkHoursAggregator.Shift(WorkHoursView.Quarter, week, 1));
        Assert.Equal(new DateOnly(2027, 1, 1), WorkHoursAggregator.Shift(WorkHoursView.Year, week, 1));
    }

    [Fact]
    public void SplitDays_CutsAtMidnight()
    {
        var sessions = new[]
        {
            Session("a", At(8, 26, 22, 0), At(8, 27, 2, 0)),
        };
        var days = WorkHoursAggregator.SplitDays(sessions, At(8, 27, 3, 0));
        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 8, 26), days[0].Date);
        Assert.Equal(TimeSpan.FromHours(2), days[0].Duration);
        Assert.Equal("22:00–00:00", days[0].Periods[0].ClockLabel);
        Assert.Equal(new DateOnly(2026, 8, 27), days[1].Date);
        Assert.Equal(TimeSpan.FromHours(2), days[1].Duration);
        Assert.Equal("00:00–02:00", days[1].Periods[0].ClockLabel);
    }

    [Fact]
    public void SplitDays_MergesOverlappingSameDay()
    {
        var sessions = new[]
        {
            Session("a", At(8, 26, 9, 0), At(8, 26, 12, 0)),
            Session("b", At(8, 26, 11, 30), At(8, 26, 13, 0)),
        };
        var days = WorkHoursAggregator.SplitDays(sessions, At(8, 26, 18, 0));
        Assert.Single(days);
        Assert.Equal(TimeSpan.FromHours(4), days[0].Duration);
        Assert.Single(days[0].Periods);
        Assert.Equal("09:00–13:00", days[0].Periods[0].ClockLabel);
    }

    [Fact]
    public void Build_WeekIncludesEmptyDaysAndLiveSession()
    {
        var now = At(8, 26, 15, 0);
        var sessions = new[]
        {
            Session("open", At(8, 26, 9, 0), null, At(8, 26, 14, 50)),
        };
        var report = WorkHoursAggregator.Build(sessions, WorkHoursView.Week, new DateOnly(2026, 8, 26), now);
        Assert.Equal("2026年8月24日–8月30日", report.Title);
        Assert.Equal(7, report.Days.Count);
        Assert.Equal(7, report.Chart.Count);
        Assert.Equal(1, report.WorkedDays);
        Assert.Equal(TimeSpan.FromHours(6), report.Total);
        Assert.True(report.Chart.Single(b => b.IsCurrent).Start == new DateOnly(2026, 8, 26));
        Assert.False(report.CanGoNext(new DateOnly(2026, 8, 26)));
        Assert.True(report.CanGoNext(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void SplitDays_FillsHourlyBucketsAcrossHourBoundary()
    {
        var sessions = new[]
        {
            Session("a", At(8, 26, 10, 45), At(8, 26, 11, 15)),
        };
        var days = WorkHoursAggregator.SplitDays(sessions, At(8, 26, 18, 0));
        var day = Assert.Single(days);
        Assert.Equal(24, day.Hourly.Count);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Hourly[10]);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Hourly[11]);
        Assert.Equal(TimeSpan.Zero, day.Hourly[9]);
    }

    [Fact]
    public void Build_Clock_PrefersEveningAndSummarizesShare()
    {
        var sessions = new[]
        {
            Session("a", At(8, 26, 20, 0), At(8, 26, 23, 0)),
        };
        var report = WorkHoursAggregator.Build(sessions, WorkHoursView.Week, new DateOnly(2026, 8, 26), At(8, 26, 23, 30));
        Assert.Equal(WorkHoursBand.Evening, report.Clock.Dominant);
        Assert.Contains("晚上", report.Clock.Summary);
        Assert.Equal(100, report.Clock.Bands.Single(b => b.Band == WorkHoursBand.Evening).Percent);
        Assert.Equal(TimeSpan.FromHours(1), report.Clock.Hourly[20]);
        Assert.Equal(TimeSpan.FromHours(1), report.Clock.Hourly[21]);
        Assert.Equal(TimeSpan.FromHours(1), report.Clock.Hourly[22]);
    }

    [Fact]
    public void Build_Clock_EmptyRangeHasNoDominantBand()
    {
        var report = WorkHoursAggregator.Build([], WorkHoursView.Week, new DateOnly(2026, 8, 26), At(8, 26, 12, 0));
        Assert.Null(report.Clock.Dominant);
        Assert.Contains("看不出作息偏好", report.Clock.Summary);
        Assert.All(report.Clock.Bands, band => Assert.Equal(0, band.Percent));
    }

    [Fact]
    public void Build_YearUsesMonthBucketsAndHidesEmptyDays()
    {
        var sessions = new[]
        {
            Session("a", At(8, 26, 9, 0), At(8, 26, 17, 0)),
            Session("b", At(1, 5, 10, 0), At(1, 5, 12, 0)),
        };
        var report = WorkHoursAggregator.Build(sessions, WorkHoursView.Year, new DateOnly(2026, 8, 26), At(8, 26, 18, 0));
        Assert.Equal("2026年", report.Title);
        Assert.Equal(12, report.Chart.Count);
        Assert.Equal(2, report.Days.Count);
        Assert.Equal(TimeSpan.FromHours(2), report.Chart[0].Duration);
        Assert.Equal(TimeSpan.FromHours(8), report.Chart[7].Duration);
    }

    [Fact]
    public void Store_StartTouchEndAndRecoverOrphan()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-hours-" + Guid.NewGuid().ToString("N") + ".json");
        var clock = At(8, 26, 9, 0);
        try
        {
            var store = new WorkHoursStore(path, () => clock);
            var first = store.Start(projectRoot: @"E:\proj\foo", projectName: "Foo", githubSlug: "acme/foo");
            Assert.Null(first.EndedAt);
            Assert.Equal("Foo", first.ProjectName);
            clock = At(8, 26, 10, 0);
            store.Touch();
            Assert.Equal(TimeSpan.FromHours(1), store.CurrentDuration());
            Assert.Equal(TimeSpan.FromHours(1), store.TodayDuration());

            var recovered = new WorkHoursStore(path, () => At(8, 26, 12, 0));
            recovered.Start();
            var closed = recovered.Sessions.Single(s => s.Id == first.Id);
            Assert.Equal(At(8, 26, 10, 0), closed.EndedAt);
            Assert.Equal(TimeSpan.Zero, recovered.Sessions.Single(s => s.Id != first.Id).Duration(At(8, 26, 12, 0)));
            recovered.End();
            Assert.Null(recovered.Current);
            recovered.End();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Store_CorruptFileStartsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-hours-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{not-json");
        try
        {
            var store = new WorkHoursStore(path, () => At(8, 26, 9, 0));
            Assert.Empty(store.Sessions);
            store.Start();
            Assert.Single(store.Sessions);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Person_GithubLoginClaimsLocalAndKeepsOtherAccountSeparate()
    {
        Assert.Equal("github:sjvann", WorkHoursPerson.GithubKey("Sjvann"));
        Assert.Equal("github:sjvann", WorkHoursPerson.GithubKey("Sjvann", "github.com"));
        Assert.Equal("github:ghe.corp.com:sjvann", WorkHoursPerson.GithubKey("Sjvann", "ghe.corp.com"));
        Assert.True(WorkHoursPerson.BelongsTo("", "github:sjvann"));
        Assert.True(WorkHoursPerson.BelongsTo("local:user", "github:sjvann"));
        Assert.False(WorkHoursPerson.BelongsTo("github:other", "github:sjvann"));

        var path = Path.Combine(Path.GetTempPath(), "ai-hours-" + Guid.NewGuid().ToString("N") + ".json");
        var clock = At(8, 26, 9, 0);
        try
        {
            var store = new WorkHoursStore(path, () => clock);
            store.Start();
            Assert.StartsWith("local:", store.Current!.Value.PersonKey);

            store.Identify(WorkHoursPerson.GithubKey("sjvann"), "@sjvann");
            Assert.Equal("github:sjvann", store.Current!.Value.PersonKey);
            Assert.All(store.Sessions, s => Assert.Equal("github:sjvann", s.PersonKey));

            clock = At(8, 26, 18, 0);
            store.End();

            store.Start("github:other", "@other", @"E:\proj\bar", "Bar");
            store.Identify(WorkHoursPerson.GithubKey("other"), "@other");
            var mine = Assert.Single(store.VisibleSessions);
            Assert.Equal("github:other", mine.PersonKey);
            Assert.Equal(2, store.Sessions.Count);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Store_StartWithoutProject_DoesNotCountAsTodayBillable()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-hours-" + Guid.NewGuid().ToString("N") + ".json");
        var clock = At(8, 26, 9, 0);
        try
        {
            var store = new WorkHoursStore(path, () => clock);
            store.Start();
            clock = At(8, 26, 11, 0);
            store.Touch();
            Assert.Equal(TimeSpan.FromHours(2), store.CurrentDuration());
            Assert.Equal(TimeSpan.Zero, store.TodayDuration());
            Assert.False(store.Current!.Value.HasProject);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Store_SwitchProject_SplitsSessionsAndKeepsSameProject()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-hours-" + Guid.NewGuid().ToString("N") + ".json");
        var clock = At(8, 26, 9, 0);
        try
        {
            var store = new WorkHoursStore(path, () => clock);
            var first = store.SwitchProject(@"E:\proj\foo", "Foo");
            clock = At(8, 26, 11, 0);
            var again = store.SwitchProject(@"E:\proj\foo", "Foo", "acme/foo");
            Assert.Equal(first.Id, again.Id);
            Assert.Equal("acme/foo", again.GithubSlug);
            Assert.Null(again.EndedAt);

            clock = At(8, 26, 12, 0);
            var second = store.SwitchProject(@"E:\proj\bar", "Bar");
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal("Bar", second.ProjectName);
            var closed = store.Sessions.Single(s => s.Id == first.Id);
            Assert.Equal(At(8, 26, 12, 0), closed.EndedAt);
            Assert.Equal(TimeSpan.FromHours(3), closed.Duration(clock));
            Assert.Equal(TimeSpan.FromHours(3), store.TodayDuration());

            store.End();
            Assert.Null(store.Current);
            Assert.Equal(TimeSpan.FromHours(3), store.TodayDuration());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Store_TwoInstances_KeepSeparateOpenProjects()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-hours-multi-" + Guid.NewGuid().ToString("N") + ".json");
        var clock = At(8, 26, 9, 0);
        try
        {
            var first = new WorkHoursStore(path, () => clock);
            var second = new WorkHoursStore(path, () => clock);
            first.SwitchProject(@"E:\proj\foo", "Foo");
            second.SwitchProject(@"E:\proj\bar", "Bar");
            Assert.Equal("Foo", first.Current!.Value.ProjectName);
            Assert.Equal("Bar", second.Current!.Value.ProjectName);
            Assert.Null(first.Current!.Value.EndedAt);
            Assert.Null(second.Current!.Value.EndedAt);
            clock = At(8, 26, 10, 0);
            first.Touch();
            Assert.Null(second.Current!.Value.EndedAt);
            Assert.Equal("Bar", second.Current!.Value.ProjectName);
            Assert.Equal(2, first.Sessions.Count(s => s.EndedAt is null));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            var lockFile = path + ".lock";
            if (File.Exists(lockFile))
                File.Delete(lockFile);
        }
    }

    [Fact]
    public void Store_ReadsLegacySessionsWithoutProject()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-hours-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
            {
              "personKey": "local:user",
              "personLabel": "user（本機）",
              "sessions": [
                {
                  "id": "legacy",
                  "startedAt": "2026-08-26T09:00:00+08:00",
                  "lastSeenAt": "2026-08-26T12:00:00+08:00",
                  "endedAt": "2026-08-26T12:00:00+08:00",
                  "personKey": "local:user",
                  "personLabel": "user（本機）"
                }
              ]
            }
            """);
        try
        {
            var store = new WorkHoursStore(path, () => At(8, 26, 13, 0));
            var session = Assert.Single(store.Sessions);
            Assert.False(session.HasProject);
            Assert.Equal(WorkHoursProject.UnallocatedName, session.DisplayName);
            Assert.Equal(TimeSpan.Zero, store.TodayDuration());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Aggregator_GroupsBySlugAndKeepsUnallocatedSeparate()
    {
        var now = At(8, 26, 18, 0);
        var sessions = new[]
        {
            Session("a", At(8, 26, 9, 0), At(8, 26, 12, 0), projectRoot: @"E:\clone-a", projectName: "Foo", githubSlug: "acme/foo"),
            Session("b", At(8, 26, 13, 0), At(8, 26, 15, 0), projectRoot: @"E:\clone-b", projectName: "Foo", githubSlug: "acme/foo"),
            Session("c", At(8, 26, 16, 0), At(8, 26, 17, 0), projectRoot: @"E:\solo", projectName: "Solo"),
            Session("legacy", At(8, 26, 8, 0), At(8, 26, 8, 30)),
        };
        var summaries = WorkHoursAggregator.SummarizeProjects(sessions, new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 30), now);
        Assert.Equal(3, summaries.Count);
        Assert.Equal("gh:acme/foo", summaries[0].Key);
        Assert.Equal(TimeSpan.FromHours(5), summaries[0].Duration);
        Assert.Equal("Solo", summaries[1].Name);
        Assert.Equal(TimeSpan.FromHours(1), summaries[1].Duration);
        Assert.True(summaries[2].IsUnallocated);
        Assert.Equal(TimeSpan.FromMinutes(30), summaries[2].Duration);

        var filtered = WorkHoursAggregator.FilterProject(sessions, "gh:acme/foo");
        var report = WorkHoursAggregator.Build(filtered, WorkHoursView.Week, new DateOnly(2026, 8, 26), now);
        Assert.Equal(TimeSpan.FromHours(5), report.Total);

        var sheet = WorkHoursAggregator.BuildTimesheet(
            sessions, WorkHoursView.Week, new DateOnly(2026, 8, 26), now, "github:sjvann", "@sjvann");
        Assert.Equal(TimeSpan.FromHours(6), sheet.BillableTotal);
        Assert.Equal(TimeSpan.FromMinutes(30), sheet.UnallocatedTotal);
        Assert.Equal(2, sheet.Projects.Count);
        Assert.DoesNotContain(sheet.Projects, p => p.Key == WorkHoursProject.UnallocatedKey);
    }

    [Fact]
    public void Contributions_FiltersIssuesAndPullsByRange()
    {
        const string issuesJson = """
            [
              {
                "number": 12,
                "title": "修好啟動",
                "state": "CLOSED",
                "url": "https://github.com/acme/foo/issues/12",
                "closedAt": "2026-08-26T02:00:00Z",
                "updatedAt": "2026-08-26T02:00:00Z",
                "assignees": [{"login": "sjvann"}]
              },
              {
                "number": 3,
                "title": "上個月的",
                "state": "CLOSED",
                "url": "https://github.com/acme/foo/issues/3",
                "closedAt": "2026-07-01T00:00:00Z",
                "updatedAt": "2026-07-01T00:00:00Z",
                "assignees": [{"login": "sjvann"}]
              }
            ]
            """;
        const string pullsJson = """
            [
              {
                "number": 18,
                "title": "Add auth",
                "state": "MERGED",
                "url": "https://github.com/acme/foo/pull/18",
                "createdAt": "2026-08-20T00:00:00Z",
                "mergedAt": "2026-08-26T04:00:00Z"
              },
              {
                "number": 19,
                "title": "WIP",
                "state": "OPEN",
                "url": "https://github.com/acme/foo/pull/19",
                "createdAt": "2026-08-25T08:00:00Z",
                "mergedAt": ""
              },
              {
                "number": 2,
                "title": "舊 PR",
                "state": "MERGED",
                "url": "https://github.com/acme/foo/pull/2",
                "createdAt": "2026-07-01T00:00:00Z",
                "mergedAt": "2026-07-02T00:00:00Z"
              }
            ]
            """;
        var start = new DateOnly(2026, 8, 24);
        var end = new DateOnly(2026, 8, 30);
        var issues = WorkHoursContributions.FromClosedIssues(
            GitHubIssues.ParseIssues(issuesJson), start, end, "gh:acme/foo", "Foo", "acme/foo");
        Assert.Equal(12, Assert.Single(issues).Number);
        Assert.Equal("修好啟動", issues[0].Title);

        var pulls = WorkHoursContributions.ParsePulls(pullsJson);
        Assert.Equal(3, pulls.Count);
        var items = WorkHoursContributions.FromPulls(pulls, start, end, "gh:acme/foo", "Foo", "acme/foo");
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Number == 18 && i.Kind == "pr");
        Assert.Contains(items, i => i.Number == 19 && i.Kind == "pr");
        Assert.DoesNotContain(items, i => i.Number == 2);
    }

    [Fact]
    public void Export_WritesMarkdownAndCsvWithFixedHeaders()
    {
        var now = At(8, 26, 18, 0);
        var sessions = new[]
        {
            Session("a", At(8, 26, 9, 0), At(8, 26, 12, 20), projectRoot: @"E:\proj\foo", projectName: "Foo", githubSlug: "acme/foo"),
        };
        var sheet = WorkHoursAggregator.BuildTimesheet(
            sessions, WorkHoursView.Week, new DateOnly(2026, 8, 26), now, "github:sjvann", "@sjvann");
        var item = new TimesheetItem(
            "issue", 12, "修好啟動", "https://github.com/acme/foo/issues/12", "CLOSED",
            At(8, 26, 10, 0), "gh:acme/foo", "Foo", "acme/foo");
        sheet = sheet.WithContributions([item]);

        Assert.Equal("timesheet-20260824-20260830-sjvann", WorkHoursExport.FilePrefix(sheet.RangeStart, sheet.RangeEnd, sheet.PersonKey));
        var md = WorkHoursExport.Markdown(sheet);
        Assert.Contains("# 工時報告單", md);
        Assert.Contains("@sjvann", md);
        Assert.Contains("3 時 20 分", md);
        Assert.Contains("#12 修好啟動", md);
        Assert.Contains("Foo（acme/foo）", md);

        var hours = WorkHoursExport.HoursCsv(sheet);
        Assert.StartsWith(WorkHoursExport.HoursHeader, hours);
        Assert.Contains("github:sjvann,@sjvann,Foo,", hours);
        Assert.Contains("acme/foo,2026-08-26,3,20,1", hours);

        var items = WorkHoursExport.ItemsCsv(sheet);
        Assert.StartsWith(WorkHoursExport.ItemsHeader, items);
        Assert.Contains("issue,12,修好啟動,", items);

        var folder = Path.Combine(Path.GetTempPath(), "ai-hours-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = WorkHoursExport.Write(folder, sheet);
            Assert.Equal(3, files.Count);
            Assert.All(files, path => Assert.True(File.Exists(path)));
            var bom = File.ReadAllBytes(files[1]);
            Assert.True(bom.Length >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Project_FileSlugAndKey()
    {
        Assert.Equal("sjvann", WorkHoursProject.FileSlug("github:Sjvann"));
        Assert.Equal("local-user", WorkHoursProject.FileSlug("local:user"));
        Assert.Equal("gh:acme/foo", WorkHoursProject.Key("Acme/Foo", @"E:\other"));
        Assert.StartsWith("path:", WorkHoursProject.Key("", @"E:\solo"));
        Assert.Equal(WorkHoursProject.UnallocatedKey, WorkHoursProject.Key("", ""));
        Assert.Equal("未掛專案", WorkHoursProject.DisplayName("", ""));
    }
}
