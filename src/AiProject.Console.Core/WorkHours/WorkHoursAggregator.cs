namespace AiProject.Console.Core.WorkHours;

public static class WorkHoursAggregator
{
    public static WorkHoursReport Build(
        IEnumerable<WorkSession> sessions,
        WorkHoursView view,
        DateOnly anchor,
        DateTimeOffset now)
    {
        var (start, end) = Range(view, anchor);
        var today = DateOnly.FromDateTime(now.Date);
        var days = SplitDays(sessions, now)
            .Where(d => d.Date >= start && d.Date <= end)
            .ToList();
        var dayMap = days.ToDictionary(d => d.Date, d => d);
        var listed = ListDays(view, start, end, dayMap);
        var worked = listed.Where(d => d.HasWork).ToList();
        var total = Sum(listed.Select(d => d.Duration));
        var longest = worked.Count == 0 ? TimeSpan.Zero : worked.Max(d => d.Duration);
        var average = worked.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(total.Ticks / worked.Count);
        var chart = ChartBuckets(view, start, end, dayMap, today);
        var chartMax = chart.Count == 0 ? TimeSpan.Zero : chart.Max(b => b.Duration);
        if (chartMax < TimeSpan.FromHours(1))
            chartMax = TimeSpan.FromHours(1);

        return new WorkHoursReport(
            view,
            start,
            end,
            Title(view, start, end),
            total,
            worked.Count,
            average,
            longest,
            chartMax,
            chart,
            listed,
            BuildClock(listed));
    }

