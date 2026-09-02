namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// The zone rules seen through the control: where an appointment is drawn, and what the control
/// reports back.
/// </summary>
[TestFixture]
public class SchedulerTimeZoneTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>Tuesday, 14:00 UTC — 10:00 in New York, since August is daylight time there.</summary>
    private static TestAppointment Instant() => new(
        new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 25, 15, 0, 0, DateTimeKind.Utc),
        "instant");

    /// <summary>Tuesday, 10:00, floating.</summary>
    private static TestAppointment Floating() => new(
        new DateTime(2026, 8, 25, 10, 0, 0),
        new DateTime(2026, 8, 25, 11, 0, 0),
        "floating");

    private static string? Description(SchedulerHarness harness, string subject) =>
        SchedulerHarness.DescriptionOf(
            harness.CentrePageAppointments.Single(view =>
                SchedulerHarness.DescriptionOf(view)?.StartsWith(subject) == true));

    [Test]
    public void A_Utc_appointment_is_drawn_where_its_instant_falls_in_the_view_s_zone()
    {
        var harness = new SchedulerHarness(
            Monday, [Instant()], configure: view => view.TimeZone = NewYork);

        Assert.That(Description(harness, "instant"), Does.Contain("10:00 - 11:00"));
    }

    [Test]
    public void A_floating_appointment_is_drawn_where_it_says_whatever_the_view_s_zone_is()
    {
        var newYork = new SchedulerHarness(
            Monday, [Floating()], configure: view => view.TimeZone = NewYork);
        var utc = new SchedulerHarness(
            Monday, [Floating()], configure: view => view.TimeZone = Utc);

        Assert.Multiple(() =>
        {
            Assert.That(Description(newYork, "floating"), Does.Contain("10:00 - 11:00"));
            Assert.That(Description(utc, "floating"), Does.Contain("10:00 - 11:00"));
        });
    }

    [Test]
    public void Changing_the_view_s_zone_moves_the_instant_and_leaves_the_floating_one_alone()
    {
        // The distinction the whole model rests on: one of these is a moment in time and the other is
        // a reading on a clock, and only the first of them depends on which clock.
        var harness = new SchedulerHarness(
            Monday, [Instant(), Floating()], configure: view => view.TimeZone = NewYork);

        Assert.Multiple(() =>
        {
            Assert.That(Description(harness, "instant"), Does.Contain("10:00 - 11:00"));
            Assert.That(Description(harness, "floating"), Does.Contain("10:00 - 11:00"));
        });

        harness.Scheduler.TimeZone = Utc;

        Assert.Multiple(() =>
        {
            Assert.That(Description(harness, "instant"), Does.Contain("14:00 - 15:00"), "four hours later");
            Assert.That(Description(harness, "floating"), Does.Contain("10:00 - 11:00"), "unmoved");
        });
    }

    [Test]
    public void A_reported_moment_carries_the_view_s_zone_and_resolves_to_an_instant()
    {
        var harness = new SchedulerHarness(
            Monday, configure: view => view.TimeZone = NewYork);

        harness.Tap(harness.PointAt(1, dayIndex: 1, TimeSpan.FromHours(10)));

        var start = harness.CellTaps.Single().Slot.Start;

        Assert.Multiple(() =>
        {
            // The wall-clock host reads the grid position and never meets a zone.
            Assert.That(start.WallClock, Is.EqualTo(new DateTime(2026, 8, 25, 10, 0, 0)));
            Assert.That(start.WallClock.Kind, Is.EqualTo(DateTimeKind.Unspecified));

            // The instant host asks, and gets one — 10:00 in New York is 14:00 UTC in August.
            Assert.That(start.Zone, Is.EqualTo(NewYork));
            Assert.That(start.ToDateTimeUtc(), Is.EqualTo(new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc)));
            Assert.That(start.ToDateTimeUtc().Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void A_moment_resolves_a_reading_its_own_zone_never_had()
    {
        // The grid draws a uniform day, so it can offer 02:30 on a morning New York skipped from
        // 02:00 to 03:00. Resolving it moves forward by the gap rather than throwing — the same rule
        // appointments coming the other way are read by.
        var moment = new SchedulerMoment(new DateTime(2026, 3, 8, 2, 30, 0), NewYork);

        Assert.Multiple(() =>
        {
            Assert.That(() => moment.ToDateTimeUtc(), Throws.Nothing);
            Assert.That(moment.ToDateTimeUtc(), Is.EqualTo(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public void A_moment_in_the_view_s_zone_round_trips_to_local_time()
    {
        var moment = new SchedulerMoment(new DateTime(2026, 8, 25, 10, 0, 0), NewYork);

        Assert.That(moment.ToDateTimeLocal(), Is.EqualTo(moment.ToDateTimeUtc().ToLocalTime()));
    }
}
