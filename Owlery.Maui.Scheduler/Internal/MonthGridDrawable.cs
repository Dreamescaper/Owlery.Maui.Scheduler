using System.Globalization;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Draws every month page — cell shading, day numbers, grid lines and the "+N more" markers — onto
/// one canvas, the way <see cref="SchedulerGridDrawable"/> does for the timeline.
/// </summary>
/// <remarks>
/// Day numbers are painted rather than being labels. There are 42 to a page and three pages rendered
/// at all times, so the alternative is 126 views that never do anything but show a number. Chips stay
/// as views because they carry the host's template.
/// <para>
/// Pages are read from the slots directly rather than from <see cref="PageGeometry.SlotStarts"/>,
/// because the overflow counts live on the slots and the two have to describe the same page.
/// </para>
/// </remarks>
internal sealed class MonthGridDrawable(MonthGeometry geometry, PageSlot[] slots) : IDrawable
{
    public Color GridLineColor { get; set; } = null!;

    public Color NonWorkingDaysBackgroundColor { get; set; } = null!;

    /// <summary>Fill for the leading and trailing days that belong to the neighbouring months.</summary>
    public Color AdjacentMonthBackgroundColor { get; set; } = null!;

    public Color CurrentDayBackgroundColor { get; set; } = null!;

    public Color DayNumberColor { get; set; } = null!;

    public Color AdjacentMonthDayNumberColor { get; set; } = null!;

    public Color CurrentDayTextColor { get; set; } = null!;

    public Color OverflowTextColor { get; set; } = null!;

    public bool ShowNonWorkingDaysShading { get; set; }

    public bool ShowCurrentDayHighlight { get; set; }

    public IReadOnlyCollection<DayOfWeek> WorkingDays { get; set; } = [];

    /// <summary>Composed with the number a cell could not show, e.g. <c>"+3 more"</c>.</summary>
    public string OverflowFormat { get; set; } = "+{0} more";

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (geometry.ViewportWidth <= 0 || geometry.ContentHeight <= 0)
            return;

        var today = DateOnly.FromDateTime(geometry.Now);

        for (var slot = 0; slot < slots.Length; slot++)
        {
            var offset = (float)(slot * geometry.PageSpan);
            var page = slots[slot].PageStart;

            DrawCells(canvas, offset, page, today, slots[slot].Overflow);
            DrawGridLines(canvas, offset);
        }
    }

    private void DrawCells(ICanvas canvas, float offset, DateOnly page, DateOnly today, IReadOnlyList<int> overflow)
    {
        var culture = CultureInfo.CurrentUICulture;
        var gridStart = geometry.GridStart(page);

        for (var index = 0; index < MonthGeometry.CellCount; index++)
        {
            var date = gridStart.AddDays(index);
            var cell = geometry.CellBounds(index);
            var x = (float)cell.X + offset;
            var y = (float)cell.Y;

            var isToday = ShowCurrentDayHighlight && date == today;
            var isAdjacent = date.Month != page.Month || date.Year != page.Year;

            var fill = isToday
                ? CurrentDayBackgroundColor
                : isAdjacent
                    ? AdjacentMonthBackgroundColor
                    : ShowNonWorkingDaysShading && !SchedulerWorkingTime.IsWorkingDay(date, WorkingDays)
                        ? NonWorkingDaysBackgroundColor
                        : null;

            if (fill is not null)
            {
                canvas.FillColor = fill;
                canvas.FillRectangle(x, y, (float)cell.Width, (float)cell.Height);
            }

            canvas.FontColor = isToday ? CurrentDayTextColor : isAdjacent ? AdjacentMonthDayNumberColor : DayNumberColor;
            canvas.FontSize = 12;
            canvas.Font = isToday ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;

            canvas.DrawString(
                date.Day.ToString(culture),
                x,
                y,
                (float)cell.Width,
                (float)MonthGeometry.DayNumberHeight,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);

            DrawOverflow(canvas, x, y, cell.Width, index < overflow.Count ? overflow[index] : 0, culture);
        }
    }

    private void DrawOverflow(ICanvas canvas, float x, float y, double width, int hidden, CultureInfo culture)
    {
        // The marker takes the cell's last line, so there has to be a line for it to take.
        if (hidden <= 0 || geometry.LinesPerCell <= 0)
            return;

        var lineY = y + (float)(MonthGeometry.DayNumberHeight
            + (geometry.LinesPerCell - 1) * (MonthGeometry.ChipHeight + MonthGeometry.ChipGap));

        canvas.FontColor = OverflowTextColor;
        canvas.FontSize = 10;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;

        canvas.DrawString(
            string.Format(culture, OverflowFormat, hidden),
            x + (float)MonthGeometry.ChipInset,
            lineY,
            (float)(width - MonthGeometry.ChipInset * 2),
            (float)MonthGeometry.ChipHeight,
            HorizontalAlignment.Left,
            VerticalAlignment.Center);
    }

    private void DrawGridLines(ICanvas canvas, float offset)
    {
        var height = (float)geometry.ContentHeight;
        var width = (float)geometry.ViewportWidth;

        canvas.StrokeColor = GridLineColor;
        canvas.StrokeSize = 1;

        for (var column = 0; column <= MonthGeometry.Columns; column++)
        {
            var x = offset + (float)(column * geometry.CellWidth);
            canvas.DrawLine(x, 0, x, height);
        }

        for (var row = 0; row <= MonthGeometry.Rows; row++)
        {
            var y = (float)(row * geometry.CellHeight);
            canvas.DrawLine(offset, y, offset + width, y);
        }
    }
}
