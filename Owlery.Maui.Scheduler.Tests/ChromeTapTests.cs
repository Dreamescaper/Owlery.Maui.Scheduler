namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// Taps on the chrome around the grid — the day headers and the hour gutter.
/// </summary>
/// <remarks>
/// Both resolve a position by the same arithmetic the grid uses, so the cases worth covering are the
/// ones where that arithmetic could disagree with itself: a column on a page other than the centre,
/// a page narrower than seven days, and the edges of the day window.
/// </remarks>
[TestFixture]
public class ChromeTapTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private const int CentreSlot = 1;

    [Test]
    public void Tapping_a_day_header_reports_that_day()
    {
        var harness = new SchedulerHarness(Monday);

        harness.TapHeader(CentreSlot, 2);

        Assert.That(harness.HeaderTaps.Single().Date, Is.EqualTo(Monday.AddDays(2)));
    }

    [Test]
    public void A_header_reports_midnight_rather_than_a_time_of_day()
    {
        // The event names a day. Anything else would be inventing an hour the user did not touch.
        var harness = new SchedulerHarness(Monday);

        harness.TapHeader(CentreSlot, 4);

        Assert.That(harness.HeaderTaps.Single().Date.TimeOfDay, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void A_header_on_the_page_alongside_reports_that_page_s_day()
    {
        // The strip carries all three pages, so resolving only against the centre would report a day
        // of the wrong week for anything half-scrolled into view.
        var harness = new SchedulerHarness(Monday);

        harness.TapHeader(2, 0);

        Assert.That(harness.HeaderTaps.Single().Date, Is.EqualTo(Monday.AddDays(7)));
    }

    [Test]
    public void Header_columns_follow_the_day_count()
    {
        // On a three-day page the third column is the third day, not the third seventh of a week.
        var harness = new SchedulerHarness(Monday, visibleDays: 3);

        harness.TapHeader(CentreSlot, 2);

        Assert.That(harness.HeaderTaps.Single().Date, Is.EqualTo(Monday.AddDays(2)));
    }

    [Test]
    public void Headers_follow_the_week_after_a_swipe()
    {
        var harness = new SchedulerHarness(Monday);

        harness.SwipeToPage(2);
        harness.TapHeader(CentreSlot, 0);

        Assert.That(harness.HeaderTaps.Single().Date, Is.EqualTo(Monday.AddDays(7)));
    }

    [Test]
    public void A_month_header_reports_nothing()
    {
        // Its columns name weekdays that recur down six rows, so no one date is being pointed at.
        //
        // Switched into a month rather than opened in one, and that is the whole test. A control that
        // began in a month has no timeline geometry, so the column arithmetic answers nothing and the
        // event stays silent whether or not anything checks the mode — the guard would look tested
        // while doing nothing. Arriving from a week leaves that geometry populated and the pages
        // holding month starts, so without the check a header tap reports a date computed by counting
        // days across a grid that is not laid out in days.
        var harness = new SchedulerHarness(Monday);
        harness.Scheduler.ViewMode = SchedulerViewMode.Month;

        harness.TapHeader(CentreSlot, 2);

        Assert.That(harness.HeaderTaps, Is.Empty);
    }

    [Test]
    public void Tapping_the_gutter_reports_the_time_there()
    {
        var harness = new SchedulerHarness(Monday);

        harness.TapGutter(TimeSpan.Parse("14:00"));

        Assert.That(harness.GutterTaps.Single().Time, Is.EqualTo(TimeSpan.Parse("14:00")));
    }

    [Test]
    public void A_gutter_tap_snaps_down_like_a_cell_tap()
    {
        // Down, not to the nearest: what a tap selects begins at or before the point touched. 14:12 is
        // past the halfway mark of its interval, so rounding would give 14:15 and flooring gives
        // 14:00 — a time such as 14:37 lands on 14:30 either way and would not tell them apart.
        var harness = new SchedulerHarness(Monday);

        harness.TapGutter(TimeSpan.Parse("14:12"));

        Assert.That(harness.GutterTaps.Single().Time, Is.EqualTo(TimeSpan.Parse("14:00")));
    }

    [Test]
    public void A_gutter_tap_honours_the_slot_length()
    {
        // 14:20 is chosen because it lands differently under each interval — it floors to 14:15 at the
        // default fifteen and to 14:00 at thirty. A time such as 14:37 gives 14:30 either way and
        // would pass whether or not the interval was read at all.
        var harness = new SchedulerHarness(Monday);
        harness.Scheduler.SlotMinutes = 30;

        harness.TapGutter(TimeSpan.Parse("14:20"));

        Assert.That(harness.GutterTaps.Single().Time, Is.EqualTo(TimeSpan.Parse("14:00")));
    }

    [Test]
    public void A_gutter_tap_above_the_window_reports_its_start()
    {
        var harness = new SchedulerHarness(Monday);

        harness.TapGutterAt(-40);

        Assert.That(
            harness.GutterTaps.Single().Time,
            Is.EqualTo(TimeSpan.FromHours(harness.Scheduler.StartHour)));
    }

    [Test]
    public void A_gutter_tap_below_the_window_stops_a_slot_short_of_its_end()
    {
        // The last slot has to begin before the window ends, or it would name a time off the grid.
        var harness = new SchedulerHarness(Monday);

        harness.TapGutterAt(SchedulerHarness.ViewHeight * 4);

        Assert.That(
            harness.GutterTaps.Single().Time,
            Is.EqualTo(TimeSpan.FromHours(harness.Scheduler.EndHour) - TimeSpan.FromMinutes(15)));
    }
}
