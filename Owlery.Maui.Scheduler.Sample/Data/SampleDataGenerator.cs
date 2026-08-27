namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>
/// Builds a deterministic set of appointments around today, so the count knob produces the same
/// calendar every time until the seed is changed.
/// </summary>
public static class SampleDataGenerator
{
    /// <summary>How wide a window the generated appointments cover, centred on today.</summary>
    public const int WindowDays = 84;

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
    /// Spreads <paramref name="count"/> appointments over <see cref="WindowDays"/> days centred on
    /// <paramref name="today"/>, clustered into normal teaching hours so overlaps happen naturally.
    /// </summary>
    public static List<SampleAppointment> Generate(int count, int seed, DateTime today)
    {
        var appointments = new List<SampleAppointment>(count);
        var random = new Random(seed);
        var firstDay = today.Date.AddDays(-WindowDays / 2);

        for (var id = 0; id < count; id++)
        {
            var day = firstDay.AddDays(random.Next(WindowDays));
            var minutes = (random.Next(8, 21) * 60) + (random.Next(4) * 15);
            var duration = TimeSpan.FromMinutes(Durations[random.Next(Durations.Length)]);
            var subject = Subjects[random.Next(Subjects.Length)];
            var person = People[random.Next(People.Length)];

            // Every tenth item refuses to be dragged, so the cancellable drag event has something
            // to demonstrate without the host having to hand-pick an appointment.
            appointments.Add(new SampleAppointment(
                id,
                day.AddMinutes(minutes),
                duration,
                subject,
                person,
                palette: id % SamplePalette.Count,
                isLocked: random.Next(10) == 0));
        }

        appointments.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return appointments;
    }
}
