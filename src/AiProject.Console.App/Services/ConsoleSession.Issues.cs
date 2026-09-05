using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.GitHub;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    GithubIssue? _resumeIssueAfterBranch;
    IReadOnlyDictionary<string, string> _issueImageMap = new Dictionary<string, string>();

    public GithubIssue? ActiveIssue { get; private set; }
    public IReadOnlyList<GithubIssueComment> IssueComments { get; private set; } = [];
    public string IssueCommentDraft { get; set; } = "";
    public string IssueViewHint { get; private set; } = "";
    public bool IssueViewBusy { get; private set; }

    public string IssueBodyHtml => DocsMarkdown.ToIssueHtml(
        string.IsNullOrWhiteSpace(ActiveIssue?.Body) ? "（沒有內文）" : ActiveIssue!.Body,
        _issueImageMap);

    public string IssueCommentHtml(string? body) => DocsMarkdown.ToIssueHtml(body, _issueImageMap);

    public bool CanSubmitIssueComment =>
        ActiveIssue is not null
        && !JobBusy
        && !IssueViewBusy
        && !string.IsNullOrWhiteSpace(IssueCommentDraft);

    public bool CanAskIssueAgent =>
        ActiveIssue is { IsOpen: true } && !JobBusy;

    public bool CanCloseActiveIssue =>
        ActiveIssue is { IsOpen: true } && !JobBusy && !IssueViewBusy;

    public bool CanFinishIssueOnBranch =>
        ActiveIssue is { IsOpen: true } && !JobBusy;

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
        ActiveIssue is { IsOpen: true }
        && IssueCompletion.NeedsTaskBranch(GitBrief, GithubDraft.DefaultBranch);

    public bool ShowIssueFinishSection => ActiveIssue is not null;

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
        _issueImageMap = new Dictionary<string, string>();
        Dialog = "issue";
        Notify();
        _ = LoadIssueViewAsync(issue);
    }

    public void OpenIssueHelp(GithubIssue issue)
    {
        if (!RequireCatalog() || !CanAskIssueAgent)
            return;
        _resumeIssueAfterBranch = issue;
        AgentPrompt = CursorLauncher.BuildIssueAgentPrompt(
            Catalog!.Root,
            issue,
            IssueComments,
            string.IsNullOrWhiteSpace(IssueCommentDraft) ? null : IssueCommentDraft);
        AgentTitle = $"請 Agent 協助 · {issue.NumberText}";
        AgentIntro = AgentLaunchIntro();
        Dialog = "agent";
        Notify();
    }

    public Task OpenActiveIssueHelpAsync()
    {
        if (!CanAskIssueAgent || ActiveIssue is null)
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
        IssueViewHint = "送出討論中…";
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

    public async Task CloseActiveIssueAsync()
    {
        if (!CanCloseActiveIssue || !RequireCatalog() || ActiveIssue is null)
            return;
        var listed = ActiveIssue;
        var note = IssueCommentDraft.Trim();
        var confirm = string.IsNullOrEmpty(note)
            ? $"確定關閉 {listed.NumberText}「{listed.Title}」？這表示討論後認為不是問題、或不必再做。"
            : $"會先把討論送出，再關閉 {listed.NumberText}。確定？";
        if (!_native.Confirm("結案", confirm))
            return;
        IssueViewBusy = true;
        IssueViewHint = "結案中…";
        Notify();
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog!).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(note))
            {
                await GitHubIssues.CommentAsync(Catalog!.Root, cfg, listed.Number, note, _cts.Token).ConfigureAwait(false);
                IssueCommentDraft = "";
            }
            await GitHubIssues.CloseAsync(Catalog!.Root, cfg, listed.Number, _cts.Token, "not planned").ConfigureAwait(false);
            JobText = $"已關閉 {listed.NumberText}";
            await RefreshIssuesAsync().ConfigureAwait(false);
            CloseDialog();
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
        if (!CanUseIssuePr)
            return Task.CompletedTask;
        var number = ActiveIssue?.Number;
        return OpenOrCreatePrAsync(number is > 0 ? number : null);
    }

    public Task OpenIssueTaskBranchAsync()
    {
        if (ActiveIssue is null || !CanFinishIssueOnBranch)
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
        IssueViewHint = "載入內容中…";
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
            var wanted = IssueMarkdown.FindHttpImageUrls(issue.Body)
                .Concat(comments.SelectMany(c => IssueMarkdown.FindHttpImageUrls(c.Body)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            _issueImageMap = await IssueImages.ToDataUrisAsync(
                wanted, Catalog.Root, cfg, _cts.Token).ConfigureAwait(false);
            if (ActiveIssue is null || ActiveIssue.Number != listed.Number)
                return;
            IssueViewHint = wanted.Count > 0 && _issueImageMap.Count == 0
                ? "截圖無法載入，可按「在 GitHub 開啟」查看。"
                : "";
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
            && _issueImageMap.Count == 0
            && _resumeIssueAfterBranch is null)
            return;
        ActiveIssue = null;
        IssueComments = [];
        IssueCommentDraft = "";
        IssueViewHint = "";
        IssueViewBusy = false;
        _issueImageMap = new Dictionary<string, string>();
    }
}
