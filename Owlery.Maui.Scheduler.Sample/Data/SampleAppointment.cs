namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>
/// The playground's implementation of <see cref="ISchedulerAppointment"/>.
/// </summary>
/// <remarks>
/// A host's own type goes here — the instance is the binding context the appointment template sees,
/// so anything the template needs to draw belongs on it.
/// <para>
/// A record, and immutable, because that is what the control asks for. It observes the collection,
/// never the appointments in it: there is no <c>INotifyPropertyChanged</c> and there are no bindable
/// properties on an appointment, so a property changed in place changes nothing on screen until
/// something makes the control re-read the collection — and then it repaints from a value that has
/// silently disagreed with the display in the meantime. A change is a new instance instead, which
/// <c>with</c> makes a one-liner; see <see cref="MovedTo"/>.
/// </para>
/// <para>
/// <see cref="Key"/> is boxed once: the control compares it with <see cref="object.Equals(object)"/>
/// on every rebind, and a boxed <c>int</c> allocated per comparison would show up under a few
/// thousand appointments. The box rides along with <c>with</c>, so a copy keeps the original's — right
/// for every change except one to <see cref="Id"/>, and changing that would be changing which
/// appointment this is.
/// </para>
/// </remarks>
/// <param name="Id">Identity, and the value <see cref="Key"/> is built from.</param>
/// <param name="Start">Wall-clock start, in the scheduler's time zone.</param>
/// <param name="Duration">How long it runs; <see cref="End"/> is derived from it.</param>
/// <param name="Subject">What it is, drawn by the template and read for the accessibility description.</param>
/// <param name="Person">Who the lesson is with — template detail, not something the control reads.</param>
/// <param name="Palette">Index into the playground's colour palette, kept stable across moves.</param>
/// <param name="IsLocked">
/// Whether the playground refuses to drag this one, demonstrating
/// <see cref="SchedulerAppointmentDragStartingEventArgs.Cancel"/>.
/// </param>
public sealed record SampleAppointment(
    int Id,
    DateTime Start,
    TimeSpan Duration,
    string Subject,
    string Person,
    int Palette,
    bool IsLocked) : ISchedulerAppointment
{
    private readonly object key = Id;

    public object Key => key;

    public DateTime End => Start + Duration;

    /// <summary>
    /// Returns a new appointment at <paramref name="newStart"/>, keeping its identity, length and
    /// everything else.
    /// </summary>
    /// <remarks>
    /// A move is a change, and a changed appointment arrives as a new instance rather than one
    /// mutated in place. Because <see cref="Key"/> is unchanged, the control reconciles the new
    /// instance onto the same view and simply repaints it at its new time.
    /// </remarks>
    public SampleAppointment MovedTo(DateTime newStart) => this with { Start = newStart };
}
