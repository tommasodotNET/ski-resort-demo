using Avalonia;
using Avalonia.Controls.Maui.Platform;
using Avalonia.Themes.Fluent;
using Microsoft.Maui.Hosting;

namespace AlpineAI.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<DesktopApplication>().UsePlatformDetect().WithInterFont()
            .LogToTrace().StartWithClassicDesktopLifetime(args);
}

public sealed class DesktopApplication : MauiAvaloniaApplication
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        MainWindow.Width = 1380;
        MainWindow.Height = 920;
        MainWindow.MinWidth = 640;
        MainWindow.MinHeight = 600;
    }
}
