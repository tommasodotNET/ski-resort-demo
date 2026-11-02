using AlpineAI.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace AlpineAI.App;

public sealed partial class MainPage : ContentPage
{
    private static readonly Color Slate = Color.FromArgb("#0f172a");
    private static readonly Color Panel = Color.FromArgb("#1b2538");
    private static readonly Color Tile = Color.FromArgb("#29364a");
    private static readonly Color Control = Color.FromArgb("#334155");
    private static readonly Color Muted = Color.FromArgb("#94a3b8");
    private static readonly Color Violet = Color.FromArgb("#c4b5fd");
    private readonly ILogger<MainPage> logger;
    private readonly Label connectionError = Text("", 12, Colors.Salmon);
    private readonly Grid dashboard = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly Grid columns = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly ContentView weatherContent = new();
    private readonly ContentView safetyContent = new();
    private readonly ContentView liftsContent = new();
    private readonly Label weatherError = Text("", 12, Colors.Salmon);
    private readonly Label safetyError = Text("", 12, Colors.Salmon);
    private readonly Label liftsError = Text("", 12, Colors.Salmon);
    private readonly ScrollView dashboardScroll;
    private readonly Border weatherCard;
    private readonly Border safetyCard;
    private readonly Border liftsCard;
    private readonly Border chatCard;
    private CancellationTokenSource? connection;
    private HttpClient? dataHttp;
    private HttpClient? chatHttp;
    private ResortClient? resort;
    private AdvisorClient? advisor;
    private Uri? origin;
    private Task? polling;
    private Task? chatting;
    private bool loaded;
    private bool? wideLayout;
    private bool? wideDashboard;

