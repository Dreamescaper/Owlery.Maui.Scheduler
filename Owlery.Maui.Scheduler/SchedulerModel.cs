namespace Owlery.Maui.Scheduler;

/// <summary>What kind of page the scheduler shows.</summary>
public enum SchedulerViewMode
{
    /// <summary>Columns of hours — a week, three days or a single day, per <see cref="SchedulerView.VisibleDays"/>.</summary>
    Timeline,

    /// <summary>A calendar month on a fixed six-by-seven grid of days.</summary>
    Month,

    /// <summary>
    /// A vertical list of appointments grouped by day, in date order.
    /// </summary>
    /// <remarks>
    /// The one surface that does not page sideways: it is a single continuous list and the whole of
    /// it scrolls vertically. Days with nothing in them are left out, so the list is as short as the
    /// calendar is empty.
    /// </remarks>
    Agenda
}

/// <summary>Which grouping a heading in the agenda introduces.</summary>
public enum SchedulerAgendaSectionKind
{
    /// <summary>A month. Shown even when the month holds nothing, so the list keeps its spine.</summary>
    Month,

    /// <summary>A week. Left out when the week holds nothing.</summary>
    Week,

    /// <summary>
    /// A day, shown beside its first appointment rather than above it.
    /// </summary>
    /// <remarks>
    /// Unlike the other two this takes no vertical space of its own: it sits in the gutter down the
    /// leading edge, level with the top of that day's first row.
    /// </remarks>
    Day
}

/// <summary>
/// A heading in the agenda. The binding context of a view built from
/// <see cref="SchedulerView.AgendaSectionTemplate"/>.
/// </summary>
/// <remarks>
/// One template renders all three kinds rather than one template each, which is the same reasoning
/// that rules out a <c>DataTemplateSelector</c> for appointments: views are pooled and rebound, and a
/// pool per kind is a pool that usually hands back the wrong shape. Branch on <see cref="Kind"/>
/// inside the template, building each arm up front and toggling between them.
/// </remarks>
/// <param name="Date">The month, week or day introduced, at midnight in the scheduler's time zone.</param>
/// <param name="Kind">Which grouping the section introduces.</param>
/// <param name="AppointmentCount">Appointments in that grouping within the loaded agenda range.</param>
public sealed record SchedulerAgendaSection(
    DateTime Date,
    SchedulerAgendaSectionKind Kind,
    int AppointmentCount);

/// <summary>
/// An item the scheduler places on the week grid.
/// </summary>
/// <remarks>
/// <see cref="Start"/> and <see cref="End"/> are wall-clock values in
/// <see cref="SchedulerView.TimeZone"/>. The control never converts between time zones;
/// the caller decides what "now" means and hands over already-converted values.
/// <para>
/// <b>Implementations should be immutable.</b> The control observes the collection, never the
/// appointments in it: nothing here derives from <c>BindableObject</c>, none of these are bindable
/// properties, and the contract does not ask for <see cref="System.ComponentModel.INotifyPropertyChanged"/>.
/// Changing an appointment in place therefore changes nothing on screen. Worse than nothing: the
/// display disagrees with the model until something unrelated makes the control re-read the
/// collection, and the repaint then arrives looking like a bug rather than like the change it is.
/// </para>
/// <para>
/// Build a new instance carrying the same <see cref="Key"/> instead, and put it in the collection in
/// place of the old one. A <c>record</c> makes that a line — <c>appointment with { Start = newStart }</c>
/// — and because the key has not moved, the control reconciles the new instance onto the view already
/// showing it and repaints, rather than building a fresh view. The sample's <c>SampleAppointment</c>
/// is the reference shape.
/// </para>
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

/// <summary>
/// Raised while dragging, each time the appointment comes to rest on a different snap boundary.
/// </summary>
/// <remarks>
/// Not a movement event: it does not fire as the finger travels within one boundary, only when the
/// position the appointment would be dropped on actually changes. That makes it the right moment for
/// feedback a person should feel once per step — a short haptic tick, most obviously — rather than
/// continuously. It does not fire when the drag is first picked up.
/// </remarks>
public sealed class SchedulerAppointmentDropTargetChangedEventArgs(ISchedulerAppointment appointment, DateTime dropStart)
    : EventArgs
{
    public ISchedulerAppointment Appointment { get; } = appointment;

    /// <summary>The snapped start it has moved to, in <see cref="SchedulerView.TimeZone"/>.</summary>
    /// <remarks>Where it would land if released now; the drag may still move on or be cancelled.</remarks>
    public DateTime DropStart { get; } = dropStart;
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
/// Raised when the header above a day column is tapped.
/// </summary>
/// <remarks>
/// A month does not raise it: its header names weekdays that recur down six rows, so there is no one
/// date a column stands for.
/// </remarks>
public sealed class SchedulerHeaderTappedEventArgs(DateTime date) : EventArgs
{
    /// <summary>Midnight on the day whose header was tapped, in <see cref="SchedulerView.TimeZone"/>.</summary>
    public DateTime Date { get; } = date;
}

/// <summary>
/// Raised when the hour gutter down the leading edge is tapped.
/// </summary>
/// <remarks>
/// Carries a time of day rather than a <see cref="DateTime"/>: the gutter runs alongside every day on
/// the page at once, so a tap on it names an hour and not a date.
/// </remarks>
public sealed class SchedulerTimeGutterTappedEventArgs(TimeSpan time) : EventArgs
{
    /// <summary>
    /// The time at the point tapped, snapped down to <see cref="SchedulerView.SlotMinutes"/> and held
    /// inside the day window.
    /// </summary>
    public TimeSpan Time { get; } = time;
}

/// <summary>
/// Raised whenever the visible period changes, so the host can fetch the data it needs.
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
