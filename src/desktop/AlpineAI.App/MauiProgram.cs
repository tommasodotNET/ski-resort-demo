using Avalonia.Controls.Maui;
using Avalonia.Controls.Maui.Essentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace AlpineAI.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<AlpineApplication>()
            .UseAvaloniaApp().UseAvaloniaEssentials();
        builder.Logging.AddConsole();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}

public sealed class AlpineApplication(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page)
        {
            Title = "AlpineAI - Ski Resort Dashboard",
            Width = 1380,
            Height = 920,
            MinimumWidth = 640,
            MinimumHeight = 600
        };
        window.Destroying += async (_, _) => await page.StopAsync();
        return window;
    }
}
