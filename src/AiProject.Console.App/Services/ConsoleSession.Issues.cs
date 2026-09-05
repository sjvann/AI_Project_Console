using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.GitHub;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    GithubIssue? _resumeIssueAfterBranch;

    public GithubIssue? ActiveIssue { get; private set; }
    public IReadOnlyList<GithubIssueComment> IssueComments { get; private set; } = [];
    public string IssueCommentDraft { get; set; } = "";
    public string IssueViewHint { get; private set; } = "";
    public bool IssueViewBusy { get; private set; }
    public string IssueBodyHtml => DocsMarkdown.ToSafeHtml(
        string.IsNullOrWhiteSpace(ActiveIssue?.Body) ? "（沒有內文）" : ActiveIssue!.Body);

    public bool CanSubmitIssueComment =>
        ActiveIssue is not null
        && !JobBusy
        && !IssueViewBusy
        && !string.IsNullOrWhiteSpace(IssueCommentDraft);

    public string IssuePrButtonLabel => PullRequest is { HasPr: true } ? "開啟 PR" : "建立 PR";

    public string? IssuePrBlockReason =>
        ActiveIssue is null || PullRequest is { HasPr: true }
            ? null
            : IssueCompletion.CreatePrBlockReason(
                GitBrief,
                GithubDraft.DefaultBranch,
                hasPr: false,
                PullRequest?.Error);

    public bool CanUseIssuePr =>
        ActiveIssue is not null
        && !JobBusy
        && (PullRequest is { HasPr: true } || string.IsNullOrEmpty(IssuePrBlockReason));

    public bool IssueNeedsTaskBranch =>
        ActiveIssue is not null && IssueCompletion.NeedsTaskBranch(GitBrief, GithubDraft.DefaultBranch);

    public string IssueBranchText => GitBrief?.Branch ?? "（未知）";

    public void OpenIssueDialog(GithubIssue issue)
    {
        if (!RequireCatalog())
            return;
        ActiveIssue = issue;
        IssueComments = [];
        IssueCommentDraft = "";
        IssueViewHint = "";
        IssueViewBusy = false;
        Dialog = "issue";
        Notify();
        _ = LoadIssueViewAsync(issue);
    }

    public void OpenIssueHelp(GithubIssue issue)
    {
        if (!RequireCatalog())
            return;
        AgentPrompt = CursorLauncher.BuildIssueAgentPrompt(Catalog!.Root, issue);
        AgentTitle = $"請 Agent 協助 · {issue.NumberText}";
        AgentIntro = AgentLaunchIntro();
        Dialog = "agent";
        Notify();
    }

    public Task OpenActiveIssueHelpAsync()
    {
        if (ActiveIssue is null)
            return Task.CompletedTask;
        OpenIssueHelp(ActiveIssue);
        return Task.CompletedTask;
    }

    public void OpenActiveIssueUrl()
    {
        if (ActiveIssue is not null)
            OpenIssueUrl(ActiveIssue);
    }

    public async Task SubmitIssueCommentAsync()
    {
        if (!CanSubmitIssueComment || !RequireCatalog() || ActiveIssue is null)
            return;
        var listed = ActiveIssue;
        var body = IssueCommentDraft.Trim();
        IssueViewBusy = true;
        IssueViewHint = "送出回應中…";
        Notify();
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog!).ConfigureAwait(false);
            await GitHubIssues.CommentAsync(Catalog!.Root, cfg, listed.Number, body, _cts.Token).ConfigureAwait(false);
            if (ActiveIssue is null || ActiveIssue.Number != listed.Number)
                return;
            IssueCommentDraft = "";
            await LoadIssueViewAsync(listed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ActiveIssue is null || ActiveIssue.Number != listed.Number)
                return;
            IssueViewHint = FirstLine(ex.Message);
            IssueViewBusy = false;
            Notify();
        }
    }

    public Task OpenIssuePrAsync()
    {
        var number = ActiveIssue?.Number;
        return OpenOrCreatePrAsync(number is > 0 ? number : null);
    }

    public Task OpenIssueTaskBranchAsync()
    {
        if (ActiveIssue is null)
            return Task.CompletedTask;
        _resumeIssueAfterBranch = ActiveIssue;
        return OpenBranchDialogAsync(
            IssueCompletion.SuggestIssueBranchName(ActiveIssue.Number, ActiveIssue.Title));
    }

    private async Task LoadIssueViewAsync(GithubIssue listed)
    {
        if (Catalog is null)
            return;
        IssueViewBusy = true;
        IssueViewHint = "載入討論中…";
        Notify();
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            var (issue, comments) = await GitHubIssues.ViewAsync(
                Catalog.Root, cfg, listed.Number, _cts.Token).ConfigureAwait(false);
            if (ActiveIssue is null || ActiveIssue.Number != listed.Number)
                return;
            ActiveIssue = issue with
            {
                CiTone = listed.CiTone,
                CiHint = listed.CiHint,
                PrUrl = listed.PrUrl,
                PrState = listed.PrState,
            };
            IssueComments = comments;
            IssueViewHint = comments.Count == 0 ? "還沒有討論。可在下方回應。" : "";
        }
        catch (Exception ex)
        {
            if (ActiveIssue is not null && ActiveIssue.Number == listed.Number)
                IssueViewHint = FirstLine(ex.Message);
        }
        finally
        {
            IssueViewBusy = false;
            Notify();
        }
    }

    private void ClearIssueView()
    {
        if (ActiveIssue is null
            && IssueComments.Count == 0
            && string.IsNullOrEmpty(IssueCommentDraft)
            && string.IsNullOrEmpty(IssueViewHint)
            && !IssueViewBusy
            && _resumeIssueAfterBranch is null)
            return;
        ActiveIssue = null;
        IssueComments = [];
        IssueCommentDraft = "";
        IssueViewHint = "";
        IssueViewBusy = false;
    }
}
