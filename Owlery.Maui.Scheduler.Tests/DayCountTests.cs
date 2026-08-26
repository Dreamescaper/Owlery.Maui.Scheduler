namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class DayCountTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private static readonly DateTime Wednesday = Monday.AddDays(2);
    private const int CentreSlot = 1;

    private static TestAppointment[] OneEachDay() =>
        [.. Enumerable.Range(0, 7).Select(day => TestAppointment.At(Monday.AddDays(day), "10:00", 1, $"d{day}"))];

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(7)]
    public void A_page_shows_exactly_the_days_it_is_asked_for(int days)
    {
        var harness = new SchedulerHarness(Monday, OneEachDay(), days);

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleDatesReports.Last().VisibleDates, Has.Count.EqualTo(days));
            Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(days));
        });
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(7)]
    public void Columns_divide_the_page_between_the_visible_days(int days)
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday, "10:00", 1)], days);

        var bounds = harness.BoundsOf(harness.CentrePageAppointments.Single());

        Assert.That(bounds.Width, Is.EqualTo(SchedulerHarness.PageWidth / days).Within(3));
    }

    [TestCase(1)]
    [TestCase(3)]
    public void A_short_page_starts_on_the_display_date_rather_than_the_week(int days)
    {
        // A week has to begin on the first day of the week; three days has no such obligation, and
        // starting on the day asked for is what puts today in the leading column.
        var harness = new SchedulerHarness(Wednesday, OneEachDay(), days);

        Assert.That(harness.VisibleDatesReports.Last().VisibleDates[0], Is.EqualTo(Wednesday));
    }

    [Test]
    public void A_full_week_still_snaps_to_the_first_day_of_the_week()
    {
        var harness = new SchedulerHarness(Wednesday, OneEachDay());

        Assert.That(harness.VisibleDatesReports.Last().VisibleDates[0], Is.EqualTo(Monday));
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(7)]
    public void Swiping_moves_by_exactly_one_page(int days)
    {
        var harness = new SchedulerHarness(Monday, OneEachDay(), days);

        harness.SwipeToPage(2);

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(days)));
    }

    [TestCase(1)]
    [TestCase(3)]
    public void The_prefetch_range_covers_three_pages(int days)
    {
        var harness = new SchedulerHarness(Monday, OneEachDay(), days);

        var report = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(report.PrefetchFrom, Is.EqualTo(Monday.AddDays(-days)));
            Assert.That(report.PrefetchTo.Date, Is.EqualTo(Monday.AddDays(days * 2 - 1)));
        });
    }

    [Test]
    public void Tapping_resolves_the_right_day_in_a_three_day_view()
    {
        var harness = new SchedulerHarness(Monday, visibleDays: 3);

        harness.Tap(harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:00")));

        Assert.That(harness.CellTaps[0].Slot.Start, Is.EqualTo(Monday.AddDays(2).AddHours(10)));
    }

    [Test]
    public void Dragging_resolves_the_right_day_in_a_three_day_view()
    {
        var appointment = TestAppointment.At(Monday, "10:00", 1);
        var harness = new SchedulerHarness(Monday, [appointment], visibleDays: 3);

        var from = harness.PointAt(CentreSlot, 0, TimeSpan.Parse("10:30"));
        harness.LongPressDrag(from.X, from.Y, from.X + harness.ColumnWidth, from.Y);

        Assert.That(harness.Drops[0].DropStart, Is.EqualTo(Monday.AddDays(1).AddHours(10)));
    }

    [Test]
    public void Growing_from_three_days_to_a_week_extends_the_page_on_both_sides()
    {
        var harness = new SchedulerHarness(Wednesday, OneEachDay(), 3);

        Assert.That(harness.VisibleDatesReports.Last().VisibleDates[0], Is.EqualTo(Wednesday));

        harness.Scheduler.VisibleDays = 7;

        var dates = harness.VisibleDatesReports.Last().VisibleDates;

        Assert.Multiple(() =>
        {
            // Two days arrive before Wednesday and two after Friday, rather than four being tacked
            // on to the end. The animation is anchored to match.
            Assert.That(dates[0], Is.EqualTo(Monday));
            Assert.That(dates[^1], Is.EqualTo(Monday.AddDays(6)));
            Assert.That(dates, Does.Contain(Wednesday));
        });
    }

    [Test]
    public void The_selected_cell_marker_follows_its_column_when_the_day_count_changes()
    {
        var harness = new SchedulerHarness(Monday, visibleDays: 7);
        harness.Tap(harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:00")));

        var marker = harness.CellSelectionAffordance;

        Assert.That(marker, Is.Not.Null);
        Assert.That(harness.BoundsOf(marker!).Width, Is.EqualTo(SchedulerHarness.PageWidth / 7).Within(1));

        harness.Scheduler.VisibleDays = 3;

        // Wednesday is still Wednesday, but it is now a third of the page rather than a seventh, and
        // it has moved to the last column of a page that starts on Monday.
        var bounds = harness.BoundsOf(harness.CellSelectionAffordance!);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.EqualTo(SchedulerHarness.PageWidth / 3).Within(1));
            Assert.That(bounds.X, Is.EqualTo(SchedulerHarness.PageWidth + (2 * SchedulerHarness.PageWidth / 3)).Within(1));
        });
    }

    [Test]
    public void Changing_the_day_count_relays_out_the_page()
    {
        var harness = new SchedulerHarness(Monday, OneEachDay());

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(7));

        harness.Scheduler.VisibleDays = 3;

        Assert.Multiple(() =>
        {
            // Monday to Wednesday; the rest of the week has moved onto the following page.
            Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(3));
            Assert.That(harness.VisibleDatesReports.Last().VisibleDates, Has.Count.EqualTo(3));
        });
    }
}
