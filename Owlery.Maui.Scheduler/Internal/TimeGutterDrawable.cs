using Microsoft.Maui.Graphics;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Draws the hour labels in the fixed left gutter, and the time indicator shown while dragging.
/// </summary>
/// <remarks>
/// Labels are centred on the line they name, matching the indicator, so a time read from the gutter
/// lines up with the grid line it belongs to.
/// </remarks>
internal sealed class TimeGutterDrawable(SchedulerGeometry geometry) : IDrawable
{
    public Color TextColor { get; set; } = Color.FromArgb("#6E6E6E");

    public float FontSize { get; set; } = 11;

    public string HourFormat { get; set; } = "HH:mm";

    public double Width { get; set; } = 52;

    public Color HighlightBackgroundColor { get; set; } = Color.FromArgb("#212121");

    public Color HighlightTextColor { get; set; } = Colors.White;

    /// <summary>Minute of day the drag indicator points at, or <c>null</c> when nothing is being dragged.</summary>
    public double? HighlightMinutes { get; set; }

    /// <summary>Text of the drag indicator — the time an appointment would take if released now.</summary>
    public string? HighlightText { get; set; }

    private const float HighlightHeight = 18;

    /// <summary>
    /// How much of an hour label has to be covered before it is worth hiding.
    /// </summary>
    /// <remarks>
    /// A fraction rather than any-overlap-at-all, because the two boxes graze each other at half past
    /// the hour: the indicator clips a single pixel off the bottom of the label, which nobody can see
    /// and which is no reason to drop an entry out of the gutter. Expressed as a share of the label so
    /// it holds at any hour height.
    /// </remarks>
    private const float ObscuredFraction = 1f / 3f;

    /// <summary>Whether the drag indicator covers enough of an hour label to be worth hiding it.</summary>
    internal bool IsHourObscured(int hour, float? highlightY)
    {
        if (highlightY is not { } indicatorY)
            return false;

        // Both the label and the indicator are centred on their own line, so this comparison is
        // symmetrical: a quarter hour either side of an hour affects its label the same way.
        var centre = (float)geometry.YFromMinutes(hour * 60.0);
        var labelTop = centre - LabelHeight / 2;
        var labelBottom = centre + LabelHeight / 2;

        var covered = Math.Min(labelBottom, indicatorY + HighlightHeight / 2)
            - Math.Max(labelTop, indicatorY - HighlightHeight / 2);

        return covered > LabelHeight * ObscuredFraction;
    }

    /// <summary>Test seam: whether the hour label is hidden by the indicator as currently set.</summary>
    internal bool IsHourLabelHidden(int hour) =>
        IsHourObscured(hour, HighlightMinutes is { } minutes ? (float)geometry.YFromMinutes(minutes) : null);

    /// <summary>
    /// The hours that get a label. The first and last lines sit on the very edges of the surface,
    /// where a label centred on them would be cut in half, so they are left out.
    /// </summary>
    internal IEnumerable<int> LabelledHours() =>
        Enumerable.Range(geometry.StartHour + 1, Math.Max(0, geometry.EndHour - geometry.StartHour - 1));

    private float LabelHeight => FontSize + 4;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (geometry.ContentHeight <= 0)
            return;

        var highlightY = HighlightMinutes is { } minutes ? (float)geometry.YFromMinutes(minutes) : (float?)null;

        canvas.FontSize = FontSize;

        foreach (var hour in LabelledHours())
        {
            // An hour label covered enough by the indicator to be unreadable gives way to it.
            if (IsHourObscured(hour, highlightY))
                continue;

            var y = (float)geometry.YFromMinutes(hour * 60.0);

            canvas.FontColor = TextColor;
            canvas.DrawString(
                DateTime.Today.AddHours(hour).ToString(HourFormat),
                0,
                y - LabelHeight / 2,
                (float)Width - 8,
                LabelHeight,
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
        }

        if (highlightY is not { } indicatorY || string.IsNullOrEmpty(HighlightText))
            return;

        // Centred on the line the appointment would start at, and in the gutter rather than over the
        // grid: above the appointment it sat directly under the finger that was dragging it.
        var chip = new RectF(2, indicatorY - HighlightHeight / 2, (float)Width - 4, HighlightHeight);

        canvas.FillColor = HighlightBackgroundColor;
        canvas.FillRoundedRectangle(chip, 4);

        canvas.FontColor = HighlightTextColor;
        canvas.DrawString(
            HighlightText,
            chip.X,
            chip.Y,
            chip.Width,
            chip.Height,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);
    }
}
