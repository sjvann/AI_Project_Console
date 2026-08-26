using AiProject.Console.Core.WorkHours;

namespace AiProject.Console.Core.Tests;

public class WorkHoursTests
{
    static readonly TimeSpan Tz = TimeSpan.FromHours(8);

    static DateTimeOffset At(int month, int day, int hour, int minute = 0, int year = 2026) =>
        new(year, month, day, hour, minute, 0, Tz);

    static WorkSession Session(string id, DateTimeOffset start, DateTimeOffset? end, DateTimeOffset? seen = null) =>
        new(id, start, end, seen ?? end ?? start);

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
            var first = store.Start();
            Assert.Null(first.EndedAt);
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

            store.Start("github:other", "@other");
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
}
