using AlpineAI.Core;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace AlpineAI.App;

public sealed partial class MainPage
{
    private void RenderWeather(Weather weather)
    {
        var stats = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 12,
            RowSpacing = 12
        };
        stats.Add(Stat("Temperature", $"{weather.Temperature:0.0}°C"), 0, 0);
        stats.Add(Stat("Wind Speed", $"{weather.WindSpeed:0.0} km/h"), 1, 0);
        stats.Add(Stat("Snow Intensity", $"{weather.SnowIntensity:0.0} cm/h"), 0, 1);
        stats.Add(Stat("Visibility", $"{weather.Visibility:0} m"), 1, 1);
        var chart = new GraphicsView { Drawable = new WeatherChart(weather), HeightRequest = 144, AutomationId = "weather-chart" };
        SemanticProperties.SetDescription(chart, "Weather bar chart. Visibility is divided by ten for comparison; exact measurements are shown above.");
        weatherContent.Content = new VerticalStackLayout { Spacing = 12, Children = { stats, chart } };
    }

    private static Border Stat(string label, string value)
    {
        var stat = Box(new VerticalStackLayout { Spacing = 0, Children = { Text(label, 12, Muted), Text(value, 14) } }, radius: 8);
        stat.Padding = new Thickness(12, 8);
        return stat;
    }

    private static Color RiskColor(string risk) => Color.FromArgb(risk switch
    {
        "Low" => "#22c55e",
        "Moderate" => "#eab308",
        "Considerable" => "#f97316",
        "High" => "#ef4444",
        _ => "#dc2626"
    });

    private void RenderSafety(Safety safety)
    {
        var color = RiskColor(safety.RiskLevel);
        var content = new VerticalStackLayout { Spacing = 12 };
        var risk = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 16
        };
        var bar = new GraphicsView
        {
            Drawable = new RiskBar(safety.AvalancheRiskIndex, color),
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "avalanche-risk"
        };
        SemanticProperties.SetDescription(bar, $"Avalanche risk: {safety.RiskLevel}, {safety.AvalancheRiskIndex:0.00} out of 1.");
        risk.Add(bar);
        risk.Add(Text(safety.RiskLevel, 14, color, true), 1);
        content.Add(risk);
        content.Add(Text($"Avalanche Risk Index: {safety.AvalancheRiskIndex:0.00} / 1.0", 12, Muted));
        if (safety.IncidentReports.Length > 0)
        {
            var incidents = new VerticalStackLayout { Spacing = 6 };
            incidents.Add(Text("RECENT INCIDENTS", 12, Muted));
            foreach (var incident in safety.IncidentReports.Take(4))
            {
                var report = Box(Text($"{incident.IncidentType} — {incident.Location} ({incident.Severity})", 12), radius: 8);
                report.Padding = new Thickness(12, 8);
                incidents.Add(report);
            }
            content.Add(incidents);
        }
        safetyContent.Content = content;
    }

    private void RenderLifts(Lift[] lifts, Slope[] slopes)
    {
        var content = new VerticalStackLayout { Spacing = 12 };
        foreach (var lift in lifts)
            content.Add(LiftGroup(lift, slopes.Where(s => s.ServedByLiftId == lift.LiftId)));
        var unmatched = slopes.Where(s => !lifts.Any(l => l.LiftId == s.ServedByLiftId)).ToArray();
        if (unmatched.Length > 0)
        {
            content.Add(Text("Other slopes", 15, bold: true));
            foreach (var slope in unmatched) content.Add(SlopeRow(slope));
        }
        if (lifts.Length == 0 && slopes.Length == 0) content.Add(Text("No slopes or lifts reported.", 14, Muted));
        liftsContent.Content = content;
    }

    private static Border LiftGroup(Lift lift, IEnumerable<Slope> slopes)
    {
        var header = new Grid
        {
            ColumnDefinitions = { new(16), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 12,
            Padding = new Thickness(12, 0),
            HeightRequest = 36,
            AutomationId = $"lift-{lift.LiftId}-header",
            BackgroundColor = Color.FromArgb("#2d3a4e")
        };
        header.Add(Text("🚡", 16));
        var name = Text(lift.Name);
        name.LineBreakMode = LineBreakMode.TailTruncation;
        header.Add(name, 1);
        header.Add(Text($"🕐 {lift.WaitTimeMinutes:0.#} min", 12, Muted), 2);
        header.Add(Text($"👥 {lift.QueueLength}", 12, Muted), 3);
        header.Add(Badge(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lift.Status),
            StatusColor(lift.Status)), 4);
        var group = new VerticalStackLayout { Spacing = 0 };
        group.Add(header);
        foreach (var slope in slopes) group.Add(SlopeRow(slope));
        var border = Box(group, Color.FromArgb("#232e41"), 0, 8);
        border.AutomationId = $"lift-{lift.LiftId}";
        return border;
    }

    private static View SlopeRow(Slope slope)
    {
        var row = new Grid
        {
            ColumnDefinitions = { new(16), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 12,
            Padding = new Thickness(32, 0, 16, 0),
            HeightRequest = 32,
            AutomationId = $"slope-{slope.SlopeId}"
        };
        var marker = new GraphicsView
        {
            Drawable = new DifficultyMarker(slope.Difficulty),
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = $"difficulty-{slope.SlopeId}"
        };
        SemanticProperties.SetDescription(marker, $"{slope.Difficulty} difficulty");
        row.Add(marker);
        var name = Text(slope.Name, 14, Color.FromArgb("#e2e8f0"));
        name.MaximumWidthRequest = 180;
        name.LineBreakMode = LineBreakMode.TailTruncation;
        row.Add(name, 1);
        row.Add(Text($"{slope.SnowDepthCm:0.#} cm", 12, Muted), 2);
        if (slope.Groomed)
            row.Add(Badge("Groomed", Color.FromArgb("#0369a1"), Color.FromArgb("#bae6fd"), 4), 3);
        var status = Badge(slope.IsOpen ? "Open" : "Closed", StatusColor(slope.IsOpen ? "open" : "closed"));
        status.AutomationId = $"slope-{slope.SlopeId}-status";
        row.Add(status, 5);
        return row;
    }

    private static Color StatusColor(string status) => Color.FromArgb(status.ToLowerInvariant() switch
    {
        "open" => "#22c55e",
        "closed" => "#ef4444",
        "maintenance" => "#f59e0b",
        _ => "#64748b"
    });

    private static Border Badge(string text, Color color, Color? foreground = null, double radius = 10)
    {
        var badge = Box(Text(text, 12, foreground), color, 0, radius);
        badge.Padding = new Thickness(8, 1);
        badge.VerticalOptions = LayoutOptions.Center;
        return badge;
    }

    private sealed class RiskBar(double risk, Color color) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            canvas.FillColor = Color.FromArgb("#475569");
            canvas.FillRoundedRectangle(bounds, 8);
            var width = (float)Math.Clamp(risk, 0, 1) * bounds.Width;
            if (width <= 0) return;
            canvas.FillColor = color;
            canvas.FillRoundedRectangle(bounds.X, bounds.Y, width, bounds.Height, Math.Min(8, width / 2));
        }
    }

    private sealed class DifficultyMarker(string difficulty) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var level = difficulty.ToLowerInvariant();
            canvas.FillColor = Color.FromArgb(level switch
            {
                "green" => "#22c55e", "blue" => "#3b82f6", "red" => "#ef4444", "black" => "#000000", _ => "#64748b"
            });
            canvas.FillEllipse(0, 0, 16, 16);
            canvas.FillColor = Colors.White;
            switch (level)
            {
                case "green": canvas.FillEllipse(5, 5, 6, 6); break;
                case "blue": canvas.FillRectangle(5, 5, 6, 6); break;
                case "red": Diamond(canvas, 8, 4); break;
                case "black":
                    canvas.StrokeColor = Colors.White;
                    canvas.StrokeSize = 1;
                    canvas.DrawEllipse(.5f, .5f, 15, 15);
                    Diamond(canvas, 5, 3);
                    Diamond(canvas, 11, 3);
                    break;
                default:
                    canvas.FontColor = Colors.White;
                    canvas.FontSize = 10;
                    canvas.DrawString("?", 0, 0, 16, 16, HorizontalAlignment.Center, VerticalAlignment.Center);
                    break;
            }
        }

        private static void Diamond(ICanvas canvas, float x, float radius)
        {
            var path = new PathF();
            path.MoveTo(x, 8 - radius);
            path.LineTo(x + radius, 8);
            path.LineTo(x, 8 + radius);
            path.LineTo(x - radius, 8);
            path.Close();
            canvas.FillPath(path);
        }
    }
}