    public static (DateOnly Start, DateOnly End) Range(WorkHoursView view, DateOnly anchor) =>
        view switch
        {
            WorkHoursView.Week => (StartOfWeek(anchor), StartOfWeek(anchor).AddDays(6)),
            WorkHoursView.Month => MonthRange(anchor),
            WorkHoursView.Quarter => QuarterRange(anchor),
            WorkHoursView.Year => (new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year, 12, 31)),
            _ => MonthRange(anchor),
        };

    public static DateOnly Shift(WorkHoursView view, DateOnly anchor, int steps)
    {
        if (steps == 0)
            return anchor;
        return view switch
        {
            WorkHoursView.Week => StartOfWeek(anchor).AddDays(7 * steps),
            WorkHoursView.Month => FirstOfMonth(anchor).AddMonths(steps),
            WorkHoursView.Quarter => QuarterRange(anchor).Start.AddMonths(3 * steps),
            WorkHoursView.Year => new DateOnly(anchor.Year + steps, 1, 1),
            _ => FirstOfMonth(anchor).AddMonths(steps),
        };
    }

    public static IReadOnlyList<WorkDaySlice> SplitDays(IEnumerable<WorkSession> sessions, DateTimeOffset now)
    {
        var map = new Dictionary<DateOnly, List<WorkTimeRange>>();
        foreach (var session in sessions)
        {
            var cursor = session.StartedAt;
            var end = session.CloseAt(now);
            if (end <= cursor)
                continue;
            while (cursor < end)
            {
                var next = NextMidnight(cursor);
                var sliceEnd = next < end ? next : end;
                if (sliceEnd > cursor)
                {
                    var date = DateOnly.FromDateTime(cursor.DateTime);
                    if (!map.TryGetValue(date, out var list))
                    {
                        list = [];
                        map[date] = list;
                    }
                    list.Add(new WorkTimeRange(cursor, sliceEnd));
                }
                cursor = sliceEnd;
            }
        }

        return map
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var merged = Merge(kv.Value);
                return new WorkDaySlice(kv.Key, Sum(merged.Select(p => p.Duration)), merged, HoursFrom(merged));
            })
            .ToList();
    }

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    static IReadOnlyList<WorkDaySlice> ListDays(
        WorkHoursView view,
        DateOnly start,
        DateOnly end,
        IReadOnlyDictionary<DateOnly, WorkDaySlice> map)
    {
        if (view is WorkHoursView.Quarter or WorkHoursView.Year)
        {
            return map.Values
                .Where(d => d.Date >= start && d.Date <= end && d.HasWork)
                .OrderBy(d => d.Date)
                .ToList();
        }

        var list = new List<WorkDaySlice>();
        for (var d = start; d <= end; d = d.AddDays(1))
            list.Add(map.TryGetValue(d, out var slice) ? slice : WorkDaySlice.Empty(d));
        return list;
    }

    static IReadOnlyList<WorkHoursBucket> ChartBuckets(
        WorkHoursView view,
        DateOnly start,
        DateOnly end,
        IReadOnlyDictionary<DateOnly, WorkDaySlice> map,
        DateOnly today)
    {
        if (view is WorkHoursView.Week or WorkHoursView.Month)
        {
            var list = new List<WorkHoursBucket>();
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                var duration = map.TryGetValue(d, out var slice) ? slice.Duration : TimeSpan.Zero;
                var label = view == WorkHoursView.Week
                    ? WorkHoursFormat.Weekday(d)
                    : d.Day.ToString();
                list.Add(new WorkHoursBucket(label, d, duration, d == today));
            }
            return list;
        }

        var buckets = new List<WorkHoursBucket>();
        var cursor = new DateOnly(start.Year, start.Month, 1);
        while (cursor <= end)
        {
            var duration = map.Values
                .Where(d => d.Date.Year == cursor.Year && d.Date.Month == cursor.Month)
                .Select(d => d.Duration)
                .DefaultIfEmpty()
                .Aggregate(TimeSpan.Zero, (a, b) => a + b);
            var current = today.Year == cursor.Year && today.Month == cursor.Month;
            buckets.Add(new WorkHoursBucket($"{cursor.Month}月", cursor, duration, current));
            cursor = cursor.AddMonths(1);
        }
        return buckets;
    }

    static (DateOnly Start, DateOnly End) MonthRange(DateOnly anchor)
    {
        var first = FirstOfMonth(anchor);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    static (DateOnly Start, DateOnly End) QuarterRange(DateOnly anchor)
    {
        var firstMonth = ((anchor.Month - 1) / 3) * 3 + 1;
        var first = new DateOnly(anchor.Year, firstMonth, 1);
        return (first, first.AddMonths(3).AddDays(-1));
    }

    static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    static string Title(WorkHoursView view, DateOnly start, DateOnly end) =>
        view switch
        {
            WorkHoursView.Week => $"{start.Year}年{start.Month}月{start.Day}日–{end.Month}月{end.Day}日",
            WorkHoursView.Month => $"{start.Year}年{start.Month}月",
            WorkHoursView.Quarter => $"{start.Year}年第{((start.Month - 1) / 3) + 1}季",
            WorkHoursView.Year => $"{start.Year}年",
            _ => $"{start}–{end}",
        };

    static DateTimeOffset NextMidnight(DateTimeOffset value)
    {
        var date = DateOnly.FromDateTime(value.DateTime).AddDays(1);
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), value.Offset);
    }

    static IReadOnlyList<WorkTimeRange> Merge(IReadOnlyList<WorkTimeRange> ranges)
    {
        if (ranges.Count <= 1)
            return ranges.OrderBy(r => r.Start).ToList();
        var ordered = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<WorkTimeRange>();
        var current = ordered[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (next.Start <= current.End)
            {
                var end = next.End > current.End ? next.End : current.End;
                current = new WorkTimeRange(current.Start, end);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }

    static WorkHoursClock BuildClock(IReadOnlyList<WorkDaySlice> days)
    {
        var hourly = new TimeSpan[24];
        foreach (var day in days)
        {
            var count = Math.Min(24, day.Hourly.Count);
            for (var i = 0; i < count; i++)
                hourly[i] += day.Hourly[i];
        }

        var total = Sum(hourly);
        var bands = Enum.GetValues<WorkHoursBand>().Select(band =>
        {
            var duration = TimeSpan.Zero;
            for (var hour = 0; hour < 24; hour++)
            {
                if (WorkHoursFormat.BandOfHour(hour) == band)
                    duration += hourly[hour];
            }

            var percent = total <= TimeSpan.Zero
                ? 0
                : (int)Math.Round(100 * duration.TotalMinutes / total.TotalMinutes);
            return new WorkHoursBandShare(
                band,
                WorkHoursFormat.BandLabel(band),
                WorkHoursFormat.BandWindow(band),
                duration,
                percent);
        }).ToList();

        WorkHoursBand? dominant = total <= TimeSpan.Zero
            ? null
            : bands.MaxBy(band => band.Duration)!.Band;
        var summary = dominant is null
            ? "這個期間還沒有工時，還看不出作息偏好。"
            : $"偏好{WorkHoursFormat.BandLabel(dominant.Value)}工作（{WorkHoursFormat.BandWindow(dominant.Value)}，佔 {bands.First(band => band.Band == dominant).Percent}%）。";
        return new WorkHoursClock(hourly, bands, dominant, summary);
    }

    static TimeSpan[] HoursFrom(IReadOnlyList<WorkTimeRange> periods)
    {
        var hours = new TimeSpan[24];
        foreach (var period in periods)
            AddToHours(hours, period.Start, period.End);
        return hours;
    }

    static void AddToHours(TimeSpan[] hours, DateTimeOffset start, DateTimeOffset end)
    {
        var cursor = start;
        while (cursor < end)
        {
            var nextHour = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0, cursor.Offset)
                .AddHours(1);
            var sliceEnd = nextHour < end ? nextHour : end;
            if (sliceEnd > cursor)
                hours[cursor.Hour] += sliceEnd - cursor;
            cursor = sliceEnd;
        }
    }

    static TimeSpan Sum(IEnumerable<TimeSpan> values)
    {
        var total = TimeSpan.Zero;
        foreach (var value in values)
            total += value;
        return total;
    }
}
