namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// The month surface: a page is one calendar month, laid out on a fixed six-by-seven grid of days.
/// </summary>
/// <remarks>
/// A page is identified by the first of its month rather than by the first cell of its grid, so
/// stepping between pages is plain month arithmetic and "does this day belong to another month" is a
/// comparison against the page's own identity. See DESIGN.md section 18.
/// </remarks>
internal sealed class MonthSurface(MonthGeometry geometry) : ISchedulerSurface
{
    private DateOnly cachedPage;
    private IReadOnlyList<int> cachedOverflow = [];

    public DateOnly StartOfPage(DateOnly date) => new(date.Year, date.Month, 1);

    public DateOnly NextPage(DateOnly pageStart) => pageStart.AddMonths(1);

    public DateOnly PreviousPage(DateOnly pageStart) => pageStart.AddMonths(-1);

    public IReadOnlyList<DateOnly> DatesOn(DateOnly pageStart)
    {
        var gridStart = geometry.GridStart(pageStart);
        return [.. Enumerable.Range(0, MonthGeometry.CellCount).Select(gridStart.AddDays)];
    }

    public bool PageContains(DateOnly pageStart, DateOnly date) => geometry.CellIndexOf(pageStart, date) >= 0;

    public IReadOnlyList<IAppointmentPlacement> Layout(IEnumerable<ISchedulerAppointment> items, DateOnly pageStart)
    {
        var layout = MonthLayoutEngine.Layout(items, geometry.GridStart(pageStart), geometry.LinesPerCell);

        cachedPage = pageStart;
        cachedOverflow = layout.OverflowByCell;

        return layout.Placements;
    }

    public IReadOnlyList<int> OverflowFor(DateOnly pageStart) => cachedPage == pageStart ? cachedOverflow : [];

    public Rect BoundsFor(IAppointmentPlacement placement)
    {
        var chip = (MonthPlacement)placement;
        var cell = geometry.CellBounds(chip.CellIndex);

        return new Rect(
            cell.X + MonthGeometry.ChipInset,
            cell.Y + MonthGeometry.DayNumberHeight
                + chip.Line * (MonthGeometry.ChipHeight + MonthGeometry.ChipGap),
            Math.Max(1, cell.Width - MonthGeometry.ChipInset * 2),
            MonthGeometry.ChipHeight);
    }

    /// <summary>A selected day is marked across its whole cell — there is no finer target in a month.</summary>
    public Rect BoundsFor(SchedulerTimeSlot slot, DateOnly pageStart)
    {
        var cellIndex = geometry.CellIndexOf(pageStart, DateOnly.FromDateTime(slot.Start));

        return cellIndex < 0 ? Rect.Zero : geometry.CellBounds(cellIndex);
    }

    /// <summary>
    /// A touch selects a whole day, expressed as a slot a day long.
    /// </summary>
    /// <remarks>
    /// A month cell has no time axis, so there is nothing finer to resolve to. The duration is what
    /// tells a host which it has been handed: a timeline reports <c>SnapMinutes</c>, a month reports
    /// a day.
    /// </remarks>
    public SchedulerTimeSlot? SlotAt(Point point, PageSlot[] pages)
    {
        if (geometry.ViewportWidth <= 0 || geometry.ContentHeight <= 0)
            return null;

        var slotIndex = Math.Clamp((int)(point.X / geometry.ViewportWidth), 0, PageGeometry.SlotCount - 1);
        var xInSlot = point.X - slotIndex * geometry.ViewportWidth;

        var column = Math.Clamp((int)(xInSlot / geometry.CellWidth), 0, MonthGeometry.Columns - 1);
        var row = Math.Clamp((int)(point.Y / geometry.CellHeight), 0, MonthGeometry.Rows - 1);

        var date = geometry.GridStart(pages[slotIndex].PageStart).AddDays(row * MonthGeometry.Columns + column);

        return new SchedulerTimeSlot(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromDays(1));
    }
}
