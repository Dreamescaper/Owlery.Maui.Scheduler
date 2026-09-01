namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// A page of the agenda: one continuous list of rows covering a range of days.
/// </summary>
/// <remarks>
/// The surface that does not page. A page is still identified by the first of a month, so
/// <c>DisplayDate</c> round-trips and a host's next/previous buttons keep working, but the range it
/// renders reaches either side of that month and only the middle ring-buffer slot is ever filled.
/// <para>
/// It is also the only stateful surface. The row table is built once per change of data or geometry
/// and then sliced per scroll, because rebuilding it on every scroll frame would defeat the point of
/// virtualizing at all. Anything that invalidates the data must say so through
/// <see cref="Invalidate"/>.
/// </para>
/// </remarks>
internal sealed class AgendaSurface(AgendaGeometry geometry) : ISchedulerSurface
{
    private AgendaPageLayout? table;
    private DateOnly tablePage;
    private bool dirty = true;

    private readonly List<AgendaSectionPlacement> windowSections = [];

    /// <summary>Rows realized by the last <see cref="Layout"/>, in the order they were returned.</summary>
    private readonly List<AgendaRow> windowRows = [];

    /// <summary>The whole table, for the drawable and for hit-testing.</summary>
    public IReadOnlyList<AgendaRow> Rows => table?.Rows ?? [];

    /// <summary>Marks the table stale. The next <see cref="Layout"/> rebuilds it.</summary>
    public void Invalidate() => dirty = true;

    public DateOnly StartOfPage(DateOnly date) => new(date.Year, date.Month, 1);

    public DateOnly NextPage(DateOnly pageStart) => pageStart.AddMonths(1);

    public DateOnly PreviousPage(DateOnly pageStart) => pageStart.AddMonths(-1);

    /// <summary>
    /// Every date the agenda covers, which reaches well past the month naming the page.
    /// </summary>
    /// <remarks>
    /// The month surface already returns dates outside its own page for the same reason — what the
    /// host is asked to have loaded is what is reachable, not what the page is called.
    /// </remarks>
    public IReadOnlyList<DateOnly> DatesOn(DateOnly pageStart)
    {
        var start = geometry.RangeStart(pageStart);
        var end = geometry.RangeEnd(pageStart);
        var dates = new List<DateOnly>(end.DayNumber - start.DayNumber + 1);

        for (var date = start; date <= end; date = date.AddDays(1))
            dates.Add(date);

        return dates;
    }

    public bool PageContains(DateOnly pageStart, DateOnly date) =>
        date >= geometry.RangeStart(pageStart) && date <= geometry.RangeEnd(pageStart);

    /// <summary>
    /// The rows worth realizing right now — those within a screen's reach of the scroll position.
    /// </summary>
    /// <remarks>
    /// Returning a slice rather than everything is the whole of the virtualization.
    /// <c>PopulateSlot</c> reconciles whatever comes back against the views already on the page, so a
    /// row that stays in the window keeps its view untouched, one that leaves goes back to the pool
    /// and one that enters takes a view from it — none of which needed new code to arrange.
    /// </remarks>
    public IReadOnlyList<IAppointmentPlacement> Layout(IEnumerable<ISchedulerAppointment> items, DateOnly pageStart)
    {
        if (dirty || table is null || tablePage != pageStart)
        {
            table = AgendaLayoutEngine.Layout(
                items,
                geometry.RangeStart(pageStart),
                geometry.RangeEnd(pageStart),
                geometry.FirstDayOfWeek,
                geometry.EstimatedRowHeight,
                geometry.MonthSectionHeight,
                geometry.WeekSectionHeight);

            tablePage = pageStart;
            dirty = false;
            geometry.SetContentHeight(table.ContentHeight);
        }

        return Slice();
    }

