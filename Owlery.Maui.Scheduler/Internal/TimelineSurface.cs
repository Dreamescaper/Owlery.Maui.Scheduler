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
        [.. AppointmentLayoutEngine.Layout(items, pageStart, geometry.VisibleDays, geometry.StartHour, geometry.EndHour)];

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

    public Rect BoundsFor(SchedulerTimeSlot slot, DateOnly pageStart)
    {
        var dayIndex = DateOnly.FromDateTime(slot.Start).DayNumber - pageStart.DayNumber;
        var y = geometry.YFromMinutes(slot.Start.TimeOfDay.TotalMinutes);
        var height = Math.Max(MinimumAppointmentHeight, geometry.YFromMinutes(slot.End.TimeOfDay.TotalMinutes) - y);

        return new Rect(dayIndex * geometry.DayWidth, y, geometry.DayWidth, height);
    }

    /// <summary>Nothing overflows on a timeline: an appointment gets the height its duration asks for.</summary>
    public IReadOnlyList<int> OverflowFor(DateOnly pageStart) => [];

    public SchedulerTimeSlot? SlotAt(Point point, PageSlot[] pages)
    {
        if (geometry.ViewportWidth <= 0)
            return null;

        var slotIndex = Math.Clamp((int)(point.X / geometry.ViewportWidth), 0, SchedulerGeometry.SlotCount - 1);
        var xInSlot = point.X - slotIndex * geometry.ViewportWidth;
        var dayIndex = Math.Clamp((int)(xInSlot / geometry.DayWidth), 0, geometry.VisibleDays - 1);

        var snap = Math.Max(1, snapMinutes());
        var minutes = geometry.MinutesFromY(point.Y);
        var snapped = Math.Floor(minutes / snap) * snap;
        snapped = Math.Clamp(snapped, geometry.WindowStartMinutes, geometry.WindowEndMinutes - snap);

        var date = pages[slotIndex].PageStart.AddDays(dayIndex);
        var start = date.ToDateTime(TimeOnly.MinValue).AddMinutes(snapped);

        return new SchedulerTimeSlot(start, TimeSpan.FromMinutes(snap));
    }
}
