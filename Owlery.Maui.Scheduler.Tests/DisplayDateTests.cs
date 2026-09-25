namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// What happens when the host moves the calendar itself, rather than the user swiping it.
/// </summary>
/// <remarks>
/// Any other period is reached by sliding onto it from the side it lies on, as a swipe would. The
/// adjacent one is already rendered on the page beside the one showing; anything further off is laid
/// out there first, and its neighbours only once the slide has finished.
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
    public void A_week_further_off_than_the_pages_either_side_slides_in_from_ahead()
    {
        // One page of motion whatever the distance — the direction is the cue, not the journey.
        var harness = new SchedulerHarness(Monday);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(21);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(21)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Leading, false), (Centre, true) }));
        });
    }

    [Test]
    public void A_week_further_back_slides_in_from_behind()
    {
        var harness = new SchedulerHarness(Monday);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(-35);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(-35)));
            Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Trailing, false), (Centre, true) }));
        });
    }

    [Test]
    public void A_distant_week_is_laid_out_before_the_slide_and_its_neighbours_after_it()
    {
        var destination = Monday.AddDays(21);
        var harness = new SchedulerHarness(Monday,
        [
            TestAppointment.At(Monday.AddDays(1), "10:00", 1, "showing"),
            TestAppointment.At(destination.AddDays(-7).AddDays(1), "10:00", 1, "before"),
            TestAppointment.At(destination.AddDays(1), "10:00", 1, "destination"),
            TestAppointment.At(destination.AddDays(7).AddDays(1), "10:00", 1, "after")
        ]);
        harness.DeferPagerScrolls = true;

        harness.Scheduler.DisplayDate = destination;

        Assert.Multiple(() =>
        {
            // The page being left keeps what it was showing while it slides out.
            Assert.That(Subjects(harness.AppointmentsOnPage(0)), Is.EqualTo(new[] { "showing" }), "mid-slide, leading");
            Assert.That(Subjects(harness.AppointmentsOnPage(1)), Is.EqualTo(new[] { "destination" }), "mid-slide, centre");
            Assert.That(Subjects(harness.AppointmentsOnPage(2)), Is.Empty, "mid-slide, trailing");
        });

        harness.CompletePendingScrolls();

        Assert.Multiple(() =>
        {
            Assert.That(Subjects(harness.AppointmentsOnPage(0)), Is.EqualTo(new[] { "before" }), "settled, leading");
            Assert.That(Subjects(harness.AppointmentsOnPage(1)), Is.EqualTo(new[] { "destination" }), "settled, centre");
            Assert.That(Subjects(harness.AppointmentsOnPage(2)), Is.EqualTo(new[] { "after" }), "settled, trailing");
        });
    }

    [Test]
    public void The_pager_takes_no_swipe_until_a_slide_has_laid_out_its_neighbours()
    {
        // A swipe back mid-slide would land on the page just left, while the calendar believed it was
        // the week before the destination.
        var harness = new SchedulerHarness(Monday);
        harness.DeferPagerScrolls = true;

        harness.Scheduler.DisplayDate = Monday.AddDays(21);

        Assert.That(harness.PagerAcceptsSwipes, Is.False, "mid-slide");

        harness.CompletePendingScrolls();

        Assert.That(harness.PagerAcceptsSwipes, Is.True, "settled");
    }

    [Test]
    public void A_slide_cut_off_by_unloading_still_hands_the_pager_back()
    {
        var harness = new SchedulerHarness(Monday);
        harness.DeferPagerScrolls = true;

        harness.Scheduler.DisplayDate = Monday.AddDays(21);
        harness.DetachFromWindow();
        harness.ReattachToWindow();

        Assert.That(harness.PagerAcceptsSwipes, Is.True);
    }

    [Test]
    public void A_second_navigation_mid_slide_lays_out_the_neighbours_of_its_own_destination()
    {
        var first = Monday.AddDays(21);
        var second = Monday.AddDays(70);
        var harness = new SchedulerHarness(Monday,
        [
            TestAppointment.At(first.AddDays(1), "10:00", 1, "first"),
            TestAppointment.At(second.AddDays(-7).AddDays(1), "10:00", 1, "before"),
            TestAppointment.At(second.AddDays(1), "10:00", 1, "destination"),
            TestAppointment.At(second.AddDays(7).AddDays(1), "10:00", 1, "after")
        ]);
        harness.DeferPagerScrolls = true;

        // The first slide is moving when the second arrives and cuts its scroll short.
        harness.Scheduler.DisplayDate = first;
        harness.CompleteOldestPendingScroll();
        harness.Scheduler.DisplayDate = second;

        Assert.Multiple(() =>
        {
            // The first slide finishing must not re-lay the page the second is sliding away from.
            Assert.That(Subjects(harness.AppointmentsOnPage(0)), Is.EqualTo(new[] { "first" }), "mid-slide, leading");
            Assert.That(harness.PagerAcceptsSwipes, Is.False, "mid-slide");
        });

        harness.CompletePendingScrolls();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(second));
            Assert.That(Subjects(harness.AppointmentsOnPage(0)), Is.EqualTo(new[] { "before" }));
            Assert.That(Subjects(harness.AppointmentsOnPage(1)), Is.EqualTo(new[] { "destination" }));
            Assert.That(Subjects(harness.AppointmentsOnPage(2)), Is.EqualTo(new[] { "after" }));
            Assert.That(harness.PagerAcceptsSwipes, Is.True);
        });
    }

    [Test]
    public void Sliding_a_long_way_asks_the_host_only_for_the_data_around_the_destination()
    {
        // Mid-slide the pages either side still hold the week just left and a recycled one. Reading
        // the range off them would ask the host for everything in between.
        var destination = Monday.AddDays(364);
        var harness = new SchedulerHarness(Monday);
        harness.DeferPagerScrolls = true;

        harness.Scheduler.DisplayDate = destination;

        var report = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(report.VisibleDates[0].WallClock, Is.EqualTo(destination));
            Assert.That(report.PrefetchFrom.WallClock, Is.EqualTo(destination.AddDays(-7)));
            Assert.That(report.PrefetchTo.WallClock.Date, Is.EqualTo(destination.AddDays(13)));
        });
    }

    [Test]
    public void Swiping_carries_on_from_where_a_distant_slide_left_the_pages()
    {
        var harness = new SchedulerHarness(Monday);

        harness.Scheduler.DisplayDate = Monday.AddDays(21);
        harness.SwipeToPage(0);

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(14)));
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
    public void A_week_ahead_of_a_three_day_page_is_two_pages_off_and_still_slides()
    {
        var harness = new SchedulerHarness(Monday, visibleDays: 3);
        harness.PagerScrolls.Clear();

        harness.Scheduler.DisplayDate = Monday.AddDays(7);

        Assert.That(harness.PagerScrolls, Is.EqualTo(new[] { (Leading, false), (Centre, true) }));
    }

    [Test]
    public void A_day_page_several_days_off_is_rebuilt()
    {
        // A one-day page makes "several pages away" only a few days away, which is the distance the
        // host covers whenever it offers a way back to today.
        var harness = new SchedulerHarness(new DateTime(2026, 8, 31), visibleDays: 1);

        harness.Scheduler.DisplayDate = new DateTime(2026, 9, 3);

        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(new DateTime(2026, 9, 3)));
    }

    [Test]
    public void Today_is_reachable_after_paging_a_day_at_a_time_into_the_month_before()
    {
        // The host's Today button, exactly: page back until the calendar is in the previous month,
        // then name today and expect to land on it.
        var today = new DateTime(2026, 9, 3);
        var harness = new SchedulerHarness(today, visibleDays: 1);

        harness.SwipeToPage(0);
        harness.SwipeToPage(0);
        harness.SwipeToPage(0);

        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(new DateTime(2026, 8, 31)), "paged back");

        harness.Scheduler.DisplayDate = today;

        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(today));
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

    private static string?[] Subjects(IEnumerable<TestAppointmentView> views) =>
        [.. views.Select(view => ((TestAppointment)view.BindingContext).Subject)];
}
