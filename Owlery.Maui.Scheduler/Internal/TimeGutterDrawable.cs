using Microsoft.Maui.Graphics;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>Draws the hour labels in the fixed left gutter.</summary>
internal sealed class TimeGutterDrawable(SchedulerGeometry geometry) : IDrawable
{
    public Color TextColor { get; set; } = Color.FromArgb("#6E6E6E");

    public float FontSize { get; set; } = 11;

    public string HourFormat { get; set; } = "HH:mm";

    public double Width { get; set; } = 52;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (geometry.ContentHeight <= 0)
            return;

        canvas.FontColor = TextColor;
        canvas.FontSize = FontSize;

        // The last hour label would be clipped by the bottom edge, so it is skipped like most calendars do.
        for (var hour = geometry.StartHour; hour < geometry.EndHour; hour++)
        {
            var y = (float)geometry.YFromMinutes(hour * 60.0);
            var text = DateTime.Today.AddHours(hour).ToString(HourFormat);

            canvas.DrawString(
                text,
                0,
                y + 2,
                (float)Width - 8,
                FontSize + 4,
                HorizontalAlignment.Right,
                VerticalAlignment.Top);
        }
    }
}
