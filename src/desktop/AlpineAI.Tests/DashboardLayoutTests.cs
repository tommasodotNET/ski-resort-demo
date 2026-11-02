using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AlpineAI.App;
using AlpineAI.Core;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Maui.Platform;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Xunit;
using Xunit.Abstractions;
using NativeControl = Avalonia.Controls.Control;
using Dispatcher = Avalonia.Threading.Dispatcher;
using Point = Avalonia.Point;

namespace AlpineAI.Tests;

[Collection("Native desktop")]
public sealed class DashboardLayoutTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ArchitectureSwitchesReleaseMessagesAndRecoverFromLongErrors()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await session.Dispatch(async () =>
        {
            var application = (LayoutApplication)Avalonia.Application.Current!;
            var window = application.MainWindow;
            var page = (MainPage)Microsoft.Maui.Controls.Application.Current!.Windows[0].Page!;
            var injectedUrl = Environment.GetEnvironmentVariable("ALPINEAI_SERVER_URL");
            using var handler = new AdvisorHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            try
            {
                Environment.SetEnvironmentVariable("ALPINEAI_SERVER_URL", null);
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                typeof(MainPage).GetField("advisor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, new AdvisorClient(http));
                typeof(MainPage).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, new CancellationTokenSource());
                var picker = Assert.IsType<Picker>(Find(page, "advisor-architecture"));
                var messages = Assert.IsType<VerticalStackLayout>(((ScrollView)Find(page, "chat-messages")).Content);
                long baseline = 0;
                var retiredMessages = new List<WeakReference>();
                using var process = Process.GetCurrentProcess();
                for (var i = 0; i < 100; i++)
                {
                    if (i > 0) retiredMessages.Add(new WeakReference(messages.Children[0]));
                    picker.SelectedIndex = i % 2 == 0 ? 1 : 0;
                    while (!picker.IsEnabled) await Task.Delay(1, timeout.Token);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.Single(messages.Children);
                    Assert.Contains(Descendants(messages).OfType<Label>(), label => label.Text == handler.ExpectedText);
                    Assert.Equal(i % 2 == 0 ? "/responses/skill" : "/responses/a2a", handler.LastPath);
                    Assert.True(Find(page, "new-chat").IsEnabled);
                    if (i == 19) baseline = GC.GetTotalMemory(forceFullCollection: true);
                }
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var retained = GC.GetTotalMemory(forceFullCollection: true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
                process.Refresh();
                output.WriteLine($"100 architecture switches: managed growth {(retained - baseline) / 1048576d:F1} MiB; RSS {process.WorkingSet64 / 1048576d:F1} MiB; peak RSS {process.PeakWorkingSet64 / 1048576d:F1} MiB.");
                Assert.Equal(100, handler.Requests);
                Assert.All(retiredMessages, message => Assert.False(message.IsAlive, "A removed chat bubble is still retained."));
                Assert.InRange(retained - baseline, long.MinValue, 32 * 1024 * 1024);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ALPINEAI_SERVER_URL", injectedUrl);
                await page.StopAsync();
                window.Close();
            }
            return true;
        }, timeout.Token);
    }

    [Fact]
    public async Task DashboardUsesCompactNativeRowsAndNoBackendControls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await session.Dispatch(async () =>
        {
            var application = (LayoutApplication)Avalonia.Application.Current!;
            var window = application.MainWindow;
            var page = (MainPage)Microsoft.Maui.Controls.Application.Current!.Windows[0].Page!;
            var injectedUrl = Environment.GetEnvironmentVariable("ALPINEAI_SERVER_URL");
            try
            {
                Environment.SetEnvironmentVariable("ALPINEAI_SERVER_URL", null);
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Contains(Descendants(page).OfType<Label>(), label => label.IsVisible && label.Text.Contains("ALPINEAI_SERVER_URL"));
                Assert.DoesNotContain(Descendants(page), view => view.AutomationId is "server-url" or "connect");
                Assert.Single(Descendants(page).OfType<Entry>());

                var lifts = new[]
                {
                    new Lift("l1", "Summit Gondola", "open", 64, 1.6, ["s1", "s2"]),
                    new Lift("l2", "Alpine Express", "maintenance", 15, 2.2, ["s3", "s4"]),
                    new Lift("l3", "Eagle Chair", "closed", 0, 0, ["s5", "s6"]),
                    new Lift("l4", "Beginner T-Bar", "open", 11, 1.2, ["s7"]),
                    new Lift("l5", "Kids Magic Carpet", "open", 48, 7.2, ["s8"])
                };
                var slopes = Enumerable.Range(1, 8).Select(i => new Slope(
                    $"s{i}", i == 1 ? "A very long slope name that must not push the status off the row" : $"Slope {i}",
                    new[] { "black", "blue", "red", "green" }[(i - 1) % 4],
                    i != 5, 123.9, i % 2 == 0, $"l{(i <= 6 ? (i + 1) / 2 : i - 3)}")).ToArray();
                Render(page, "RenderWeather", new Weather(-7.3, 18.2, 1.2, 6562));
                Render(page, "RenderSafety", new Safety(.45, [new Incident("Fall", "North Face", "minor", null)]));
                Render(page, "RenderLifts", lifts, slopes);

                foreach (var width in new[] { 1380, 1024, 768, 640 })
                {
                    window.Width = width;
                    window.Height = 920;
                    window.UpdateLayout();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    window.UpdateLayout();

                    foreach (var lift in lifts)
                    {
                        var header = Platform(Find(page, $"lift-{lift.LiftId}-header"));
                        Assert.InRange(header.Bounds.Height, 35, 37);
                    }
                    foreach (var slope in slopes)
                    {
                        var row = Platform(Find(page, $"slope-{slope.SlopeId}"));
                        var status = Platform(Find(page, $"slope-{slope.SlopeId}-status"));
                        Assert.InRange(row.Bounds.Height, 31, 33);
                        var right = status.TranslatePoint(new Point(status.Bounds.Width, 0), row)!.Value.X;
                        Assert.InRange(right, row.Bounds.Width - 17, row.Bounds.Width - 15);
                        Assert.InRange(Platform(Find(page, $"difficulty-{slope.SlopeId}")).Bounds.Width, 15, 17);
                    }
                    var content = Assert.IsType<VerticalStackLayout>(((Border)Find(page, "lift-l1")).Content);
                    Assert.InRange(Platform(content).Bounds.Height, 99, 101);
                    Assert.True(Platform(Find(page, "chat-messages")).Bounds.Height > 0);
                }
                return true;
            }
            finally
            {
                Environment.SetEnvironmentVariable("ALPINEAI_SERVER_URL", injectedUrl);
                await page.StopAsync();
                window.Close();
            }
        }, timeout.Token);
    }

    private static void Render(MainPage page, string method, params object[] data) =>
        typeof(MainPage).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, data);

    private static VisualElement Find(MainPage page, string id) =>
        Descendants(page).Single(view => view.AutomationId == id);

    private static NativeControl Platform(VisualElement view) =>
        Assert.IsAssignableFrom<NativeControl>(view.Handler?.PlatformView);

    private static IEnumerable<VisualElement> Descendants(IVisualTreeElement element)
    {
        if (element is VisualElement view) yield return view;
        foreach (var child in element.GetVisualChildren())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    public sealed class LayoutApplication : MauiAvaloniaApplication
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<LayoutApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();

        public override void RegisterServices()
        {
            ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown };
            base.RegisterServices();
        }
        public override void Initialize() => Styles.Add(new FluentTheme());
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

    private sealed class AdvisorHandler : HttpMessageHandler
    {
        private readonly string error = string.Concat(Enumerable.Repeat("DefaultAzureCredential: AzureCliCredential failed to invoke Azure CLI.\n", 200)).TrimEnd();
        public int Requests { get; private set; }
        public string? LastPath { get; private set; }
        public string? ExpectedText { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
            Requests++;
            LastPath = request.RequestUri!.AbsolutePath;
            if (Requests % 5 == 2)
            {
                ExpectedText = $"Error: {error}";
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent($"data: {JsonSerializer.Serialize(new { type = "response.failed", response = new { error = new { message = error } } })}\n\n", Encoding.UTF8, "text/event-stream")
                };
            }
            if (Requests % 5 == 4)
            {
                ExpectedText = $"Error: Responses API failed (503): {error}";
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(error) };
            }
            ExpectedText = string.Concat(Enumerable.Repeat("Clear skies. ", 256));
            var stream = string.Concat(Enumerable.Repeat("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Clear skies. \"}\n\n", 256))
                + "data: {\"type\":\"response.completed\"}\n\ndata: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") };
        }
    }
}

[CollectionDefinition("Native desktop", DisableParallelization = true)]
public sealed class NativeDesktopCollection;
