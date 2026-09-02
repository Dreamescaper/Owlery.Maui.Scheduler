namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>
/// Owns the playground's appointments and pushes them at the control.
/// </summary>
/// <remarks>
/// The control fetches nothing and owns nothing: it renders whatever <c>ItemsSource</c> holds and
/// picks out the items belonging to each rendered page itself, so one flat collection covering the
/// whole generated window is all it needs. A real host would load from its API in
/// <c>VisibleDatesChanged</c> instead.
/// <para>
/// The collection is assigned to <c>ItemsSource</c> once and then mutated in place with the range
/// operations (<see cref="SchedulerAppointmentCollection{T}.AddRange"/>,
/// <see cref="SchedulerAppointmentCollection{T}.RemoveRange"/>), so the control observes each change
/// rather than being handed a fresh list: new months are appended whole, and a moved or regenerated
/// appointment is swapped by removing and re-adding it. It is never rebuilt item by item — see
/// <see cref="Publish"/>.
/// </para>
/// </remarks>
public sealed class AppointmentSource(SchedulerView scheduler)
{
    private readonly Dictionary<DateOnly, List<SampleAppointment>> appointmentsByMonth = [];
    private readonly SchedulerAppointmentCollection<SampleAppointment> appointments = new();
    private bool published;
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

    /// <summary>
    /// Regenerates every loaded month and swaps the whole flat collection for the result in one
    /// operation, so a full reseed is one change notification however many months are loaded.
    /// </summary>
    /// <remarks>
    /// Per month would be the obvious shape and is the wrong one twice over: it raises two events per
    /// month, and removing a month by value scans the whole flat collection once per appointment —
    /// at five thousand a month across a dozen months that is the slider knob doing hundreds of
    /// millions of comparisons. Replacing the lot is one pass and one event.
    /// </remarks>
    public void Regenerate()
    {
        foreach (var month in appointmentsByMonth.Keys.ToArray())
            appointmentsByMonth[month] = SampleDataGenerator.GenerateMonth(Count, seed, month);

        appointments.ReplaceRange(0, appointments.Count, Loaded());

        Publish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Every loaded month's appointments, oldest month first.</summary>
    private IEnumerable<SampleAppointment> Loaded() =>
        appointmentsByMonth.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value);

    /// <summary>
    /// Loads the selected quantity for every calendar month intersecting the scheduler's range.
    /// </summary>
    /// <remarks>
    /// Months are never dropped once loaded. The control filters the visible period itself, so a host
    /// keeps the whole loaded range and does not trim it when a period scrolls off screen — the
    /// guidance in <c>docs/API.md</c>. The range only grows: newly requested months are appended
    /// bulk at a time with <see cref="SchedulerAppointmentCollection{T}.AddRange"/>, and existing
    /// months retain their appointment instances, so the control's identity-based reconciliation
    /// leaves visible rows untouched.
    /// </remarks>
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

        // ONLY growth: a month the control asked for once stays loaded forever.
        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            if (appointmentsByMonth.ContainsKey(month))
                continue;

            var items = SampleDataGenerator.GenerateMonth(Count, seed, month);
            appointmentsByMonth[month] = items;
            appointments.AddRange(items);
        }

        Publish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves an appointment between the cached month buckets and republishes it.
    /// </summary>
    /// <remarks>
    /// The moved appointment is delivered as a new instance — a move is a change, and a changed
    /// appointment must be a new instance rather than one mutated in place (docs/API.md). Because
    /// <see cref="SampleAppointment.Key"/> is unchanged, the control reconciles the new instance onto
    /// the same view and simply repaints it at the new time.
    /// <para>
    /// The flat collection is changed in one operation rather than a removal and an addition: the new
    /// instance takes the old one's place, which is one event and keeps the collection's order.
    /// </para>
    /// </remarks>
    public void Move(SampleAppointment appointment, DateTime start)
    {
        var moved = appointment.MovedTo(start);

        foreach (var month in appointmentsByMonth.Values)
            month.Remove(appointment);

        var target = MonthOf(start);

        if (!appointmentsByMonth.TryGetValue(target, out var targetMonth))
            appointmentsByMonth[target] = targetMonth = [];

        targetMonth.Add(moved);

        // A record, so this finds it by value — which is exact, because Id is unique and part of it.
        var position = appointments.IndexOf(appointment);

        if (position >= 0)
            appointments.ReplaceRange(position, 1, [moved]);
        else
            appointments.Add(moved);

        Publish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands the control the collection the first time, which is what tells it to repaint. After
    /// that the mutations are observed in place, so this is a no-op. The range operations above
    /// settle a dropped appointment against the model by changing the collection, which the control
    /// reads and repopulates from.
    /// </summary>
    public void Publish()
    {
        if (published)
            return;

        scheduler.ItemsSource = appointments;
        published = true;
    }

    private static DateOnly MonthOf(DateTime date) => new(date.Year, date.Month, 1);
}
