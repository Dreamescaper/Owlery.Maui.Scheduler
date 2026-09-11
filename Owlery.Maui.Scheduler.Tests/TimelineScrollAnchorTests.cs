namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// Where the timeline opens vertically, and what it takes for the platform to accept that offset.
/// </summary>
/// <remarks>
/// The three surfaces share one vertical scroll view. A month's content is exactly one viewport
/// tall, so while one is showing the timeline's offset is clamped away and returning to it would
/// otherwise arrive at the top of the day window — and the offset written on the way back is asked
/// for against a content size the platform has not resized yet, which is the same clamp the pager
/// hit leaving an agenda.
/// </remarks>
[TestFixture]
public class TimelineScrollAnchorTests
{
    private static readonly DateTime Monday = new(2026, 8, 31);

    private const double HourHeight = 50;

    private const int CentreSlot = 1;

    private static SchedulerHarness WholeDay(TimeOnly? opening = null) =>
        new(Monday, configure: scheduler =>
        {
            scheduler.StartHour = 0;
            scheduler.EndHour = 24;
            scheduler.InitialScrollTime = opening;
        });

    /// <summary>The furthest down a whole day of hours can be scrolled at the harness's size.</summary>
    private static double Furthest(SchedulerHarness harness) =>
        (24 * HourHeight) - harness.TimelineViewportHeight;

    [Test]
    public void The_timeline_opens_at_the_time_it_was_given()
    {
        var harness = WholeDay(new TimeOnly(8, 0));

        // The load that happens inside the constructor is before the harness stands in for the
        // platform scroll view, so the round trip off the window and back is what puts an observable
        // offset through it. A host navigating away and back does exactly this.
        harness.DetachFromWindow();
        harness.ReattachToWindow();

        Assert.That(harness.TimelineScrollY, Is.EqualTo(8 * HourHeight).Within(0.5));
    }

    [Test]
    public void Returning_from_a_month_opens_at_that_time_again()
    {
        var harness = WholeDay(new TimeOnly(8, 0));

        harness.ScrollVerticallyTo(0);
        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.That(harness.TimelineScrollY, Is.EqualTo(8 * HourHeight).Within(0.5));
    }

    [Test]
    public void Returning_from_an_agenda_opens_at_that_time_again()
    {
        var harness = WholeDay(new TimeOnly(8, 0));

        harness.Scheduler.ViewMode = SchedulerViewMode.Agenda;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.That(harness.TimelineScrollY, Is.EqualTo(8 * HourHeight).Within(0.5));
    }

    [Test]
    public void An_opening_time_the_hours_below_cannot_fill_settles_at_the_end_of_the_day()
    {
        // 22:00 is 1100 points down a 1200-point day: less than a viewport of hours follows it, so
        // the top of the screen can only reach the last offset the content has.
        var harness = WholeDay(new TimeOnly(22, 0));

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.That(harness.TimelineScrollY, Is.EqualTo(Furthest(harness)).Within(0.5));
    }

    [Test]
    public void An_opening_time_before_the_day_window_opens_at_the_start_of_it()
    {
        var harness = new SchedulerHarness(Monday, configure: scheduler =>
        {
            scheduler.StartHour = 8;
            scheduler.EndHour = 23;
            scheduler.InitialScrollTime = new TimeOnly(6, 0);
        });

        harness.ScrollVerticallyTo(200);
        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.That(harness.TimelineScrollY, Is.Zero);
    }

    [Test]
    public void An_offset_the_platform_clamped_away_is_asked_for_again()
    {
        var harness = WholeDay(new TimeOnly(8, 0));

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;

        // What a platform scroll view does with an offset past the content it still measures: it
        // takes the request, applies nothing, and reports nothing further.
        harness.DeferVerticalScrollRequests = true;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.That(harness.TimelineScrollY, Is.Zero, "the request should have been clamped away");
        Assert.That(harness.LastVerticalScrollRequest, Is.EqualTo(8 * HourHeight).Within(0.5));

        // The layout pass that resizes the content has landed, so the same offset now fits.
        harness.DeferVerticalScrollRequests = false;
        harness.FireTimelineScrollRetryTimer();

        Assert.That(harness.TimelineScrollY, Is.EqualTo(8 * HourHeight).Within(0.5));
    }

    [Test]
    public void An_offset_that_never_fits_is_given_up_on()
    {
        var harness = WholeDay(new TimeOnly(8, 0));

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.DeferVerticalScrollRequests = true;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        for (var i = 0; i < 10; i++)
            harness.FireTimelineScrollRetryTimer();

        // Nothing is owed any more, so a scroll view that would now accept the offset is not asked
        // for it: the reader is left where the platform put them rather than pulled back later.
        harness.DeferVerticalScrollRequests = false;
        harness.FireTimelineScrollRetryTimer();

        Assert.That(harness.TimelineScrollY, Is.Zero);
    }

    [Test]
    public void Without_an_opening_time_the_timeline_opens_near_the_current_time()
    {
        // NAV-18, on the way back rather than only on load: the default anchor is an hour before now.
        var harness = new SchedulerHarness(Monday, configure: scheduler =>
        {
            scheduler.StartHour = 0;
            scheduler.EndHour = 24;
        });

        harness.ScrollVerticallyTo(0);
        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        var expected = Math.Clamp(
            (DateTime.Now.TimeOfDay - TimeSpan.FromHours(1)).TotalHours * HourHeight,
            0,
            Furthest(harness));

        Assert.That(harness.TimelineScrollY, Is.EqualTo(expected).Within(HourHeight));
    }

    [Test]
    public void Scrolling_by_dragging_takes_over_from_an_offset_still_owed()
    {
        var harness = new SchedulerHarness(
            Monday,
            [TestAppointment.At(Monday.AddDays(2), "10:00", 1)],
            configure: scheduler =>
            {
                scheduler.StartHour = 0;
                scheduler.EndHour = 24;
                scheduler.InitialScrollTime = new TimeOnly(8, 0);
            });

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.DeferVerticalScrollRequests = true;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;
        harness.DeferVerticalScrollRequests = false;

        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(harness.BottomEdge(grab.X));
        harness.FireEdgeScrollTimer();

        var reached = harness.TimelineScrollY;
        harness.FireTimelineScrollRetryTimer();

        Assert.That(harness.TimelineScrollY, Is.EqualTo(reached).Within(0.5),
            "the opening offset must not pull the surface back from under a drag");
    }

    [Test]
    public void A_gesture_after_the_offset_arrives_is_left_alone()
    {
        var harness = WholeDay(new TimeOnly(8, 0));

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        harness.ScrollVerticallyTo(120);
        harness.FireTimelineScrollRetryTimer();

        Assert.That(harness.TimelineScrollY, Is.EqualTo(120).Within(0.5));
    }
}
