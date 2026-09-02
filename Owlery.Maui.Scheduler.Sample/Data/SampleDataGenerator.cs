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
    public static List<SampleAppointment> GenerateMonth(int count, int seed, DateOnly month)
    {
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
            appointments.Add(new SampleAppointment(
                id,
                day.AddMinutes(minutes),
                duration,
                subject,
                person,
                Palette: Math.Abs(id % SamplePalette.Count),
                IsLocked: random.Next(10) == 0));
        }

        appointments.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return appointments;
    }
}
