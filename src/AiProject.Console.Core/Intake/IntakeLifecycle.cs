namespace AiProject.Console.Core.Intake;

public static class IntakeLifecycle
{
    public static IntakeRecord ApplyTraces(IntakeRecord intake, IReadOnlyDictionary<int, IssueTrace> traces)
    {
        foreach (var item in intake.Items)
        {
            if (item.IssueNumber is not int number || !traces.TryGetValue(number, out var trace))
                continue;
            item.IssueUrl = string.IsNullOrEmpty(item.IssueUrl) ? trace.Url : item.IssueUrl;
            if (!string.IsNullOrEmpty(trace.PrUrl))
            {
                item.PrUrl = trace.PrUrl;
                item.PrState = trace.PrState;
            }
            if (!string.IsNullOrEmpty(trace.CiTone))
            {
                item.CiTone = trace.CiTone;
                item.CiHint = trace.CiHint;
            }
        }
        var (stage, block) = Derive(intake, traces);
        intake.Stage = stage;
        intake.BlockReason = block;
        return intake;
    }

    public static (string Stage, string Block) Derive(
        IntakeRecord intake,
        IReadOnlyDictionary<int, IssueTrace>? traces = null)
    {
        traces ??= new Dictionary<int, IssueTrace>();
        if (intake.Billed)
            return (IntakeStages.Billed, "");
        if (!string.IsNullOrEmpty(intake.AcceptedAt))
            return (IntakeStages.Accepted, "");

        var issued = intake.Items.Where(i => i.HasIssue).ToList();
        if (issued.Count == 0)
        {
            if (IntakeGates.BlockPublish(intake) is null)
                return (IntakeStages.Split, "");
            if (IntakeGates.BlockSplit(intake) is null)
                return (IntakeStages.Split, IntakeGates.BlockPublish(intake) ?? "");
            if (IntakeGates.BlockDesignReady(intake) is null)
                return (IntakeStages.DesignReady, IntakeGates.BlockSplit(intake) ?? "");
            return (IntakeStages.Draft, IntakeGates.BlockDesignReady(intake) ?? "");
        }

        var closed = 0;
        var assigned = 0;
        var hasPr = 0;
        var pendingCi = 0;
        var failedCi = 0;
        var green = 0;
        var merged = 0;
        foreach (var item in issued)
        {
            traces.TryGetValue(item.IssueNumber!.Value, out var trace);
            var state = trace?.State ?? "";
            if (state.Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
                closed++;
            if (trace is { Assignees.Count: > 0 } || !string.IsNullOrWhiteSpace(item.Assignee))
                assigned++;
            if (!string.IsNullOrEmpty(trace?.PrUrl) || !string.IsNullOrEmpty(item.PrUrl))
                hasPr++;
            var tone = trace?.CiTone ?? item.CiTone;
            if (tone == "warn")
                failedCi++;
            else if (tone is "wait" or "run")
                pendingCi++;
            if (trace?.ChecksGreen == true || tone == "ok")
                green++;
            if (trace?.Merged == true || string.Equals(item.PrState, "MERGED", StringComparison.OrdinalIgnoreCase))
                merged++;
        }

        if (!string.IsNullOrEmpty(intake.DeployRunId) || intake.SkipDeploy)
        {
            if (!string.IsNullOrEmpty(intake.ReleaseTag) || intake.SkipDeploy)
                return (IntakeStages.Deployed, "");
        }
        if (!string.IsNullOrEmpty(intake.ReleaseTag))
            return (IntakeStages.Released, intake.SkipDeploy ? "" : "尚未部署（可略過）。");
        if (merged == issued.Count && issued.Count > 0)
            return (IntakeStages.Merged, "可依進件發行 Release。");
        if (green == issued.Count && hasPr == issued.Count && failedCi == 0 && pendingCi == 0)
            return (IntakeStages.Review, "");
        if (pendingCi > 0 || failedCi > 0 || hasPr > 0)
            return (IntakeStages.Verify, failedCi > 0 ? "CI 未通過。" : "遠端檢查進行中。");
        if (assigned > 0 || hasPr > 0)
            return (IntakeStages.Doing, assigned == 0 ? "已發出但尚未指派。" : "");
        return (IntakeStages.Issued, "已發出，等待工程師接受。");
    }

    public static string IssueBody(IntakeRecord intake, IntakeWorkItem item)
    {
        var lines = new List<string>
        {
            $"intake: {intake.Id}",
            "",
            intake.Body.Trim(),
            "",
            "## 驗收條件",
        };
        foreach (var c in item.AcceptanceCriteria.Where(x => !string.IsNullOrWhiteSpace(x)))
            lines.Add("- [ ] " + c.Trim());
        if (intake.IsDesignChange)
        {
            lines.Add("");
            lines.Add("## 設計變更");
            lines.Add("- 現況：" + intake.AsIs.Trim());
            lines.Add("- 期望：" + intake.ToBe.Trim());
            lines.Add("- 影響：" + intake.Impact.Trim());
        }
        if (intake.DesignDocs.Count > 0)
        {
            lines.Add("");
            lines.Add("## 設計文件");
            foreach (var doc in intake.DesignDocs.Where(d => !string.IsNullOrWhiteSpace(d)))
                lines.Add("- " + doc.Trim());
        }
        return string.Join('\n', lines);
    }

    public static string PublishPreview(IntakeRecord intake)
    {
        var pending = intake.Items.Where(i => !i.HasIssue).ToList();
        if (pending.Count == 0)
            return "";
        var parts = pending.Select(item =>
            item.Title + "\n" + IssueBody(intake, item));
        var text = string.Join("\n---\n", parts);
        return text.Length <= 900 ? text : text[..900] + "…";
    }

    public static string ReleaseNotes(IntakeRecord intake)
    {
        var lines = new List<string>
        {
            $"# {intake.Id} {intake.Title}",
            "",
            intake.KindLabel + " · " + intake.GithubSlug,
            "",
        };
        foreach (var item in intake.Items.Where(i => i.HasIssue))
            lines.Add($"- #{item.IssueNumber} {item.Title}");
        return string.Join('\n', lines);
    }
}
