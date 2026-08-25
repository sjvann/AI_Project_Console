using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Photino.NET;
using AiProject.Console.App.Services;
using AiProject.Console.Core;

namespace AiProject.Console.App;

internal static class Program
{
    private static string? ResolveWindowIcon()
    {
        var dir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(dir, "wwwroot", "favicon.ico"),
            Path.Combine(dir, "Assets", "app.ico"),
            Path.Combine(dir, "app.ico"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Win32WindowIcon.BindProcessIdentity();

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddLogging();
        builder.Services.AddSingleton<NativeUi>();
        builder.Services.AddSingleton<ConsoleSession>();
        builder.RootComponents.Add<App>("app");

        var app = builder.Build();
        app.MainWindow.LogVerbosity = 0;
        var native = app.Services.GetRequiredService<NativeUi>();
        native.Window = app.MainWindow;

        var iconFile = ResolveWindowIcon();
        var window = app.MainWindow
            .SetTitle($"{AppInfo.Product} v{AppInfo.Version}")
            .SetNotificationRegistrationId(AppInfo.AppUserModelId)
            .SetNotificationsEnabled(false)
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1280, 820))
            .SetMinSize(960, 640)
            .Center();

        if (iconFile is not null)
            window.SetIconFile(iconFile);

        window.RegisterWindowCreatedHandler((_, _) => Win32WindowIcon.Apply(window, iconFile));

        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            try
            {
                app.MainWindow.ShowMessage("未處理例外", error.ExceptionObject?.ToString() ?? "");
            }
            catch
            {
                // ignore
            }
        };

        app.Run();
    }
}
