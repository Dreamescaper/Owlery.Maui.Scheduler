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
/// <see cref="Start"/> and <see cref="End"/> are read according to their
/// <see cref="DateTimeKind"/> and this appointment's <see cref="TimeZone"/>, then placed on the axis
/// of <see cref="SchedulerView.TimeZone"/>. Supply them with <see cref="DateTimeKind.Unspecified"/>
/// and no zone — the default — and nothing is converted at all: they are drawn exactly as given.
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

    /// <summary>
    /// The zone <see cref="Start"/> and <see cref="End"/> are expressed in, when their
    /// <see cref="DateTimeKind"/> does not already say. <c>null</c> — the default — means they speak
    /// for themselves.
    /// </summary>
    /// <remarks>
    /// The kind decides first, and this only speaks when the kind is silent:
    /// <list type="table">
    /// <item>
    ///   <term><see cref="DateTimeKind.Utc"/></term>
    ///   <description>an instant. Converted into the view's zone; this property is <b>ignored</b>.</description>
    /// </item>
    /// <item>
    ///   <term><see cref="DateTimeKind.Local"/></term>
    ///   <description>an instant on this device. Converted; this property is <b>ignored</b>.</description>
    /// </item>
    /// <item>
    ///   <term><see cref="DateTimeKind.Unspecified"/>, this <c>null</c></term>
    ///   <description>floating. Drawn exactly as given, whatever the view's zone is.</description>
    /// </item>
    /// <item>
    ///   <term><see cref="DateTimeKind.Unspecified"/>, this set</term>
    ///   <description>wall-clock in that zone. Converted into the view's zone.</description>
    /// </item>
    /// </list>
    /// <para>
    /// The two ignoring rows are deliberate: a <c>Utc</c> or <c>Local</c> value already carries a
    /// complete instant, so a zone beside it is redundant or contradictory, and ignoring it beats
    /// guessing which the host meant.
    /// </para>
    /// <para>
    /// It is a default interface member, so existing implementations keep compiling and opt in by
    /// overriding it. One wrinkle of that: the default is reachable through the interface but not
    /// through your own type, so read it as
    /// <c>((ISchedulerAppointment)appointment).TimeZone</c> if you have not declared it yourself.
    /// </para>
    /// <para>
    /// The zone says what the times <em>mean</em>. It does not ask for the appointment to be drawn on
    /// that zone's clock — the grid has one time axis, and placing items on different ones would put
    /// them in the wrong order relative to each other. A host wanting to label an appointment with
    /// its own zone renders that in its template, which binds to the host's own type.
    /// </para>
    /// </remarks>
    TimeZoneInfo? TimeZone => null;
}

/// <summary>
/// A moment the control reports, in the zone the calendar is displayed in.
/// </summary>
/// <remarks>
/// Everything the control hands back — a tapped cell, a drop, a tapped header, the visible dates —
/// is one of these rather than a bare <see cref="DateTime"/>, because two kinds of host want two
/// different answers from the same value and neither should have to convert. A host that thinks in
/// wall-clock reads <see cref="WallClock"/> and never meets a time zone; a host whose backend stores
/// instants calls <see cref="ToDateTimeUtc"/>. There is no default that is wrong for half of them.
/// <para>
/// Deliberately no implicit conversion to <see cref="DateTime"/>. The whole value of the type is that
/// "which of the two is this?" cannot be answered by accident, and an implicit conversion would let
/// <c>DateTime start = e.DropStart;</c> compile and quietly pick one.
/// </para>
/// </remarks>
/// <param name="WallClock">
/// The reading on the calendar's own clock, always <see cref="DateTimeKind.Unspecified"/>: it is a
/// position on the grid, not an instant, until <see cref="Zone"/> resolves it.
/// </param>
/// <param name="Zone">The zone the calendar is displayed in — <see cref="SchedulerView.TimeZone"/>.</param>
public readonly record struct SchedulerMoment(DateTime WallClock, TimeZoneInfo Zone)
{
    /// <summary>The instant this moment names, in UTC.</summary>
    /// <remarks>
    /// A clock reading is not always an instant: on the morning a zone springs forward the reading
    /// never happens, and on the morning it falls back it happens twice. Rather than throw, an
    /// invalid reading is moved forward by the gap and an ambiguous one is taken as its first
    /// occurrence — the same rule the control applies to appointments coming the other way. The grid
    /// draws a uniform day, so it can offer a reading that its own zone does not have.
    /// </remarks>
    public DateTime ToDateTimeUtc() => Internal.AppointmentTime.ToUtc(WallClock, Zone);

    /// <summary>The same instant on this device's clock.</summary>
    public DateTime ToDateTimeLocal() => ToDateTimeUtc().ToLocalTime();

    public SchedulerMoment Add(TimeSpan value) => this with { WallClock = WallClock + value };

    public static SchedulerMoment operator +(SchedulerMoment moment, TimeSpan value) => moment.Add(value);

    public override string ToString() => $"{WallClock:yyyy-MM-dd HH:mm} {Zone.Id}";
}


