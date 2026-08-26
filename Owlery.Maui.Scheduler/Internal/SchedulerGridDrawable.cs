namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Draws the entire scrollable background — every day column, hour line and shading for all three
/// rendered weeks — onto one canvas. See DESIGN.md for why this is not a grid of cell views.
/// </summary>
internal sealed class SchedulerGridDrawable(SchedulerGeometry geometry) : IDrawable
{
    public Color HourLineColor { get; set; } = Color.FromArgb("#E0E0E0");

    public Color HalfHourLineColor { get; set; } = Color.FromArgb("#F0F0F0");

    public Color DaySeparatorColor { get; set; } = Color.FromArgb("#E0E0E0");

    public Color WeekendBackgroundColor { get; set; } = Color.FromArgb("#FAFAFA");

    public Color TodayBackgroundColor { get; set; } = Color.FromArgb("#F3E8FC");

    public Color CurrentTimeColor { get; set; } = Color.FromArgb("#FD4225");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (geometry.ViewportWidth <= 0 || geometry.ContentHeight <= 0)
            return;

        if (geometry.AnimationOffsetX != 0)
        {
            canvas.SaveState();
            canvas.Translate((float)geometry.AnimationOffsetX, 0);
        }

        var dayWidth = (float)geometry.DayWidth;
        var height = (float)geometry.ContentHeight;
        var today = DateOnly.FromDateTime(geometry.Now);

        DrawDayBackgrounds(canvas, dayWidth, height, today);
        DrawHourLines(canvas);
        DrawDaySeparators(canvas, dayWidth, height);
        DrawCurrentTimeLine(canvas, today);

        if (geometry.AnimationOffsetX != 0)
            canvas.RestoreState();
    }

    private void DrawDayBackgrounds(ICanvas canvas, float dayWidth, float height, DateOnly today)
    {
        for (var slot = 0; slot < SchedulerGeometry.SlotCount; slot++)
        {
            var slotOffset = (float)(slot * geometry.PageSpan);

            for (var day = 0; day < geometry.VisibleDays; day++)
            {
                var date = geometry.SlotStarts[slot].AddDays(day);

                var fill = date == today
                    ? TodayBackgroundColor
                    : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                        ? WeekendBackgroundColor
                        : null;

                if (fill is null)
                    continue;

                canvas.FillColor = fill;
                canvas.FillRectangle(slotOffset + day * dayWidth, 0, dayWidth, height);
            }
        }
    }

    private void DrawHourLines(ICanvas canvas)
    {
        var width = (float)geometry.SurfaceWidth;
        canvas.StrokeSize = 1;

        for (var hour = geometry.StartHour; hour <= geometry.EndHour; hour++)
        {
            var y = (float)geometry.YFromMinutes(hour * 60.0);

            canvas.StrokeColor = HourLineColor;
            canvas.DrawLine(0, y, width, y);

            if (hour < geometry.EndHour)
            {
                var halfY = (float)geometry.YFromMinutes(hour * 60.0 + 30);
                canvas.StrokeColor = HalfHourLineColor;
                canvas.DrawLine(0, halfY, width, halfY);
            }
        }
    }

    private void DrawDaySeparators(ICanvas canvas, float dayWidth, float height)
    {
        canvas.StrokeColor = DaySeparatorColor;
        canvas.StrokeSize = 1;

        for (var slot = 0; slot < SchedulerGeometry.SlotCount; slot++)
        {
            var slotOffset = (float)(slot * geometry.PageSpan);

            for (var column = 0; column <= geometry.VisibleDays; column++)
                canvas.DrawLine(slotOffset + column * dayWidth, 0, slotOffset + column * dayWidth, height);
        }
    }

    private void DrawCurrentTimeLine(ICanvas canvas, DateOnly today)
    {
        var minutes = geometry.Now.TimeOfDay.TotalMinutes;

        if (minutes < geometry.WindowStartMinutes || minutes > geometry.WindowEndMinutes)
            return;

        var y = (float)geometry.YFromMinutes(minutes);
        var dayWidth = (float)geometry.DayWidth;

        for (var slot = 0; slot < SchedulerGeometry.SlotCount; slot++)
        {
            var dayIndex = today.DayNumber - geometry.SlotStarts[slot].DayNumber;
            if (dayIndex < 0 || dayIndex >= geometry.VisibleDays)
                continue;

            var slotOffset = (float)(slot * geometry.PageSpan);

            canvas.StrokeColor = CurrentTimeColor;
            canvas.StrokeSize = 2;
            canvas.DrawLine(slotOffset, y, slotOffset + geometry.VisibleDays * dayWidth, y);

            canvas.FillColor = CurrentTimeColor;
            canvas.FillCircle(slotOffset + dayIndex * dayWidth + 4, y, 4);
        }
    }
}
