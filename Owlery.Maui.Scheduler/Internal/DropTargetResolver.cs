namespace Owlery.Maui.Scheduler.Internal;

/// <summary>Where a dragged appointment would land if it were released now.</summary>
internal readonly record struct DropTarget(int DayIndex, double SnappedMinutes, DateTime Start)
{
    /// <summary>Left edge of the appointment within its page, in surface coordinates.</summary>
    public double XWithinPage(SchedulerGeometry geometry, double gap) => DayIndex * geometry.DayWidth + gap;

    public double Y(SchedulerGeometry geometry) => geometry.YFromMinutes(SnappedMinutes);
}

/// <summary>
/// Resolves a touch point into the day and time an appointment would take.
/// </summary>
/// <remarks>
/// Kept apart from the control because it is the part of dragging that is arithmetic rather than
/// view manipulation, and the part most worth pinning down: the rounding differs from cell selection
/// (nearest, not containing), the clamp has to keep the whole appointment inside the visible hours,
/// and the column has to be bounded by the page rather than by a week.
/// </remarks>
internal static class DropTargetResolver
{
    public static DropTarget Resolve(
        SchedulerGeometry geometry,
        DateOnly pageStart,
        Point point,
        Point grabOffset,
        double pageOffsetX,
        double appointmentHeight,
        int snapMinutes)
    {
        // Where the appointment's top-left wants to be, keeping the grabbed point under the finger.
        var desiredX = point.X - grabOffset.X - pageOffsetX;
        var desiredY = point.Y - grabOffset.Y;

        var dayIndex = Math.Clamp((int)Math.Round(desiredX / geometry.DayWidth), 0, geometry.VisibleDays - 1);

        var snap = Math.Max(1, snapMinutes);
        var minutes = geometry.MinutesFromY(desiredY);

        // Nearest boundary rather than the containing one: under a moving finger, rounding down would
        // make the appointment trail behind where it looks like it is being put.
        var snapped = Math.Round(minutes / snap) * snap;

        // Clamped so the whole appointment stays inside the window, not just its top edge.
        snapped = Math.Clamp(
            snapped,
            geometry.WindowStartMinutes,
            Math.Max(geometry.WindowStartMinutes, geometry.WindowEndMinutes - (appointmentHeight / geometry.HourHeight * 60)));

        var start = pageStart.AddDays(dayIndex).ToDateTime(TimeOnly.MinValue).AddMinutes(snapped);

        return new DropTarget(dayIndex, snapped, start);
    }
}
