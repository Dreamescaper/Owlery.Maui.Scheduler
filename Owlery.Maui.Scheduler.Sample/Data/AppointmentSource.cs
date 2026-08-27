namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>
/// Owns the playground's appointments and pushes them at the control.
/// </summary>
/// <remarks>
/// The control fetches nothing and owns nothing: it renders whatever <c>ItemsSource</c> holds and
/// picks out the items belonging to each rendered page itself, so one flat list covering the whole
/// generated window is all it needs. A real host would load from its API in
/// <c>VisibleDatesChanged</c> instead.
/// </remarks>
public sealed class AppointmentSource(SchedulerView scheduler)
{
    private List<SampleAppointment> appointments = [];
    private int seed = 1;

    /// <summary>How many appointments the generator produces.</summary>
    public int Count { get; private set; }

    /// <summary>Raised after the collection is rebuilt, so the chrome can restate the count.</summary>
    public event EventHandler? Changed;

    public void SetCount(int count)
    {
        count = Math.Clamp(count, 0, 20000);

        if (count == Count)
            return;

        Count = count;
        Regenerate();
    }

    /// <summary>Rebuilds with a different seed — same count, a different calendar.</summary>
    public void Reseed()
    {
        seed++;
        Regenerate();
    }

    public void Regenerate()
    {
        appointments = SampleDataGenerator.Generate(Count, seed, DateTime.Today);
        Publish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands the control a fresh collection instance, which is what tells it to repaint. Called
    /// after a drop: the control leaves a dropped appointment where it was released and waits for
    /// the host to confirm the move by re-emitting, so this is what settles it against the model.
    /// </summary>
    public void Publish() => scheduler.ItemsSource = appointments.ToList();
}
