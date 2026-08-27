namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>
/// The playground's implementation of <see cref="ISchedulerAppointment"/>.
/// </summary>
/// <remarks>
/// A host's own type goes here — the instance is the binding context the appointment template sees,
/// so anything the template needs to draw belongs on it. <see cref="Key"/> is boxed once: the control
/// compares it with <see cref="object.Equals(object)"/> on every rebind, and a boxed <c>int</c>
/// allocated per comparison would show up under a few thousand appointments.
/// </remarks>
public sealed class SampleAppointment(int id, DateTime start, TimeSpan duration, string subject, string person, int palette, bool isLocked)
    : ISchedulerAppointment
{
    private readonly object key = id;

    public object Key => key;

    public int Id => id;

    public DateTime Start { get; private set; } = start;

    public TimeSpan Duration { get; private set; } = duration;

    public DateTime End => Start + Duration;

    public string? Subject => subject;

    /// <summary>Who the lesson is with — template detail, not something the control reads.</summary>
    public string Person => person;

    /// <summary>Index into the playground's colour palette, kept stable across moves.</summary>
    public int Palette => palette;

    /// <summary>
    /// Whether the playground refuses to drag this one, demonstrating
    /// <see cref="SchedulerAppointmentDragStartingEventArgs.Cancel"/>.
    /// </summary>
    public bool IsLocked => isLocked;

    /// <summary>
    /// Moves the appointment, keeping its length. The control does not mutate the host's model on a
    /// drop; it reports the drop and waits for the host to re-emit <c>ItemsSource</c>.
    /// </summary>
    public void MoveTo(DateTime newStart) => Start = newStart;
}
