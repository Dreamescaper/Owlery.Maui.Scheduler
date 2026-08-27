using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class AppointmentLayoutEngineTests
{
    private static readonly DateOnly WeekStart = new(2026, 8, 24); // Monday
    private const int StartHour = 8;
    private const int EndHour = 23;

    // The engine builds the interface list the control consumes; the tests want the concrete type.
    private static List<PositionedAppointment> Layout(params ISchedulerAppointment[] appointments)
        => [.. AppointmentLayoutEngine.Layout(appointments, WeekStart, 7, StartHour, EndHour)
            .Cast<PositionedAppointment>()];

    private static DateTime Day(int offset) => WeekStart.AddDays(offset).ToDateTime(TimeOnly.MinValue);

    [Test]
    public void Appointment_is_placed_on_its_own_day_at_its_own_time()
    {
        var result = Layout(TestAppointment.At(Day(2), "10:00", 1));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].DayIndex, Is.EqualTo(2));
            Assert.That(result[0].StartMinutes, Is.EqualTo(600));
            Assert.That(result[0].EndMinutes, Is.EqualTo(660));
        });
    }

    [Test]
    public void Appointments_that_do_not_overlap_each_use_the_full_column()
    {
        var result = Layout(
            TestAppointment.At(Day(0), "09:00", 1),
            TestAppointment.At(Day(0), "11:00", 1));

        Assert.That(result.Select(p => p.WidthFraction), Is.All.EqualTo(1));
        Assert.That(result.Select(p => p.LeftFraction), Is.All.EqualTo(0));
    }

    [Test]
    public void Two_overlapping_appointments_share_the_column()
    {
        var result = Layout(
            TestAppointment.At(Day(0), "09:00", 2),
            TestAppointment.At(Day(0), "10:00", 2));

        Assert.That(result.Select(p => p.WidthFraction), Is.All.EqualTo(0.5));
        Assert.That(result.Select(p => p.LeftFraction).Order(), Is.EqualTo(new[] { 0, 0.5 }));
    }

    [Test]
    public void Three_overlapping_appointments_split_the_column_into_thirds()
    {
        var result = Layout(
            TestAppointment.At(Day(0), "09:00", 3),
            TestAppointment.At(Day(0), "10:00", 3),
            TestAppointment.At(Day(0), "11:00", 3));

        Assert.That(result.Select(p => p.WidthFraction), Is.All.EqualTo(1d / 3).Within(1e-9));
    }

    [Test]
    public void A_free_column_is_reused_rather_than_narrowing_everything()
    {
        // B ends before C starts, so C sits in B's column instead of forcing a third.
        var result = Layout(
            TestAppointment.At(Day(0), "09:00", 5, "A"),
            TestAppointment.At(Day(0), "09:00", 1, "B"),
            TestAppointment.At(Day(0), "11:00", 1, "C"));

        Assert.That(result.Select(p => p.WidthFraction), Is.All.EqualTo(0.5));
    }

    [Test]
    public void A_crowded_morning_does_not_narrow_an_uncontested_afternoon()
    {
        var result = Layout(
            TestAppointment.At(Day(0), "09:00", 2, "morning-a"),
            TestAppointment.At(Day(0), "09:30", 2, "morning-b"),
            TestAppointment.At(Day(0), "16:00", 1, "afternoon"));

        var afternoon = result.Single(p => ((TestAppointment)p.Appointment).Subject == "afternoon");
        Assert.That(afternoon.WidthFraction, Is.EqualTo(1));
    }

    [Test]
    public void Overlaps_are_resolved_per_day_not_across_the_week()
    {
        var result = Layout(
            TestAppointment.At(Day(0), "09:00", 2),
            TestAppointment.At(Day(1), "09:00", 2));

        Assert.That(result.Select(p => p.WidthFraction), Is.All.EqualTo(1));
    }

    [Test]
    public void Appointments_outside_the_week_are_dropped()
    {
        var result = Layout(
            TestAppointment.At(Day(-1), "10:00", 1),
            TestAppointment.At(Day(7), "10:00", 1));

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void An_appointment_starting_before_the_window_is_clipped_to_it()
    {
        var result = Layout(TestAppointment.At(Day(0), "06:00", 4));

        Assert.Multiple(() =>
        {
            Assert.That(result[0].StartMinutes, Is.EqualTo(StartHour * 60));
            Assert.That(result[0].EndMinutes, Is.EqualTo(600));
        });
    }

    [Test]
    public void An_appointment_running_past_midnight_appears_only_on_the_day_it_starts()
    {
        var result = Layout(TestAppointment.At(Day(2), "22:00", 5));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].DayIndex, Is.EqualTo(2));
            Assert.That(result[0].EndMinutes, Is.EqualTo(EndHour * 60));
        });
    }

    [Test]
    public void An_appointment_entirely_outside_the_window_is_dropped()
    {
        var result = Layout(TestAppointment.At(Day(0), "02:00", 1));

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Ordering_is_deterministic_regardless_of_input_order()
    {
        // View reuse across a refresh depends on this: entry i must be the same appointment
        // every time the same data is laid out.
        ISchedulerAppointment[] appointments =
        [
            TestAppointment.At(Day(3), "14:00", 1, "d"),
            TestAppointment.At(Day(0), "09:00", 1, "a"),
            TestAppointment.At(Day(0), "11:00", 1, "b"),
            TestAppointment.At(Day(2), "10:00", 1, "c")
        ];

        var forwards = AppointmentLayoutEngine.Layout(appointments, WeekStart, 7, StartHour, EndHour);
        var backwards = AppointmentLayoutEngine.Layout(appointments.Reverse(), WeekStart, 7, StartHour, EndHour);

        Assert.That(
            backwards.Select(p => ((TestAppointment)p.Appointment).Subject),
            Is.EqualTo(forwards.Select(p => ((TestAppointment)p.Appointment).Subject)));
        Assert.That(forwards.Select(p => ((TestAppointment)p.Appointment).Subject), Is.EqualTo(new[] { "a", "b", "c", "d" }));
    }
}
