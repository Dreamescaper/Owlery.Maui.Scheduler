namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>
/// Builds one deterministic month of appointments, so extending the scheduler's requested range can
/// add data without replacing appointments that were already loaded.
/// </summary>
public static class SampleDataGenerator
{
    private static readonly string[] Subjects =
    [
        "Piano", "Guitar", "Violin", "Maths", "Physics", "English", "Spanish",
        "Drawing", "Chess", "Vocals", "Chemistry", "Coding"
    ];

    private static readonly string[] People =
    [
        "Anna", "Ben", "Clara", "Dmytro", "Elena", "Farid", "Greta", "Hugo",
        "Iryna", "Jonas", "Kateryna", "Liam", "Marta", "Daisy", "Olena", "Pavlo"
    ];

    private static readonly int[] Durations = [15, 30, 30, 45, 60, 60, 90, 120];

    /// <summary>
    /// Spreads <paramref name="count"/> appointments over one calendar month, clustered into normal
    /// teaching hours so overlaps happen naturally.
    /// </summary>
    /// <param name="asUtc">
    /// Whether to stamp the times as instants. Floating is the default and the simple case; UTC shows
    /// what a host with an instant-storing backend supplies, and moves when the view's zone changes.
    /// </param>
    // PROBE: weekly-recurring data for the same-bounds recycling experiment.
    public static bool Recurring;

    /// <summary>PROBE: share of occurrences that are cancelled, moved, or joined by a one-off (a third each).</summary>
    public static double ExceptionRate;

    public static List<SampleAppointment> GenerateMonth(int count, int seed, DateOnly month, bool asUtc = false)
    {
        if (Recurring)
            return GenerateRecurringMonth(count, seed, month);

        var appointments = new List<SampleAppointment>(count);
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        var monthOrdinal = (monthStart.Year - 2000) * 12 + monthStart.Month - 1;
        var monthSeed = unchecked(seed * 1_000_003 + monthStart.Year * 397 + monthStart.Month * 17);
        var random = new Random(monthSeed);

        for (var index = 0; index < count; index++)
        {
            var day = monthStart.AddDays(random.Next(days)).ToDateTime(TimeOnly.MinValue);
            var minutes = (random.Next(8, 21) * 60) + (random.Next(4) * 15);
            var duration = TimeSpan.FromMinutes(Durations[random.Next(Durations.Length)]);
            var subject = Subjects[random.Next(Subjects.Length)];
            var person = People[random.Next(People.Length)];
            var id = checked(monthOrdinal * 10_000 + index);

            // Every tenth item refuses to be dragged, so the cancellable drag event has something
            // to demonstrate without the host having to hand-pick an appointment.
            var start = day.AddMinutes(minutes);

            appointments.Add(new SampleAppointment(
                id,
                asUtc ? DateTime.SpecifyKind(start, DateTimeKind.Utc) : start,
                duration,
                subject,
                person,
                Palette: Math.Abs(id % SamplePalette.Count),
                IsLocked: random.Next(10) == 0));
        }

        appointments.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return appointments;
    }

    /// <summary>PROBE: the same weekly pattern repeated through the month — every occurrence a new instance.</summary>
    private static List<SampleAppointment> GenerateRecurringMonth(int count, int seed, DateOnly month)
    {
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        var monthOrdinal = (monthStart.Year - 2000) * 12 + monthStart.Month - 1;
        var random = new Random(seed);
        var perWeek = Math.Max(1, (int)Math.Round(count * 7.0 / 30));

        var pattern = Enumerable.Range(0, perWeek).Select(_ => (
            Day: (DayOfWeek)random.Next(7),
            Minutes: (random.Next(8, 21) * 60) + (random.Next(4) * 15),
            Duration: TimeSpan.FromMinutes(Durations[random.Next(Durations.Length)]),
            Subject: Subjects[random.Next(Subjects.Length)],
            Person: People[random.Next(People.Length)],
            Palette: random.Next(SamplePalette.Count),
            Locked: random.Next(10) == 0)).ToArray();

        var appointments = new List<SampleAppointment>();
        var index = 0;
        var exceptions = new Random(unchecked(seed * 1_000_003 + monthStart.Year * 397 + monthStart.Month * 17));
        var share = ExceptionRate / 3;

        for (var d = 0; d < days; d++)
        {
            var day = monthStart.AddDays(d);

            foreach (var session in pattern)
            {
                if (session.Day != day.DayOfWeek)
                    continue;

                var roll = exceptions.NextDouble();

                if (roll < share)
                    continue; // cancelled

                var minutes = roll < 2 * share
                    ? (exceptions.Next(8, 21) * 60) + (exceptions.Next(4) * 15) // moved within the day
                    : session.Minutes;

                appointments.Add(new SampleAppointment(
                    checked(monthOrdinal * 10_000 + index++),
                    day.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes),
                    session.Duration,
                    session.Subject,
                    session.Person,
                    session.Palette,
                    session.Locked));
            }

            // One-offs, about as many as each other kind of exception.
            var expected = pattern.Count(session => session.Day == day.DayOfWeek) * share;

            for (var extra = expected; extra > 0; extra--)
            {
                if (extra < 1 && exceptions.NextDouble() >= extra)
                    break;

                appointments.Add(new SampleAppointment(
                    checked(monthOrdinal * 10_000 + index++),
                    day.ToDateTime(TimeOnly.MinValue).AddMinutes((exceptions.Next(8, 21) * 60) + (exceptions.Next(4) * 15)),
                    TimeSpan.FromMinutes(Durations[exceptions.Next(Durations.Length)]),
                    Subjects[exceptions.Next(Subjects.Length)],
                    People[exceptions.Next(People.Length)],
                    exceptions.Next(SamplePalette.Count),
                    false));
            }
        }

        appointments.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return appointments;
    }
}
