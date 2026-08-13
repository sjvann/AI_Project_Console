using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Photino.NET;
using AiProject.Console.App.Services;
using AiProject.Console.Core;

namespace AiProject.Console.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddLogging();
        builder.Services.AddSingleton<NativeUi>();
        builder.Services.AddSingleton<ConsoleSession>();
        builder.RootComponents.Add<App>("app");

        var app = builder.Build();
        var native = app.Services.GetRequiredService<NativeUi>();
        native.Window = app.MainWindow;

        app.MainWindow
            .SetTitle($"{AppInfo.Product} v{AppInfo.Version}")
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1280, 820))
            .SetMinSize(960, 640)
            .Center();

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