/// <summary>
/// A position on the grid, produced by tapping empty space or by dropping an appointment.
/// </summary>
public readonly record struct SchedulerTimeSlot(SchedulerMoment Start, TimeSpan Duration)
{
    public SchedulerMoment End => Start + Duration;

    public DateOnly Date => DateOnly.FromDateTime(Start.WallClock);
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
public sealed class SchedulerAppointmentDropTargetChangedEventArgs(ISchedulerAppointment appointment, SchedulerMoment dropStart)
    : EventArgs
{
    public ISchedulerAppointment Appointment { get; } = appointment;

    /// <summary>The snapped start it has moved to, in <see cref="SchedulerView.TimeZone"/>.</summary>
    /// <remarks>Where it would land if released now; the drag may still move on or be cancelled.</remarks>
    public SchedulerMoment DropStart { get; } = dropStart;
}

/// <summary>
/// Raised when a dragged appointment is released, asking the host to move it.
/// </summary>
/// <remarks>
/// The control does not move anything itself: it lays the calendar out from <see cref="SchedulerView.ItemsSource"/>
/// as that reads the moment the handler returns. A host that wants the move applies it to its data
/// before returning — replacing the appointment with one starting at <see cref="DropStart"/>, keeping
/// its <see cref="ISchedulerAppointment.Key"/> — and one that does not want it need do nothing at all,
/// which leaves the appointment where it already was. There is nothing to cancel and nothing to
/// release afterwards.
/// <para>
/// An update that can fail belongs after that: apply the move, start the call, and put the old start
/// back if it fails. That last step is an ordinary change to the collection, not part of this event.
/// </para>
/// </remarks>
public sealed class SchedulerAppointmentDroppedEventArgs(ISchedulerAppointment appointment, SchedulerMoment dropStart) : EventArgs
{
    public ISchedulerAppointment Appointment { get; } = appointment;

    /// <summary>The snapped start the appointment was dropped on, in <see cref="SchedulerView.TimeZone"/>.</summary>
    public SchedulerMoment DropStart { get; } = dropStart;
}

/// <summary>
/// Raised when the header above a day column is tapped.
/// </summary>
/// <remarks>
/// A month does not raise it: its header names weekdays that recur down six rows, so there is no one
/// date a column stands for.
/// </remarks>
public sealed class SchedulerHeaderTappedEventArgs(SchedulerMoment date) : EventArgs
{
    /// <summary>Midnight on the day whose header was tapped, in <see cref="SchedulerView.TimeZone"/>.</summary>
    public SchedulerMoment Date { get; } = date;
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
    IReadOnlyList<SchedulerMoment> visibleDates,
    SchedulerMoment prefetchFrom,
    SchedulerMoment prefetchTo) : EventArgs
{
    /// <summary>The seven days of the week the user is currently looking at.</summary>
    public IReadOnlyList<SchedulerMoment> VisibleDates { get; } = visibleDates;

    /// <summary>Start of the first rendered week — data before this is never displayed without another swipe.</summary>
    public SchedulerMoment PrefetchFrom { get; } = prefetchFrom;

    /// <summary>End of the last rendered week.</summary>
    public SchedulerMoment PrefetchTo { get; } = prefetchTo;
}
