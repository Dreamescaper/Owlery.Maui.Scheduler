using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// How a host's <see cref="DateTime"/> is read: the kind decides, and an appointment's own zone
/// speaks only when the kind is silent.
/// </summary>
[TestFixture]
public class AppointmentTimeTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>A summer day, so both zones are on daylight time and the arithmetic is easy to read.</summary>
    private static DateTime Summer(int hour, DateTimeKind kind = DateTimeKind.Unspecified) =>
        new(2026, 8, 25, hour, 0, 0, kind);

    private sealed record Zoned(DateTime Start, DateTime End, TimeZoneInfo? Zone) : ISchedulerAppointment
    {
        public object Key => Start;

        public string? Subject => null;

        public TimeZoneInfo? TimeZone => Zone;
    }

    [Test]
    public void Utc_is_an_instant_and_is_converted()
    {
        // 14:00 UTC is 10:00 in New York in August.
        Assert.That(
            AppointmentTime.In(Summer(14, DateTimeKind.Utc), null, NewYork),
            Is.EqualTo(Summer(10)));
    }

    [Test]
    public void Utc_ignores_an_appointment_zone()
    {
        // The value already carries a complete instant, so a zone beside it is redundant. Ignoring it
        // beats guessing which of the two the host meant.
        Assert.That(
            AppointmentTime.In(Summer(14, DateTimeKind.Utc), Berlin, NewYork),
            Is.EqualTo(AppointmentTime.In(Summer(14, DateTimeKind.Utc), null, NewYork)));
    }

    [Test]
    public void Unspecified_without_a_zone_floats_and_is_never_converted()
    {
        // The zero-configuration case: 10:00 means 10:00, whatever the view is showing.
        Assert.Multiple(() =>
        {
            Assert.That(AppointmentTime.In(Summer(10), null, NewYork), Is.EqualTo(Summer(10)));
            Assert.That(AppointmentTime.In(Summer(10), null, Berlin), Is.EqualTo(Summer(10)));
            Assert.That(AppointmentTime.In(Summer(10), null, Utc), Is.EqualTo(Summer(10)));
        });
    }

    [Test]
    public void Unspecified_with_a_zone_is_wall_clock_in_that_zone()
    {
        // 10:00 in New York is 16:00 in Berlin in August.
        Assert.That(AppointmentTime.In(Summer(10), NewYork, Berlin), Is.EqualTo(Summer(16)));
    }

    [Test]
    public void Unspecified_in_the_view_s_own_zone_is_left_alone()
    {
        Assert.That(AppointmentTime.In(Summer(10), NewYork, NewYork), Is.EqualTo(Summer(10)));
    }

    [Test]
    public void Local_is_a_device_instant_and_ignores_an_appointment_zone()
    {
        var local = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Local);
        var expected = TimeZoneInfo.ConvertTime(local, TimeZoneInfo.Local, NewYork);

        Assert.Multiple(() =>
        {
            Assert.That(AppointmentTime.In(local, null, NewYork), Is.EqualTo(expected));
            Assert.That(AppointmentTime.In(local, Berlin, NewYork), Is.EqualTo(expected), "the zone is ignored");
        });
    }

    [Test]
    public void The_result_is_always_a_grid_position_not_an_instant()
    {
        // Whatever went in, what comes out is a reading on the calendar's clock.
        Assert.Multiple(() =>
        {
            Assert.That(AppointmentTime.In(Summer(14, DateTimeKind.Utc), null, NewYork).Kind, Is.EqualTo(DateTimeKind.Unspecified));
            Assert.That(AppointmentTime.In(Summer(10), NewYork, Berlin).Kind, Is.EqualTo(DateTimeKind.Unspecified));
            Assert.That(AppointmentTime.In(Summer(10), null, Utc).Kind, Is.EqualTo(DateTimeKind.Unspecified));
        });
    }

    [Test]
    public void A_time_the_zone_skipped_is_moved_forward_by_the_gap_rather_than_throwing()
    {
        // New York springs forward at 02:00 on 8 March 2026, so 02:30 never happens there. The
        // framework throws on it; a layout pass over tens of thousands of items cannot.
        var skipped = new DateTime(2026, 3, 8, 2, 30, 0);

        Assert.That(NewYork.IsInvalidTime(skipped), Is.True, "the fixture depends on this being a real gap");

        Assert.Multiple(() =>
        {
            Assert.That(() => AppointmentTime.ToUtc(skipped, NewYork), Throws.Nothing);

            // 03:30 EDT is 07:30 UTC.
            Assert.That(
                AppointmentTime.ToUtc(skipped, NewYork),
                Is.EqualTo(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public void A_time_the_zone_repeated_is_read_as_its_first_occurrence()
    {
        // New York falls back at 02:00 on 1 November 2026, so 01:30 happens twice. The framework
        // silently takes the second (standard) one; the first is what a clock shows first.
        var repeated = new DateTime(2026, 11, 1, 1, 30, 0);

        Assert.That(NewYork.IsAmbiguousTime(repeated), Is.True, "the fixture depends on this being a real overlap");

        Assert.Multiple(() =>
        {
            Assert.That(() => AppointmentTime.ToUtc(repeated, NewYork), Throws.Nothing);

            // 01:30 EDT (-4) is 05:30 UTC; the later, standard reading would be 06:30.
            Assert.That(
                AppointmentTime.ToUtc(repeated, NewYork),
                Is.EqualTo(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public void The_fast_path_agrees_with_the_conversion_it_skips()
    {
        // Unspecified in the view's own zone returns without touching the time-zone database. That is
        // an optimisation only for as long as it agrees with the round trip it is skipping — if the
        // two ever differ, the short-circuit is a bug wearing a performance argument.
        for (var hour = 0; hour < 24; hour++)
        {
            var value = new DateTime(2026, 8, 25, hour, 30, 0);
            var converted = TimeZoneInfo.ConvertTimeFromUtc(AppointmentTime.ToUtc(value, NewYork), NewYork);

            Assert.That(
                AppointmentTime.In(value, NewYork, NewYork),
                Is.EqualTo(DateTime.SpecifyKind(converted, DateTimeKind.Unspecified)),
                $"hour {hour}");
        }
    }

    [Test]
    public void An_appointment_reads_both_of_its_ends_the_same_way()
    {
        var appointment = new Zoned(Summer(10), Summer(11), NewYork);

        Assert.Multiple(() =>
        {
            Assert.That(appointment.StartIn(Berlin), Is.EqualTo(Summer(16)));
            Assert.That(appointment.EndIn(Berlin), Is.EqualTo(Summer(17)));
        });
    }

    [Test]
    public void An_appointment_that_declares_no_zone_floats()
    {
        // The default interface member, which is what every existing implementation gets.
        var appointment = new TestAppointment(Summer(10), Summer(11));

        Assert.That(appointment.StartIn(Berlin), Is.EqualTo(Summer(10)));
    }
}
