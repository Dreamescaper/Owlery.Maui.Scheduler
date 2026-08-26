namespace Owlery.Maui.Scheduler;

/// <summary>What kind of page the scheduler shows.</summary>
public enum SchedulerViewMode
{
    /// <summary>Columns of hours — a week, three days or a single day, per <see cref="SchedulerView.VisibleDays"/>.</summary>
    Timeline,

    /// <summary>A calendar month on a fixed six-by-seven grid of days.</summary>
    Month
}

/// <summary>
/// An item the scheduler places on the week grid.
/// </summary>
/// <remarks>
/// <see cref="Start"/> and <see cref="End"/> are wall-clock values in
/// <see cref="SchedulerView.TimeZone"/>. The control never converts between time zones;
/// the caller decides what "now" means and hands over already-converted values.
/// </remarks>
public interface ISchedulerAppointment
{
    /// <summary>
    /// Stable identity for this appointment, used to recognise it across collection changes.
    /// </summary>
    /// <remarks>
    /// The control never assumes it is handed the same object twice. A host is free to rebuild its
    /// collection at any moment — and will, because moving an appointment between periods asks it to
    /// load the periods it passes through — so equality of this value, not of the instance, is what
    /// ties an appointment to the view showing it and to the drag carrying it.
    /// Compared with <see cref="object.Equals(object)"/>; any stable value will do.
    /// </remarks>
    object Key { get; }

    DateTime Start { get; }

    DateTime End { get; }

    /// <summary>Short text describing the appointment, used for the accessibility description.</summary>
    string? Subject { get; }
}

/// <summary>
/// A position on the grid, produced by tapping empty space or by dropping an appointment.
/// </summary>
public readonly record struct SchedulerTimeSlot(DateTime Start, TimeSpan Duration)
{
    public DateTime End => Start + Duration;

    public DateOnly Date => DateOnly.FromDateTime(Start);
}

public sealed class SchedulerCellTappedEventArgs(SchedulerTimeSlot slot) : EventArgs
{
    public SchedulerTimeSlot Slot { get; } = slot;
}

public sealed class SchedulerAppointmentTappedEventArgs(ISchedulerAppointment appointment) : EventArgs
{
    public ISchedulerAppointment Appointment { get; } = appointment;
}

public sealed class SchedulerAppointmentDragStartingEventArgs(ISchedulerAppointment appointment) : EventArgs
{
    public ISchedulerAppointment Appointment { get; } = appointment;

    /// <summary>Set to <c>true</c> to prevent the drag from starting.</summary>
    public bool Cancel { get; set; }
}

public sealed class SchedulerAppointmentDroppedEventArgs(ISchedulerAppointment appointment, DateTime dropStart) : EventArgs
{
    public ISchedulerAppointment Appointment { get; } = appointment;

    /// <summary>The snapped start the appointment was dropped on, in <see cref="SchedulerView.TimeZone"/>.</summary>
    public DateTime DropStart { get; } = dropStart;

    /// <summary>Set to <c>true</c> to reject the drop and snap the appointment back.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Raised whenever the centre week changes, so the host can fetch the data it needs.
/// </summary>
public sealed class SchedulerVisibleDatesChangedEventArgs(
    IReadOnlyList<DateTime> visibleDates,
    DateTime prefetchFrom,
    DateTime prefetchTo) : EventArgs
{
    /// <summary>The seven days of the week the user is currently looking at.</summary>
    public IReadOnlyList<DateTime> VisibleDates { get; } = visibleDates;

    /// <summary>Start of the first rendered week — data before this is never displayed without another swipe.</summary>
    public DateTime PrefetchFrom { get; } = prefetchFrom;

    /// <summary>End of the last rendered week.</summary>
    public DateTime PrefetchTo { get; } = prefetchTo;
}