    public MainPage(ILogger<MainPage> logger)
    {
        this.logger = logger;
        Title = "AlpineAI";
        BackgroundColor = Slate;
        Padding = 0;
        connectionError.IsVisible = weatherError.IsVisible = safetyError.IsVisible = liftsError.IsVisible = false;
        connectionError.Margin = new Thickness(24, 8);
        weatherContent.Content = Text("Loading weather...", 14, Muted);
        safetyContent.Content = Text("Loading safety...", 14, Muted);
        liftsContent.Content = Text("Loading slopes & lifts...", 14, Muted);
        weatherCard = Card("🌨️ Weather", Color.FromArgb("#7dd3fc"), weatherContent, weatherError);
        safetyCard = Card("⚠️ Safety", Color.FromArgb("#fcd34d"), safetyContent, safetyError);
        liftsCard = Card("🏔️ Slopes & Lifts", Violet, liftsContent, liftsError);
        liftsCard.AutomationId = "slopes-lifts-card";
        dashboard.Add(weatherCard);
        dashboard.Add(safetyCard);
        dashboard.Add(liftsCard);
        dashboardScroll = new ScrollView { Content = dashboard };
        chatCard = CreateChat();
        columns.Add(dashboardScroll);
        columns.Add(chatCard);
        columns.Margin = 16;
        var heading = new HorizontalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(24, 16),
            Children =
            {
                Text("🏔️", 24),
                Text("AlpineAI", 20, Colors.White, true),
                Text("Ski Resort Dashboard", 16, Muted)
            }
        };
        var top = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { heading, new BoxView { Color = Tile, HeightRequest = 1 }, connectionError }
        };
        var root = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) }
        };
        root.Add(top);
        root.Add(columns, 0, 1);
        Content = root;
        SizeChanged += (_, _) => ArrangeDashboard();
        Loaded += (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            ArrangeDashboard();
            Connect();
        };
        UpdateChatControls();
    }

    private void ArrangeDashboard()
    {
        var wide = Width >= 1024;
        if (wideLayout != wide)
        {
            wideLayout = wide;
            columns.ColumnDefinitions.Clear();
            columns.RowDefinitions.Clear();
            columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            if (wide) columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            columns.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            if (!wide) columns.RowDefinitions.Add(new RowDefinition(new GridLength(1.3, GridUnitType.Star)));
            Grid.SetColumn(chatCard, wide ? 1 : 0);
            Grid.SetRow(chatCard, wide ? 0 : 1);
        }
        if (wide) columns.ColumnDefinitions[1].Width = Math.Clamp((Width - 48) / 3, 320, 480);
        var twoCards = Width >= 768;
        if (wideDashboard == twoCards) return;
        wideDashboard = twoCards;
        dashboard.ColumnDefinitions.Clear();
        dashboard.RowDefinitions.Clear();
        dashboard.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (twoCards) dashboard.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < (twoCards ? 2 : 3); i++) dashboard.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(safetyCard, twoCards ? 1 : 0);
        Grid.SetRow(safetyCard, twoCards ? 0 : 1);
        Grid.SetRow(liftsCard, twoCards ? 1 : 2);
        Grid.SetColumnSpan(liftsCard, twoCards ? 2 : 1);
    }

    private void Connect()
    {
        Uri target;
        try { target = ResortClient.ParseServerUri(Environment.GetEnvironmentVariable("ALPINEAI_SERVER_URL")); }
        catch (ArgumentException ex)
        {
            Report(ex, connectionError);
            weatherContent.Content = safetyContent.Content = liftsContent.Content = null;
            return;
        }
        origin = target;
        dataHttp = new HttpClient { BaseAddress = target, Timeout = TimeSpan.FromSeconds(15) };
        chatHttp = new HttpClient { BaseAddress = target, Timeout = Timeout.InfiniteTimeSpan };
        resort = new ResortClient(dataHttp);
        advisor = new AdvisorClient(chatHttp);
        connection = new CancellationTokenSource();
        messages.Clear();
        polling = PollAsync(connection.Token);
        chatting = SendAsync("Greet the user and briefly offer ski resort advisory help.", false);
    }

    private async Task PollAsync(CancellationToken token)
    {
        var interval = 10000;
        var configRead = DateTimeOffset.MinValue;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - configRead >= TimeSpan.FromSeconds(30))
                {
                    try
                    {
                        interval = (await resort!.GetConfigAsync(token)).PollingIntervalMs;
                        connectionError.IsVisible = false;
                    }
                    catch (Exception ex) when (IsRequestError(ex) && !token.IsCancellationRequested)
                    {
                        Report(ex, connectionError, $"Dashboard config unavailable; retaining {interval / 1000.0:0.#}s polling. ");
                    }
                    configRead = DateTimeOffset.UtcNow;
                }
                await Task.WhenAll(
                    RefreshAsync(async () => RenderWeather(await resort!.GetWeatherAsync(token)), weatherError, token),
                    RefreshAsync(async () => RenderSafety(await resort!.GetSafetyAsync(token)), safetyError, token),
                    RefreshAsync(async () =>
                    {
                        var lifts = resort!.GetLiftsAsync(token);
                        var slopes = resort.GetSlopesAsync(token);
                        await Task.WhenAll(lifts, slopes);
                        RenderLifts(await lifts, await slopes);
                    }, liftsError, token));
                await Task.Delay(interval, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshAsync(Func<Task> refresh, Label error, CancellationToken token)
    {
        try
        {
            await refresh();
            error.Text = "";
            error.IsVisible = false;
        }
        catch (Exception ex) when (IsRequestError(ex) && !token.IsCancellationRequested)
        {
            Report(ex, error, "Refresh failed; displayed data may be stale. ");
        }
    }

    private static bool IsRequestError(Exception ex) =>
        ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException
            or InvalidOperationException or KeyNotFoundException or IOException;

    private void Report(Exception ex, Label label, string prefix = "")
    {
        logger.LogError(ex, "{Context}", prefix.Length == 0 ? "Desktop request failed" : prefix);
        label.Text = prefix + ex.Message;
        label.TextColor = Colors.Salmon;
        label.IsVisible = true;
    }

    public async Task StopAsync()
    {
        if (connection is not null) await connection.CancelAsync();
        await StopVoiceAsync();
        if (polling is not null) await polling;
        if (chatting is not null) await chatting;
        connection?.Dispose();
        connection = null;
        polling = null;
        chatting = null;
        dataHttp?.Dispose();
        chatHttp?.Dispose();
        advisor = null;
        resort = null;
        UpdateChatControls();
    }

    private static Label Text(string text, double size = 14, Color? color = null, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        TextColor = color ?? Colors.White,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        LineBreakMode = LineBreakMode.WordWrap,
        VerticalOptions = LayoutOptions.Center
    };

    private static Button Button(string text, string id) => new()
    {
        Text = text,
        AutomationId = id,
        TextColor = Colors.White,
        BackgroundColor = Control,
        CornerRadius = 8,
        Padding = new Thickness(12, 8),
        FontSize = 13
    };

    private static Border Box(View content, Color? color = null, double padding = 12, double radius = 12) => new()
    {
        Content = content,
        BackgroundColor = color ?? Tile,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = radius },
        Padding = padding
    };

    private static Border Card(string title, Color color, View content, Label error) =>
        Box(new VerticalStackLayout { Spacing = 12, Children = { Text(title, 18, color, true), content, error } }, Panel, 20, 16);
}
