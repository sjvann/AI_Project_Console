using Photino.NET;

namespace AiProject.Console.App.Services;

public sealed class NativeUi
{
    public PhotinoWindow? Window { get; set; }

    public async Task<string?> PickFolderAsync(string title = "選擇專案目錄")
    {
        if (Window is null)
            return null;
        var folders = await Window.ShowOpenFolderAsync(title, multiSelect: false).ConfigureAwait(false);
        return folders is { Length: > 0 } && !string.IsNullOrWhiteSpace(folders[0]) ? folders[0] : null;
    }

    public async Task<string[]?> PickFilesAsync(string title, params (string Name, string[] Ext)[] filters)
    {
        if (Window is null)
            return null;
        var mapped = filters.Select(f => (f.Name, f.Ext)).ToArray();
        return await Window.ShowOpenFileAsync(title, multiSelect: true, filters: mapped).ConfigureAwait(false);
    }

    public PhotinoDialogResult Message(
        string title,
        string text,
        PhotinoDialogButtons buttons = PhotinoDialogButtons.Ok,
        PhotinoDialogIcon icon = PhotinoDialogIcon.Info)
    {
        return Window?.ShowMessage(title, text, buttons, icon) ?? PhotinoDialogResult.Cancel;
    }

    public bool Confirm(string title, string text) =>
        Message(title, text, PhotinoDialogButtons.YesNo, PhotinoDialogIcon.Question) == PhotinoDialogResult.Yes;

    public PhotinoDialogResult YesNoCancel(string title, string text) =>
        Message(title, text, PhotinoDialogButtons.YesNoCancel, PhotinoDialogIcon.Question);

    public void Info(string title, string text) => Message(title, text);

    public void Warn(string title, string text) =>
        Message(title, text, PhotinoDialogButtons.Ok, PhotinoDialogIcon.Warning);

    public void Error(string title, string text) =>
        Message(title, text, PhotinoDialogButtons.Ok, PhotinoDialogIcon.Error);

    public void Close() => Window?.Close();

    public void SetTitle(string title)
    {
        if (Window is null || string.IsNullOrWhiteSpace(title))
            return;
        Window.SetTitle(title);
    }
}
