namespace AiProject.Company.Domain;

public sealed class AvailabilityCalculator : IAvailabilityCalculator
{
    public PersonAvailability ForWeek(Person person, DateOnly weekStart, IReadOnlyList<Assignment> assignments)
    {
        var weekEnd = weekStart.AddDays(6);
        decimal assigned = 0;
        foreach (var item in assignments.Where(a => a.PersonId == person.Id && a.Overlaps(weekStart, weekEnd)))
        {
            var overlapStart = item.Start > weekStart ? item.Start : weekStart;
            var overlapEnd = item.End < weekEnd ? item.End : weekEnd;
            var days = overlapEnd.DayNumber - overlapStart.DayNumber + 1;
            var weekDays = Math.Max(1, (item.End.DayNumber - item.Start.DayNumber + 1));
            assigned += item.PlannedHoursPerWeek * days / weekDays;
        }
        var unavailableDays = Enumerable.Range(0, 7).Count(i => person.IsUnavailableOn(weekStart.AddDays(i)));
        var cap = person.WeeklyHourCap * (7 - unavailableDays) / 7m;
        assigned = decimal.Round(assigned, 2);
        cap = decimal.Round(cap, 2);
        return new PersonAvailability(person.Id, cap, assigned, cap - assigned, assigned > cap);
    }
}

public sealed class AssignmentSuggester : IAssignmentSuggester
{
    readonly IAvailabilityCalculator _availability;

    public AssignmentSuggester(IAvailabilityCalculator availability) => _availability = availability;

    public IReadOnlyList<AssignmentSuggestion> Suggest(Project project, IReadOnlyList<Person> candidates, IReadOnlyList<Assignment> weekAssignments, DateOnly weekStart)
    {
        var stack = project.Name.ToLowerInvariant();
        var list = new List<AssignmentSuggestion>();
        foreach (var person in candidates.Where(p => p.Status == PersonStatus.Active && !p.IsDeleted))
        {
            var avail = _availability.ForWeek(person, weekStart, weekAssignments);
            if (avail.RemainingHours <= 0)
                continue;
            var hits = person.Skills.Count(s => stack.Contains(s, StringComparison.Ordinal));
            var reason = hits > 0
                ? $"技能吻合 {hits} 項，本週剩餘 {avail.RemainingHoursClamped} 小時"
                : $"本週剩餘 {avail.RemainingHoursClamped} 小時";
            list.Add(new AssignmentSuggestion(person.Id, person.DisplayName, avail.RemainingHoursClamped, hits, reason));
        }
        return list
            .OrderByDescending(s => s.SkillHits)
            .ThenByDescending(s => s.RemainingHours)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }
}

public sealed class TimesheetIdempotency : ITimesheetIdempotency
{
    public Outcome Decide(Timesheet? existing, bool incomingIsCorrection)
    {
        if (existing is null)
            return Outcome.Success();
        if (existing.Status == TimesheetStatus.Approved && !incomingIsCorrection)
            return Outcome.Fail(ErrorCodes.TimesheetApprovedImmutable, Messages.TimesheetApprovedImmutable);
        if (existing.Status == TimesheetStatus.Approved && incomingIsCorrection)
            return Outcome.Success();
        return Outcome.Success();
    }
}

public sealed class ProjectHealthPolicy : IProjectHealthPolicy
{
    readonly IMarginThresholds _thresholds;

    public ProjectHealthPolicy(IMarginThresholds thresholds) => _thresholds = thresholds;

    public ProjectHealth Evaluate(Project project, DateOnly today, int unassignedIssueDays, bool hoursMissing, decimal? marginPercent, bool requirementsCatalogReady = true)
    {
        var overdue = project.Milestones.Any(m => m.IsOverdue(today)) || project.Phases.Any(p => p.IsOverdue(today)) || today > project.TargetEnd;
        var dueSoon = !overdue && today.AddDays(14) >= project.TargetEnd;
        HealthTone schedule;
        string scheduleLabel;
        if (overdue)
        {
            schedule = HealthTone.Red;
            scheduleLabel = "逾期";
        }
        else if (dueSoon)
        {
            schedule = HealthTone.Yellow;
            scheduleLabel = "即將到期";
        }
        else
        {
            schedule = HealthTone.Green;
            scheduleLabel = "進度正常";
        }

        HealthTone staffing;
        string staffingLabel;
        if (unassignedIssueDays >= 3)
        {
            staffing = HealthTone.Red;
            staffingLabel = "派工缺口";
        }
        else if (hoursMissing)
        {
            staffing = HealthTone.Yellow;
            staffingLabel = "工時未傳";
        }
        else
        {
            staffing = HealthTone.Green;
            staffingLabel = "人力正常";
        }

        HealthTone margin;
        string marginLabel;
        if (project.ExcludeFromMarginKpi)
        {
            margin = HealthTone.Green;
            marginLabel = "不列入毛利";
        }
        else
        {
            var tone = _thresholds.Classify(marginPercent);
            margin = tone switch
            {
                "紅" => HealthTone.Red,
                "黃" => HealthTone.Yellow,
                "—" => HealthTone.Green,
                _ => HealthTone.Green,
            };
            marginLabel = marginPercent is null ? "無收入" : "毛利 " + MarginResult.Display(marginPercent);
        }

        var discovery = project.Phases.FirstOrDefault(p => p.Name == "需求");
        if (discovery is { LeftAt: null } && !requirementsCatalogReady && schedule == HealthTone.Green)
        {
            schedule = HealthTone.Yellow;
            scheduleLabel = "需求分析未就緒";
        }

        return new ProjectHealth(schedule, staffing, margin, scheduleLabel, staffingLabel, marginLabel);
    }
}

public sealed class HomeRouteResolver : IHomeRouteResolver
{
    public string PathFor(PlatformRole role)
    {
        if (role.LandsOnWarRoom())
            return "/war-room";
        if (role.LandsOnProjects())
            return "/projects";
        if (role.LandsOnPeople())
            return "/people";
        return "/me";
    }
}
