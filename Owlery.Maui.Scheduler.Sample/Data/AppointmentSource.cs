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
    private readonly Dictionary<DateOnly, List<SampleAppointment>> appointmentsByMonth = [];
    private List<SampleAppointment> appointments = [];
    private int seed = 1;
    private DateOnly? rangeStart;
    private DateOnly? rangeEnd;

    /// <summary>How many appointments the generator produces for each loaded calendar month.</summary>
    public int Count { get; private set; }

    public int LoadedMonthCount => appointmentsByMonth.Count;

    public int LoadedAppointmentCount => appointments.Count;

    /// <summary>Raised after the collection is rebuilt, so the chrome can restate the count.</summary>
    public event EventHandler? Changed;

    public void SetCount(int count)
    {
        count = Math.Clamp(count, 0, 5000);

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
        foreach (var month in appointmentsByMonth.Keys.ToArray())
            appointmentsByMonth[month] = SampleDataGenerator.GenerateMonth(Count, seed, month);

        RebuildFlatList();
        Publish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Loads the selected quantity for every calendar month intersecting the scheduler's range.
    /// Existing months retain their appointment instances when the range grows, which lets the
    /// control's identity-based reconciliation leave visible rows untouched.
    /// </summary>
    public void SetRange(DateTime from, DateTime to)
    {
        var first = MonthOf(from);
        var last = MonthOf(to);

        if (last < first)
            (first, last) = (last, first);

        if (rangeStart == first && rangeEnd == last)
            return;

        rangeStart = first;
        rangeEnd = last;

        foreach (var loaded in appointmentsByMonth.Keys.Where(month => month < first || month > last).ToArray())
            appointmentsByMonth.Remove(loaded);

        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            if (!appointmentsByMonth.ContainsKey(month))
                appointmentsByMonth[month] = SampleDataGenerator.GenerateMonth(Count, seed, month);
        }

        RebuildFlatList();
        Publish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves an appointment between the cached month buckets and republishes it.</summary>
    public void Move(SampleAppointment appointment, DateTime start)
    {
        foreach (var month in appointmentsByMonth.Values)
            month.Remove(appointment);

        appointment.MoveTo(start);

        var target = MonthOf(start);

        if (!appointmentsByMonth.TryGetValue(target, out var targetMonth))
            appointmentsByMonth[target] = targetMonth = [];

        targetMonth.Add(appointment);
        RebuildFlatList();
        Publish();
    }

    /// <summary>
    /// Hands the control a fresh collection instance, which is what tells it to repaint. Called
    /// after a drop: the control leaves a dropped appointment where it was released and waits for
    /// the host to confirm the move by re-emitting, so this is what settles it against the model.
    /// </summary>
    public void Publish() => scheduler.ItemsSource = appointments.ToList();

    private void RebuildFlatList()
    {
        appointments =
        [
            .. appointmentsByMonth
                .OrderBy(pair => pair.Key)
                .SelectMany(pair => pair.Value)
                .OrderBy(appointment => appointment.Start)
        ];
    }

    private static DateOnly MonthOf(DateTime date) => new(date.Year, date.Month, 1);
}
