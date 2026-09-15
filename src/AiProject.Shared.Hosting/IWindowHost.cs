namespace AiProject.Shared.Hosting;

/// <summary>宿主無關的對話結果（對應 Photino／Avalonia 等原生對話）。</summary>
public enum HostDialogResult
{
    Ok = 0,
    Yes = 1,
    No = 2,
    Cancel = 3,
}

/// <summary>
/// 桌面視窗能力抽象。Core／Razor／Session 只依賴此介面，不得引用 Photino 或 Avalonia 類型（AD-9、AD-10、K0-1）。
/// </summary>
public interface IWindowHost
{
    Task<string?> PickFolderAsync(string title = "選擇專案目錄");

    Task<string[]?> PickFilesAsync(string title, params (string Name, string[] Ext)[] filters);

    bool Confirm(string title, string text);

    HostDialogResult YesNoCancel(string title, string text);

    void Info(string title, string text);

    void Warn(string title, string text);

    void Error(string title, string text);

    void Close();

    void SetTitle(string title);
}
