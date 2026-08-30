namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Draws the entire scrollable background — every day column, hour line and shading for all three
/// rendered weeks — onto one canvas. See DESIGN.md for why this is not a grid of cell views.
/// </summary>
internal sealed class SchedulerGridDrawable(SchedulerGeometry geometry) : IDrawable
{
    public Color GridLineColor { get; set; } = null!;

    public Color MinorGridLineColor { get; set; } = null!;

    public Color NonWorkingDaysBackgroundColor { get; set; } = null!;

    public Color NonWorkingHoursBackgroundColor { get; set; } = null!;

    public Color CurrentDayBackgroundColor { get; set; } = null!;

    public Color CurrentTimeIndicatorColor { get; set; } = null!;

    public bool ShowNonWorkingDaysShading { get; set; }

    public bool ShowCurrentDayHighlight { get; set; }

    public bool ShowNonWorkingHoursShading { get; set; }

    public IReadOnlyCollection<DayOfWeek> WorkingDays { get; set; } = [];

    public TimeOnly WorkingHoursStart { get; set; }

    public TimeOnly WorkingHoursEnd { get; set; }

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
        DrawNonWorkingHours(canvas, dayWidth);
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

                var fill = ShowCurrentDayHighlight && date == today
                    ? CurrentDayBackgroundColor
                    : ShowNonWorkingDaysShading && !SchedulerWorkingTime.IsWorkingDay(date, WorkingDays)
                        ? NonWorkingDaysBackgroundColor
                        : null;

                if (fill is null)
                    continue;

                canvas.FillColor = fill;
                canvas.FillRectangle(slotOffset + day * dayWidth, 0, dayWidth, height);
            }
        }
    }

    private void DrawNonWorkingHours(ICanvas canvas, float dayWidth)
    {
        if (!ShowNonWorkingHoursShading
            || !SchedulerWorkingTime.HasSameDayInterval(WorkingHoursStart, WorkingHoursEnd))
        {
            return;
        }

        var workingStart = SchedulerWorkingTime.Minutes(WorkingHoursStart);
        var workingEnd = SchedulerWorkingTime.Minutes(WorkingHoursEnd);

        for (var slot = 0; slot < SchedulerGeometry.SlotCount; slot++)
        {
            var slotOffset = (float)(slot * geometry.PageSpan);

            for (var day = 0; day < geometry.VisibleDays; day++)
            {
                var date = geometry.SlotStarts[slot].AddDays(day);
                if (!SchedulerWorkingTime.IsWorkingDay(date, WorkingDays))
                    continue;

                var x = slotOffset + day * dayWidth;

                FillMinuteRange(canvas, x, dayWidth, geometry.WindowStartMinutes, workingStart);
                FillMinuteRange(canvas, x, dayWidth, workingEnd, geometry.WindowEndMinutes);
            }
        }
    }

    private void FillMinuteRange(ICanvas canvas, float x, float width, double start, double end)
    {
        var clippedStart = Math.Clamp(start, geometry.WindowStartMinutes, geometry.WindowEndMinutes);
        var clippedEnd = Math.Clamp(end, geometry.WindowStartMinutes, geometry.WindowEndMinutes);
        if (clippedEnd <= clippedStart)
            return;

        var y = (float)geometry.YFromMinutes(clippedStart);
        var height = (float)(geometry.YFromMinutes(clippedEnd) - y);

        canvas.FillColor = NonWorkingHoursBackgroundColor;
        canvas.FillRectangle(x, y, width, height);
    }

    private void DrawHourLines(ICanvas canvas)
    {
        var width = (float)geometry.SurfaceWidth;
        canvas.StrokeSize = 1;

        for (var hour = geometry.StartHour; hour <= geometry.EndHour; hour++)
        {
            var y = (float)geometry.YFromMinutes(hour * 60.0);

            canvas.StrokeColor = GridLineColor;
            canvas.DrawLine(0, y, width, y);

            if (hour < geometry.EndHour)
            {
                var halfY = (float)geometry.YFromMinutes(hour * 60.0 + 30);
                canvas.StrokeColor = MinorGridLineColor;
                canvas.DrawLine(0, halfY, width, halfY);
            }
        }
    }

    private void DrawDaySeparators(ICanvas canvas, float dayWidth, float height)
    {
        canvas.StrokeColor = GridLineColor;
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

            canvas.StrokeColor = CurrentTimeIndicatorColor;
            canvas.StrokeSize = 2;
            canvas.DrawLine(slotOffset, y, slotOffset + geometry.VisibleDays * dayWidth, y);

            canvas.FillColor = CurrentTimeIndicatorColor;
            canvas.FillCircle(slotOffset + dayIndex * dayWidth + 4, y, 4);
        }
    }
}
