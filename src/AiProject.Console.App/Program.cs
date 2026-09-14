using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Photino.NET;
using AiProject.Console.App.Services;
using AiProject.Console.CompanyClient;
using AiProject.Console.Core;
using AiProject.Console.Core.Build;
using AiProject.Console.Core.Stack;

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
        if (McpCli.IsRequested(args))
        {
            Environment.ExitCode = McpCli.Run(args);
            return;
        }

        Win32WindowIcon.BindProcessIdentity();
        LockedOutput.Sweep(AppContext.BaseDirectory);
        var webViewDir = ConsoleProcess.Register();

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddLogging();
        builder.Services.AddTransient<CompanyBaseAddressHandler>();
        builder.Services.AddHttpClient<ICompanyPlatformClient, CompanyPlatformClient>()
            .AddHttpMessageHandler<CompanyBaseAddressHandler>();
        builder.Services.AddSingleton<NativeUi>();
        builder.Services.AddSingleton<ConsoleSession>();
        builder.RootComponents.Add<App>("app");

        var app = builder.Build();
        var window = app.MainWindow.SetTemporaryFilesPath(webViewDir);
        window.LogVerbosity = 0;
        var native = app.Services.GetRequiredService<NativeUi>();
        native.Window = window;

        var iconFile = ResolveWindowIcon();
        window
            .SetTitle(AppInfo.WindowTitle())
            .SetNotificationRegistrationId(AppInfo.AppUserModelId)
            .SetNotificationsEnabled(false)
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1280, 820))
            .SetMinSize(960, 640)
            .SetMaximized(true)
            .Center();

        if (iconFile is not null)
            window.SetIconFile(iconFile);

        window.RegisterWindowCreatedHandler((_, _) => Win32WindowIcon.Apply(window, iconFile));
        window.RegisterWindowClosingHandler((_, _) =>
        {
            app.Services.GetRequiredService<ConsoleSession>().CloseWorkSession();
            ConsoleProcess.Release();
            return false;
        });

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
