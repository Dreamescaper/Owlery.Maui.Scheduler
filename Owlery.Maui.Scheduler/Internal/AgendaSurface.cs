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
    private readonly Dictionary<object, double> measuredHeights = [];
    private DateOnly loadedPage;
    private DateOnly loadedStart;
    private DateOnly loadedEnd;

    /// <summary>The range the host has been asked for, which runs ahead of the one laid out.</summary>
    /// <remarks>
    /// Growing is a request, not a fact. Laying out a month before the host has answered it puts a
    /// range on screen whose data does not exist yet, and the rules that describe a range then
    /// describe the wrong one: a week straddling the new edge becomes eligible for a heading while
    /// its earlier days are still empty, so the heading lands under the wrong month for one frame and
    /// moves when the data arrives. Keeping the two apart means the laid-out range and the data in it
    /// always agree.
    /// </remarks>
    private DateOnly requestedStart;
    private DateOnly requestedEnd;
    private bool rangeRequestOutstanding;
    private bool rangeInitialised;
    private double pendingContentShift;

    /// <summary>Known appointment heights supplied by the host, or null to measure realized views.</summary>
    internal Func<ISchedulerAppointment, double>? RowHeightResolver { get; set; }

    private readonly List<AgendaSectionPlacement> windowSections = [];
    private readonly List<IAppointmentPlacement> windowPlacements = [];

    /// <summary>Rows realized by the last <see cref="Layout"/>, in the order they were returned.</summary>
    private readonly List<AgendaRow> windowRows = [];

    /// <summary>The whole table, for the drawable and for hit-testing.</summary>
    public IReadOnlyList<AgendaRow> Rows => table?.Rows ?? [];

    /// <summary>Marks the table stale. The next <see cref="Layout"/> rebuilds it.</summary>
    /// <param name="clearMeasurements">
    /// Whether cached row measurements are no longer valid, as after a width or template change.
    /// Ordinary data refreshes keep them: identity is the appointment key, so rebuilding a table
    /// must not make scrolling back over a row pay to measure it again.
    /// </param>
    public void Invalidate(bool clearMeasurements = false)
    {
        dirty = true;

        if (clearMeasurements)
            measuredHeights.Clear();
    }

    /// <summary>
    /// Drops the row table and the window over it, for when there is nothing left to lay out.
    /// </summary>
    /// <remarks>
    /// <see cref="SectionsFor"/> answers from the last <see cref="Slice"/>, the way
    /// <see cref="OverflowFor"/> does on the surfaces that use it. That is only safe while every
    /// populate pass slices; a pass that skips the layout — no items, no template — would otherwise
    /// leave the previous window's headings on screen after every appointment row had been released.
    /// The loaded range is deliberately kept, so items returning do not cost the reader the range
    /// they had grown.
    /// </remarks>
    public void ClearWindow()
    {
        table = null;
        dirty = true;
        pendingContentShift = 0;

        windowPlacements.Clear();
        windowSections.Clear();
        windowRows.Clear();

        geometry.SetContentHeight(0);
    }

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
        var (start, end) = RangeFor(pageStart);
        var dates = new List<DateOnly>(end.DayNumber - start.DayNumber + 1);

        for (var date = start; date <= end; date = date.AddDays(1))
            dates.Add(date);

        return dates;
    }

    public bool PageContains(DateOnly pageStart, DateOnly date)
    {
        var (start, end) = RangeFor(pageStart);

        return date >= start && date <= end;
    }

    /// <summary>Asks for one more whole month after the current end. Nothing is laid out yet.</summary>
    public void GrowForward(DateOnly pageStart)
    {
        EnsureRange(pageStart);

        // A host that never answers must not wedge the edge: take what was last asked for before
        // asking for more, so a second attempt still moves.
        AdoptRequestedRange();

        requestedEnd = AgendaGeometry.LastOfMonth(requestedEnd.AddMonths(1));
        rangeRequestOutstanding = true;
    }

    /// <summary>Asks for one more whole month before the current start. Nothing is laid out yet.</summary>
    public void GrowBackward(DateOnly pageStart)
    {
        EnsureRange(pageStart);
        AdoptRequestedRange();

        requestedStart = AgendaGeometry.FirstOfMonth(requestedStart.AddMonths(-1));
        rangeRequestOutstanding = true;
    }

    /// <summary>
    /// Brings the laid-out range up to what the host was last asked for.
    /// </summary>
    /// <remarks>
    /// Called when items arrive, which is the host answering. Until then the agenda keeps rendering
    /// the range it has data for, so growing costs no layout at all and the reader sees nothing move
    /// until there is something to show them.
    /// </remarks>
    public void AdoptRequestedRange()
    {
        if (!rangeRequestOutstanding)
            return;

        rangeRequestOutstanding = false;
        loadedStart = requestedStart;
        loadedEnd = requestedEnd;
        dirty = true;
    }

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
            var anchor = tablePage == pageStart ? CaptureAnchor() : null;
            EnsureRange(pageStart);

            table = AgendaLayoutEngine.Layout(
                items,
                loadedStart,
                loadedEnd,
                geometry.FirstDayOfWeek,
                geometry.EstimatedRowHeight,
                geometry.MonthSectionHeight,
                geometry.WeekSectionHeight,
                geometry.DayGap,
                geometry.TimeZone);

            var firstMeasured = int.MaxValue;

            foreach (var row in table.Rows)
            {
                if (row.Appointment is null)
                {
                    continue;
                }

                double measuredHeight;

                if (RowHeightResolver is not null)
                {
                    measuredHeight = RowHeightResolver(row.Appointment);

                    if (!double.IsFinite(measuredHeight))
                        measuredHeight = geometry.EstimatedRowHeight;

                    measuredHeight = Math.Max(geometry.MinimumRowHeight, measuredHeight);
                    measuredHeights[row.Appointment.Key] = measuredHeight;
                }
                else if (!measuredHeights.TryGetValue(row.Appointment.Key, out measuredHeight))
                {
                    continue;
                }

                row.Height = measuredHeight;
                row.Measured = true;
                firstMeasured = Math.Min(firstMeasured, row.Index);
            }

            if (firstMeasured != int.MaxValue)
                table = table with { ContentHeight = AgendaLayoutEngine.Reflow(table.Rows, firstMeasured, geometry.DayGap) };

            pendingContentShift = ShiftOf(anchor, table.Rows);

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

        windowPlacements.Clear();
        windowSections.Clear();
        windowRows.Clear();

        if (rows.Count == 0)
            return windowPlacements;

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
                        row.SectionFor(SchedulerAgendaSectionKind.Month),
                        geometry.RowBounds(row)));
                    break;

                case AgendaRowKind.WeekSection:
                    windowSections.Add(new AgendaSectionPlacement(
                        row.SectionFor(SchedulerAgendaSectionKind.Week),
                        geometry.RowBounds(row)));
                    break;

                case AgendaRowKind.Appointment:
                    windowPlacements.Add(row.Placement);

                    // The day marker is not a row of its own — it hangs in the gutter beside the
                    // first appointment of its day, so an extra day costs nothing but its content.
                    if (row.StartsDay)
                    {
                        windowSections.Add(new AgendaSectionPlacement(
                            row.SectionFor(SchedulerAgendaSectionKind.Day),
                            geometry.DayMarkerBounds(row)));
                    }

                    break;
            }
        }

        return windowPlacements;
    }

    /// <summary>
    /// Records what a row actually measured, correcting everything below it.
    /// </summary>
    /// <returns>The viewport correction and whether any stored row position changed.</returns>
    public AgendaHeightCorrection ApplyMeasuredHeights(IReadOnlyList<(AgendaRow Row, double Height)> measured)
    {
        var rows = Rows;
        var first = int.MaxValue;
        var shiftAbove = 0.0;

        foreach (var (row, measuredHeight) in measured)
        {
            var height = Math.Max(geometry.MinimumRowHeight, measuredHeight);

            if (row.Appointment is not null)
                measuredHeights[row.Appointment.Key] = height;

            if (Math.Abs(row.Height - height) < 0.5)
            {
                row.Measured = true;
                continue;
            }

            // A row entirely above the fold changing height moves everything the reader can see.
            if (row.Bottom <= geometry.VisibleTop)
                shiftAbove += height - row.Height;

            row.Height = height;
            row.Measured = true;
            first = Math.Min(first, row.Index);
        }

        if (first == int.MaxValue)
            return new AgendaHeightCorrection(0, false);

        geometry.SetContentHeight(AgendaLayoutEngine.Reflow(rows, first, geometry.DayGap));

        return new AgendaHeightCorrection(shiftAbove, true);
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

        return new SchedulerTimeSlot(new SchedulerMoment(ToDateTime(date), geometry.TimeZone), TimeSpan.FromDays(1));
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

    /// <summary>
    /// Returns and clears the movement of the row that anchored the viewport across the last rebuild.
    /// </summary>
    public double TakeContentShift()
    {
        var shift = pendingContentShift;
        pendingContentShift = 0;
        return shift;
    }

    /// <summary>
    /// The offset of the first appointment on or after a date, or the nearest later month heading.
    /// </summary>
    public double OffsetFor(DateOnly date)
    {
        var rows = Rows;

        foreach (var row in rows)
        {
            if (row.Kind is AgendaRowKind.Appointment
                && row.Date >= date
                && row.Date.Year == date.Year
                && row.Date.Month == date.Month)
            {
                return row.Top;
            }
        }

        var monthStart = new DateOnly(date.Year, date.Month, 1);

        foreach (var row in rows)
        {
            if (row.Kind is AgendaRowKind.MonthSection && row.Date == monthStart)
                return row.Top;
        }

        foreach (var row in rows)
        {
            if (row.Kind is AgendaRowKind.Appointment && row.Date >= date)
                return row.Top;
        }

        foreach (var row in rows)
        {
            if (row.Kind is AgendaRowKind.MonthSection && row.Date >= date)
                return row.Top;
        }

        return geometry.ContentHeight;
    }

    /// <summary>The date occupying a vertical offset, or null before the row table exists.</summary>
    public DateOnly? DateAt(double offset)
    {
        var rows = Rows;

        if (rows.Count == 0)
            return null;

        var index = Math.Min(AgendaLayoutEngine.IndexAt(rows, Math.Max(0, offset)), rows.Count - 1);
        return rows[index].Date;
    }

    /// <summary>What the host is asked to have loaded, which is the range including what is pending.</summary>
    private (DateOnly Start, DateOnly End) RangeFor(DateOnly pageStart) =>
        rangeInitialised && loadedPage == pageStart
            ? (requestedStart, requestedEnd)
            : (geometry.RangeStart(pageStart), geometry.RangeEnd(pageStart));

    private void EnsureRange(DateOnly pageStart)
    {
        if (rangeInitialised && loadedPage == pageStart)
            return;

        loadedPage = pageStart;
        loadedStart = requestedStart = geometry.RangeStart(pageStart);
        loadedEnd = requestedEnd = geometry.RangeEnd(pageStart);
        rangeRequestOutstanding = false;
        rangeInitialised = true;
    }

    private Anchor? CaptureAnchor()
    {
        var rows = table?.Rows;

        if (rows is null || rows.Count == 0 || tablePage != loadedPage)
            return null;

        var index = Math.Min(AgendaLayoutEngine.IndexAt(rows, geometry.VisibleTop), rows.Count - 1);
        var row = rows[index];

        return new Anchor(
            row.Kind,
            row.Date,
            row.Appointment?.Key,
            row.Top);
    }

    private static double ShiftOf(Anchor? anchor, IReadOnlyList<AgendaRow> rows)
    {
        if (anchor is null)
            return 0;

        foreach (var row in rows)
        {
            if (row.Kind != anchor.Value.Kind || row.Date != anchor.Value.Date)
                continue;

            if (row.Kind is AgendaRowKind.Appointment
                && !Equals(row.Appointment?.Key, anchor.Value.AppointmentKey))
            {
                continue;
            }

            return row.Top - anchor.Value.Top;
        }

        return 0;
    }

    private readonly record struct Anchor(
        AgendaRowKind Kind,
        DateOnly Date,
        object? AppointmentKey,
        double Top);

    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}

internal readonly record struct AgendaHeightCorrection(double ShiftAbove, bool LayoutChanged);
