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

    public DateTime Start { get; } = start;

    public TimeSpan Duration { get; } = duration;

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
    /// Returns a new appointment at <paramref name="newStart"/>, keeping its identity, length and
    /// stable fields.
    /// </summary>
    /// <remarks>
    /// A move is a change, and a changed appointment must arrive as a new instance rather than being
    /// mutated in place — mutating one changes nothing the control can see until it re-reads the
    /// collection, which is the one habit <c>docs/API.md</c> warns about. Because <see cref="Key"/>
    /// is unchanged, the control reconciles the new instance onto the same view instead of building a
    /// fresh one.
    /// </remarks>
    public SampleAppointment MovedTo(DateTime newStart) =>
        new(Id, newStart, Duration, subject, Person, Palette, IsLocked);
}
