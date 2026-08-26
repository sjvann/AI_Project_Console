namespace AiProject.Console.Core.WorkHours;

public enum WorkHoursView
{
    Week,
    Month,
    Quarter,
    Year,
}

public readonly record struct WorkSession(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset LastSeenAt,
    string PersonKey = "",
    string PersonLabel = "")
{
    public DateTimeOffset CloseAt(DateTimeOffset now) => EndedAt ?? now;

    public TimeSpan Duration(DateTimeOffset now)
    {
        var end = CloseAt(now);
        var span = end - StartedAt;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }
}

public sealed record WorkTimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration
    {
        get
        {
            var span = End - Start;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }
    }

    public string ClockLabel => $"{WorkHoursFormat.Clock(Start)}–{WorkHoursFormat.Clock(End)}";
}

public enum WorkHoursBand
{
    Night,
    Morning,
    Afternoon,
    Evening,
}

public sealed record WorkDaySlice(
    DateOnly Date,
    TimeSpan Duration,
    IReadOnlyList<WorkTimeRange> Periods,
    IReadOnlyList<TimeSpan> Hourly)
{
    public bool HasWork => Duration > TimeSpan.Zero && Periods.Count > 0;

    public static WorkDaySlice Empty(DateOnly date) =>
        new(date, TimeSpan.Zero, [], new TimeSpan[24]);
}

public sealed record WorkHoursBandShare(
    WorkHoursBand Band,
    string Label,
    string Window,
    TimeSpan Duration,
    int Percent);

public sealed record WorkHoursClock(
    IReadOnlyList<TimeSpan> Hourly,
    IReadOnlyList<WorkHoursBandShare> Bands,
    WorkHoursBand? Dominant,
    string Summary)
{
    public TimeSpan HourlyMax => Hourly.Count == 0 ? TimeSpan.Zero : Hourly.Max();
}

public sealed record WorkHoursBucket(
    string Label,
    DateOnly Start,
    TimeSpan Duration,
    bool IsCurrent);

public sealed record WorkHoursReport(
    WorkHoursView View,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    string Title,
    TimeSpan Total,
    int WorkedDays,
    TimeSpan AverageWorkedDay,
    TimeSpan LongestDay,
    TimeSpan ChartMax,
    IReadOnlyList<WorkHoursBucket> Chart,
    IReadOnlyList<WorkDaySlice> Days,
    WorkHoursClock Clock)
{
    public bool CanGoNext(DateOnly today) => RangeEnd < today;
}

public static class WorkHoursFormat
{
    public const int SoftHours = 8;
    public const int HardHours = 10;

    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        var minutes = (int)Math.Floor(value.TotalMinutes);
        var hours = minutes / 60;
        var remain = minutes % 60;
        if (hours == 0)
            return $"{remain} 分";
        return remain == 0 ? $"{hours} 時" : $"{hours} 時 {remain} 分";
    }

    public static string Compact(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        var minutes = (int)Math.Floor(value.TotalMinutes);
        return $"{minutes / 60}:{minutes % 60:D2}";
    }

    public static string Hours(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        return value.TotalHours.ToString("0.#") + " 時";
    }

    public static string Clock(DateTimeOffset value) => value.ToString("HH:mm");

    public static string Tone(TimeSpan today)
    {
        if (today >= TimeSpan.FromHours(HardHours))
            return "over";
        if (today >= TimeSpan.FromHours(SoftHours))
            return "warn";
        return "ok";
    }

    public static string Weekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "週一",
        DayOfWeek.Tuesday => "週二",
        DayOfWeek.Wednesday => "週三",
        DayOfWeek.Thursday => "週四",
        DayOfWeek.Friday => "週五",
        DayOfWeek.Saturday => "週六",
        _ => "週日",
    };

    public static string DayTitle(DateOnly date) =>
        $"{Weekday(date)} {date.Month}/{date.Day}";

    public static WorkHoursBand BandOfHour(int hour) => hour switch
    {
        < 6 => WorkHoursBand.Night,
        < 12 => WorkHoursBand.Morning,
        < 18 => WorkHoursBand.Afternoon,
        _ => WorkHoursBand.Evening,
    };

    public static string BandLabel(WorkHoursBand band) => band switch
    {
        WorkHoursBand.Night => "深夜",
        WorkHoursBand.Morning => "上午",
        WorkHoursBand.Afternoon => "下午",
        _ => "晚上",
    };

    public static string BandWindow(WorkHoursBand band) => band switch
    {
        WorkHoursBand.Night => "0–6 時",
        WorkHoursBand.Morning => "6–12 時",
        WorkHoursBand.Afternoon => "12–18 時",
        _ => "18–24 時",
    };
}
