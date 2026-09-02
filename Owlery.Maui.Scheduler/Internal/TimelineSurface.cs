namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// The day/three-day/week surface: a page is <see cref="SchedulerGeometry.VisibleDays"/> columns of
/// hours, and the vertical axis is time.
/// </summary>
internal sealed class TimelineSurface(SchedulerGeometry geometry, Func<int> snapMinutes) : ISchedulerSurface
{
    /// <summary>Space left either side of an appointment so touching columns stay distinguishable.</summary>
    public const double AppointmentGap = 1;

    /// <summary>Appointments shorter than this still get a box big enough to hit.</summary>
    public const double MinimumAppointmentHeight = 18;

    /// <summary>
    /// The first day of the page containing <paramref name="date"/>.
    /// </summary>
    /// <remarks>
    /// A full week snaps to <see cref="SchedulerGeometry.FirstDayOfWeek"/>, because a week that
    /// started on an arbitrary day would not be one. Shorter pages start on the day asked for, which
    /// is what makes "today" the leading column in a day or three-day view.
    /// </remarks>
    public DateOnly StartOfPage(DateOnly date)
    {
        if (geometry.VisibleDays < 7)
            return date;

        var diff = ((int)date.DayOfWeek - (int)geometry.FirstDayOfWeek + 7) % 7;
        return date.AddDays(-diff);
    }

    public DateOnly NextPage(DateOnly pageStart) => pageStart.AddDays(geometry.VisibleDays);

    public DateOnly PreviousPage(DateOnly pageStart) => pageStart.AddDays(-geometry.VisibleDays);

    public IReadOnlyList<DateOnly> DatesOn(DateOnly pageStart) =>
        [.. Enumerable.Range(0, geometry.VisibleDays).Select(pageStart.AddDays)];

    public bool PageContains(DateOnly pageStart, DateOnly date)
    {
        var offset = date.DayNumber - pageStart.DayNumber;
        return offset >= 0 && offset < geometry.VisibleDays;
    }

    public IReadOnlyList<IAppointmentPlacement> Layout(IEnumerable<ISchedulerAppointment> items, DateOnly pageStart) =>
        AppointmentLayoutEngine.Layout(
            items, pageStart, geometry.VisibleDays, geometry.StartHour, geometry.EndHour, geometry.TimeZone);

    public Rect BoundsFor(IAppointmentPlacement placement)
    {
        var position = (PositionedAppointment)placement;

        var dayWidth = geometry.DayWidth;
        var x = position.DayIndex * dayWidth + position.LeftFraction * dayWidth + AppointmentGap;
        var width = Math.Max(1, position.WidthFraction * dayWidth - AppointmentGap * 2);
        var y = geometry.YFromMinutes(position.StartMinutes);
        var height = Math.Max(MinimumAppointmentHeight, geometry.YFromMinutes(position.EndMinutes) - y);

        return new Rect(x, y, width, height);
    }

    /// <summary>The exact extent of a slot, with none of the minimum an appointment gets.</summary>
    /// <remarks>
    /// <see cref="MinimumAppointmentHeight"/> exists so a short appointment stays visible and
    /// tappable. A selection marker is neither: it is not touchable, and drawn taller than its slot it
    /// claims time that is not selected — a quarter hour marked out to twenty-two minutes, overlapping
    /// the slot below.
    /// </remarks>
    public Rect BoundsFor(SchedulerTimeSlot slot, DateOnly pageStart)
    {
        var dayIndex = DateOnly.FromDateTime(slot.Start.WallClock).DayNumber - pageStart.DayNumber;
        var y = geometry.YFromMinutes(slot.Start.WallClock.TimeOfDay.TotalMinutes);
        var height = geometry.YFromMinutes(slot.End.WallClock.TimeOfDay.TotalMinutes) - y;

        return new Rect(dayIndex * geometry.DayWidth, y, geometry.DayWidth, height);
    }

    /// <summary>Nothing overflows on a timeline: an appointment gets the height its duration asks for.</summary>
    public IReadOnlyList<int> OverflowFor(DateOnly pageStart) => [];

    public IReadOnlyList<AgendaSectionPlacement> SectionsFor(DateOnly pageStart) => [];

    public SchedulerTimeSlot? SlotAt(Point point, PageSlot[] pages)
    {
        if (DateAt(point.X, pages) is not { } date)
            return null;

        var snapped = Snap(geometry.MinutesFromY(point.Y));
        var start = date.ToDateTime(TimeOnly.MinValue).AddMinutes(snapped);

        return new SchedulerTimeSlot(new SchedulerMoment(start, geometry.TimeZone), TimeSpan.FromMinutes(SnapInterval));
    }

    /// <summary>
    /// Which day a horizontal position falls in, across all three rendered pages.
    /// </summary>
    /// <remarks>
    /// Shared by the grid and by the day headers above it. The two are separate views in separate
    /// coordinate spaces, but both are three pages wide and laid out to the same column boundaries,
    /// so resolving a column is one question and is answered here once.
    /// </remarks>
    public DateOnly? DateAt(double x, PageSlot[] pages)
    {
        if (geometry.ViewportWidth <= 0)
            return null;

        var slotIndex = Math.Clamp((int)(x / geometry.ViewportWidth), 0, SchedulerGeometry.SlotCount - 1);
        var xInSlot = x - slotIndex * geometry.ViewportWidth;
        var dayIndex = Math.Clamp((int)(xInSlot / geometry.DayWidth), 0, geometry.VisibleDays - 1);

        return pages[slotIndex].PageStart.AddDays(dayIndex);
    }

    /// <summary>
    /// Rounds a minute of the day to the snap interval and holds it inside the day window.
    /// </summary>
    /// <remarks>
    /// Snapped *down* rather than to the nearest interval, so what a tap selects always begins at or
    /// before the point touched — tapping just under an hour line selects that hour, never the one
    /// after it. Shared by the grid and the hour gutter so the two cannot disagree about which slot a
    /// given height is.
    /// </remarks>
    public double Snap(double minutesOfDay)
    {
        var snap = SnapInterval;
        var snapped = Math.Floor(minutesOfDay / snap) * snap;

        return Math.Clamp(snapped, geometry.WindowStartMinutes, geometry.WindowEndMinutes - snap);
    }

    private int SnapInterval => Math.Max(1, snapMinutes());
}
