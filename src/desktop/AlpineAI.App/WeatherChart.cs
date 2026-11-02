using AlpineAI.Core;
using Microsoft.Maui.Graphics;

namespace AlpineAI.App;

internal sealed class WeatherChart(Weather weather) : IDrawable
{
    public void Draw(ICanvas canvas, RectF bounds)
    {
        var values = new[] { weather.Temperature, weather.WindSpeed, weather.SnowIntensity, weather.Visibility / 10 };
        var labels = new[] { "Temp (°C)", "Wind (km/h)", "Snow (cm/h)", "Visibility (m)" };
        var colors = new[] { "#60a5fa", "#34d399", "#c084fc", "#fbbf24" };
        var minimum = Math.Min(0, values.Min());
        var maximum = Math.Max(1, values.Max());
        var height = bounds.Height - 42;
        var range = maximum - minimum;
        var zero = 16 + (float)(maximum / range) * height;
        var slot = bounds.Width / 4;
        canvas.StrokeColor = Color.FromArgb("#64748b");
        canvas.DrawLine(0, zero, bounds.Width, zero);
        canvas.FontSize = 11;
        for (var i = 0; i < values.Length; i++)
        {
            var y = 16 + (float)((maximum - values[i]) / range) * height;
            canvas.FillColor = Color.FromArgb(colors[i]);
            canvas.FillRoundedRectangle(i * slot + slot * .25f, Math.Min(y, zero), slot * .5f, Math.Max(1, Math.Abs(zero - y)), 3);
            canvas.FontColor = Color.FromArgb("#94a3b8");
            canvas.DrawString(labels[i], i * slot, bounds.Height - 20, slot, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