    /// <summary>Re-slices the current table without rebuilding it, after a scroll or a correction.</summary>
    public IReadOnlyList<IAppointmentPlacement> Slice()
    {
        var rows = Rows;
        var placements = new List<IAppointmentPlacement>();

        windowSections.Clear();
        windowRows.Clear();

        if (rows.Count == 0)
            return placements;

        var top = geometry.WindowTop;
        var bottom = geometry.WindowBottom;

        for (var i = AgendaLayoutEngine.IndexAt(rows, top); i < rows.Count && rows[i].Top < bottom; i++)
        {
            var row = rows[i];
            windowRows.Add(row);

            switch (row.Kind)
            {
                case AgendaRowKind.MonthSection:
                    windowSections.Add(new AgendaSectionPlacement(
                        new SchedulerAgendaSection(ToDateTime(row.Date), SchedulerAgendaSectionKind.Month, 0),
                        geometry.MonthBounds(row)));
                    break;

                case AgendaRowKind.WeekSection:
                    windowSections.Add(new AgendaSectionPlacement(
                        new SchedulerAgendaSection(ToDateTime(row.Date), SchedulerAgendaSectionKind.Week, 0),
                        geometry.WeekBounds(row)));
                    break;

                case AgendaRowKind.Appointment:
                    placements.Add(new AgendaPlacement(row.Appointment!, row));

                    // The day marker is not a row of its own — it hangs in the gutter beside the
                    // first appointment of its day, so an extra day costs nothing but its content.
                    if (row.StartsDay)
                    {
                        windowSections.Add(new AgendaSectionPlacement(
                            new SchedulerAgendaSection(
                                ToDateTime(row.Date), SchedulerAgendaSectionKind.Day, row.DayCount),
                            geometry.DayMarkerBounds(row)));
                    }

                    break;
            }
        }

        return placements;
    }

    /// <summary>
    /// Records what a row actually measured, correcting everything below it.
    /// </summary>
    /// <returns>
    /// How much content above <see cref="AgendaGeometry.VisibleTop"/> grew or shrank, which the
    /// control has to absorb into the scroll offset if the view is not to jump under the reader.
    /// </returns>
    public double ApplyMeasuredHeights(IReadOnlyList<(AgendaRow Row, double Height)> measured)
    {
        var rows = Rows;
        var first = int.MaxValue;
        var shiftAbove = 0.0;

        foreach (var (row, height) in measured)
        {
            if (row.Measured && Math.Abs(row.Height - height) < 0.5)
                continue;

            // A row entirely above the fold changing height moves everything the reader can see.
            if (row.Bottom <= geometry.VisibleTop)
                shiftAbove += height - row.Height;

            row.Height = height;
            row.Measured = true;
            first = Math.Min(first, row.Index);
        }

        if (first == int.MaxValue)
            return 0;

        geometry.SetContentHeight(AgendaLayoutEngine.Reflow(rows, first));

        return shiftAbove;
    }

    public Rect BoundsFor(IAppointmentPlacement placement) => geometry.RowBounds(((AgendaPlacement)placement).Row);

    /// <summary>
    /// Nothing. An agenda lists what exists rather than offering the empty space between things.
    /// </summary>
    public Rect BoundsFor(SchedulerTimeSlot slot, DateOnly pageStart) => Rect.Zero;

    /// <summary>
    /// The day a position falls in, reported as a whole day.
    /// </summary>
    /// <remarks>
    /// A day rather than a span of minutes, the same shape the month reports, because a list has no
    /// time axis to resolve a finer answer against. A tap landing on a heading selects the day that
    /// heading introduces.
    /// </remarks>
    public SchedulerTimeSlot? SlotAt(Point point, PageSlot[] pages)
    {
        var rows = Rows;

        if (rows.Count == 0)
            return null;

        var index = AgendaLayoutEngine.IndexAt(rows, point.Y);

        if (index >= rows.Count)
            return null;

        var date = rows[index].Date;

        return new SchedulerTimeSlot(ToDateTime(date), TimeSpan.FromDays(1));
    }

    /// <summary>Nothing is hidden: a row is either in the list or outside the range.</summary>
    public IReadOnlyList<int> OverflowFor(DateOnly pageStart) => [];

    /// <summary>
    /// The headings within the window, from the last <see cref="Layout"/> or <see cref="Slice"/>.
    /// </summary>
    /// <remarks>
    /// Answered from that pass rather than recomputed, exactly as <see cref="OverflowFor"/> is on the
    /// surfaces that use it, so it has to be read straight after slicing.
    /// </remarks>
    public IReadOnlyList<AgendaSectionPlacement> SectionsFor(DateOnly pageStart) =>
        tablePage == pageStart ? windowSections : [];

    /// <summary>The rows realized by the last slice, so the control can measure what it just placed.</summary>
    public IReadOnlyList<AgendaRow> WindowRows => windowRows;

    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}
