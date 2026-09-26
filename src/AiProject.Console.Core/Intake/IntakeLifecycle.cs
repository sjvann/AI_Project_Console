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
        if (!string.IsNullOrEmpty(intake.AcceptedAt) || intake.Billed)
            return (IntakeStages.Accepted, "");

        var (stage, block) = DeriveProgress(intake, traces);
        if (intake.IsRecalled)
            return (stage, "已收回並通知相關 Issue。已入主線的程式不會自動還原。");
        if (intake.IsPaused)
            return (stage, string.IsNullOrEmpty(block) ? "已暫停執行。" : "已暫停執行。" + block);
        return (stage, block);
    }

    static (string Stage, string Block) DeriveProgress(
        IntakeRecord intake,
        IReadOnlyDictionary<int, IssueTrace> traces)
    {
        var issued = intake.Items.Where(i => i.HasIssue).ToList();
        if (issued.Count == 0)
            return (IntakeStages.Draft, IntakeGates.BlockPublish(intake) ?? "");

        var closed = 0;
        var assigned = 0;
        var hasPr = 0;
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
        }

        if (closed == issued.Count)
            return (IntakeStages.Accepted, "");
        if (assigned > 0 || hasPr > 0)
            return (IntakeStages.Doing, assigned == 0 ? "已發出但尚未指派。" : "");
        return (IntakeStages.Issued, "已發出，等待工程師接受。");
    }

    public static string HoldComment(IntakeRecord intake, string action, string actor, string note)
    {
        var who = string.IsNullOrWhiteSpace(actor) ? "需求台" : actor.Trim();
        var extra = string.IsNullOrWhiteSpace(note) ? "" : "\n\n說明：" + note.Trim();
        return action switch
        {
            "pause" =>
                $"需求台已暫停執行「{intake.Title}」（{intake.Id}），操作者 @{who}。請先停下相關實作與審查，待通知再開。{extra}",
            "resume" =>
                $"需求台已恢復執行「{intake.Title}」（{intake.Id}），操作者 @{who}。請依原任務繼續。{extra}",
            _ =>
                $"需求台收回「{intake.Title}」（{intake.Id}），操作者 @{who}。請停止實作；Issue 將關閉（不計畫進行）。已入主線的程式不會自動還原。{extra}",
        };
    }

    public static IReadOnlyList<string> PublishRelPaths(IntakeRecord intake)
    {
        var list = new List<string> { IntakeStore.RelPath };
        foreach (var doc in intake.DesignDocs.Where(d => !string.IsNullOrWhiteSpace(d)))
            list.Add(doc.Trim());
        foreach (var sketch in intake.Sketches.Where(s => !string.IsNullOrWhiteSpace(s.Path)))
            list.Add(sketch.Path.Trim());
        foreach (var crop in intake.Crops.Where(c => !string.IsNullOrWhiteSpace(c.Path)))
            list.Add(crop.Path.Trim());
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string IssueBody(IntakeRecord intake, IntakeWorkItem item, IntakeIssueLinks? links = null)
    {
        var lines = new List<string>
        {
            $"intake: {intake.Id}",
            "",
            intake.Body.Trim(),
        };
        var criteria = item.AcceptanceCriteria.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (criteria.Count > 0)
        {
            lines.Add("");
            lines.Add("## 驗收條件");
            foreach (var c in criteria)
                lines.Add("- [ ] " + c.Trim());
        }
        if (!string.IsNullOrWhiteSpace(intake.AsIs)
            || !string.IsNullOrWhiteSpace(intake.ToBe)
            || !string.IsNullOrWhiteSpace(intake.Impact))
        {
            lines.Add("");
            lines.Add("## 設計變更");
            if (!string.IsNullOrWhiteSpace(intake.AsIs))
                lines.Add("- 現況：" + intake.AsIs.Trim());
            if (!string.IsNullOrWhiteSpace(intake.ToBe))
                lines.Add("- 期望：" + intake.ToBe.Trim());
            if (!string.IsNullOrWhiteSpace(intake.Impact))
                lines.Add("- 影響：" + intake.Impact.Trim());
        }
        if (intake.DesignDocs.Count > 0)
        {
            lines.Add("");
            lines.Add("## 分析／設計文件");
            foreach (var doc in intake.DesignDocs.Where(d => !string.IsNullOrWhiteSpace(d)))
                lines.Add("- " + FileLink(doc.Trim(), links));
        }
        var attachments = intake.Sketches
            .Concat(intake.Crops)
            .Where(v => !string.IsNullOrWhiteSpace(v.Path))
            .ToList();
        if (attachments.Count > 0)
        {
            lines.Add("");
            lines.Add("## 附件");
            foreach (var visual in attachments)
            {
                var note = string.IsNullOrWhiteSpace(visual.Note) ? "" : " — " + visual.Note.Trim();
                lines.Add("- " + FileLink(visual.Path.Trim(), links) + note);
            }
        }
        return string.Join('\n', lines);
    }

    static string FileLink(string rel, IntakeIssueLinks? links)
    {
        if (links is null || string.IsNullOrWhiteSpace(links.WebUrl) || string.IsNullOrWhiteSpace(links.Branch))
            return rel;
        var name = FileName(rel);
        return $"[{name}]({IntakeDesignFiles.BlobUrl(links.WebUrl, links.Branch, rel)})";
    }

    static string FileName(string rel)
    {
        var norm = rel.Replace('\\', '/');
        var i = norm.LastIndexOf('/');
        var name = i >= 0 ? norm[(i + 1)..] : norm;
        return string.IsNullOrEmpty(name) ? rel : name;
    }

    public static string PublishPreview(IntakeRecord intake)
    {
        intake.EnsurePrimaryItem();
        var pending = intake.Items.Where(i => !i.HasIssue).ToList();
        if (pending.Count == 0)
            return "";
        var parts = pending.Select(item =>
            (string.IsNullOrWhiteSpace(intake.Title) ? item.Title : intake.Title) + "\n" + IssueBody(intake, item));
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
