namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// What happens when the host moves the calendar itself, rather than the user swiping it.
/// </summary>
/// <remarks>
/// The interesting case is the adjacent period: it is already rendered on the page beside the one
/// showing, so it is reached by sliding onto it rather than by rebuilding all three.
/// </remarks>
[TestFixture]
public class DisplayDateTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);

    private const double Centre = SchedulerHarness.PageWidth;
    private const double Leading = 0;
    private const double Trailing = SchedulerHarness.PageWidth * 2;

    [Test]
    public void Moving_to_the_next_week_slides_onto_it()
    {
        var harness = new SchedulerHarness(Monday);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(7);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(7)));

            // Jump to where the outgoing week has landed — identical to the frame before — and then
            // animate across, so the change reads as the swipe the user did not make.
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Leading, false), (Centre, true) }));
        });
    }

    [Test]
    public void Moving_to_the_previous_week_slides_the_other_way()
    {
        var harness = new SchedulerHarness(Monday);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(-7);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(-7)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Trailing, false), (Centre, true) }));
        });
    }

    [Test]
    public void Any_day_of_the_adjacent_week_slides_to_the_start_of_it()
    {
        var harness = new SchedulerHarness(Monday);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(10);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(7)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Leading, false), (Centre, true) }));
        });
    }

    [Test]
    public void A_week_further_off_than_the_pages_either_side_is_still_rebuilt()
    {
        // Nothing between here and there was ever rendered, so there is no page to slide through and
        // an animation would only be a delay.
        var harness = new SchedulerHarness(Monday);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(21);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(21)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Centre, false) }));
        });
    }

    [Test]
    public void Sliding_onto_a_week_keeps_the_views_it_was_already_showing()
    {
        // The point of the slide, and not merely how it looks: the destination is one of the three
        // rendered pages, so its appointments are already built and bound. A rebuild discards them
        // and rents replacements for the same data.
        var next = Monday.AddDays(7);
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(next.AddDays(1), "10:00", 1)]);

        var before = harness.VisibleAppointments.Single();

        harness.Scheduler.DisplayDate = next;

        Assert.That(harness.CentrePageAppointments.Single(), Is.SameAs(before));
    }

    [Test]
    public void Sliding_asks_the_host_for_the_data_around_the_new_week()
    {
        var harness = new SchedulerHarness(Monday);
        var reportsBefore = harness.VisibleDatesReports.Count;

        harness.Scheduler.DisplayDate = Monday.AddDays(7);

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleDatesReports, Has.Count.GreaterThan(reportsBefore));
            Assert.That(harness.VisibleDatesReports.Last().VisibleDates[0].WallClock, Is.EqualTo(Monday.AddDays(7)));
        });
    }

    [Test]
    public void Swiping_carries_on_from_where_a_slide_left_the_pages()
    {
        // A rotation that left the ring buffer inconsistent would only show up on the next swipe.
        var harness = new SchedulerHarness(Monday);

        harness.Scheduler.DisplayDate = Monday.AddDays(7);
        harness.SwipeToPage(2);

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(14)));
    }

    [Test]
    public void A_three_day_page_slides_by_three_days()
    {
        // What counts as adjacent is a page, not a week. A shorter page starts on the date asked for,
        // so its neighbours are three days out rather than seven.
        var harness = new SchedulerHarness(Monday, visibleDays: 3);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(3);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(3)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Leading, false), (Centre, true) }));
        });
    }

    [Test]
    public void A_week_ahead_of_a_three_day_page_is_two_pages_off_and_is_rebuilt()
    {
        var harness = new SchedulerHarness(Monday, visibleDays: 3);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(7);

        Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Centre, false) }));
    }

    [Test]
    public void The_next_month_slides_too()
    {
        var harness = new SchedulerHarness(new DateTime(2026, 8, 15), viewMode: SchedulerViewMode.Month);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = new DateTime(2026, 9, 20);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(new DateTime(2026, 9, 1)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[]
            {
                (0d, false),
                (SchedulerHarness.ViewWidth, true)
            }));
        });
    }
}
