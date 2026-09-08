namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class SchedulerViewTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private const int CentreSlot = 1;

    private static TestAppointment[] ThreeAppointments() =>
    [
        TestAppointment.At(Monday, "09:00", 1, "a"),
        TestAppointment.At(Monday.AddDays(2), "10:00", 1, "b"),
        TestAppointment.At(Monday.AddDays(4), "14:00", 2, "c")
    ];

    [Test]
    public void Lays_out_one_view_per_appointment_in_the_visible_week()
    {
        var harness = new SchedulerHarness(Monday, ThreeAppointments());

        Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(3));
    }

    [Test]
    public void Places_an_appointment_at_its_day_and_time()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);

        var bounds = harness.BoundsOf(harness.VisibleAppointments[0]);

        Assert.Multiple(() =>
        {
            // Wednesday is the third column of the centre week.
            Assert.That(bounds.X, Is.EqualTo(CentreSlot * SchedulerHarness.PageWidth + 2 * SchedulerHarness.DayWidth).Within(2));
            // 10:00 is two hours below an 08:00 window start, at 50px an hour.
            Assert.That(bounds.Y, Is.EqualTo(100).Within(0.01));
            Assert.That(bounds.Height, Is.EqualTo(50).Within(0.01));
        });
    }

    [Test]
    public void Reports_the_visible_week_and_a_three_week_prefetch_range()
    {
        var harness = new SchedulerHarness(Monday, ThreeAppointments());

        var report = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(report.VisibleDates, Has.Count.EqualTo(7));
            Assert.That(report.VisibleDates[0].WallClock, Is.EqualTo(Monday));
            Assert.That(report.VisibleDates[6].WallClock, Is.EqualTo(Monday.AddDays(6)));
            Assert.That(report.PrefetchFrom.WallClock, Is.EqualTo(Monday.AddDays(-7)));
            Assert.That(report.PrefetchTo.WallClock.Date, Is.EqualTo(Monday.AddDays(13)));
        });
    }

    [Test]
    public void Shows_the_week_containing_the_display_date()
    {
        var wednesday = Monday.AddDays(2);
        var harness = new SchedulerHarness(wednesday, ThreeAppointments());

        Assert.That(harness.VisibleDatesReports.Last().VisibleDates[0].WallClock, Is.EqualTo(Monday));
    }

    [Test]
    public void Refreshing_with_equivalent_data_keeps_every_appointment_on_its_own_view()
    {
        // Regression: releasing and re-renting made each view come back bound to a different
        // appointment, so a refresh returning identical data repainted the whole week.
        var harness = new SchedulerHarness(Monday, ThreeAppointments());
        var before = harness.VisibleAppointments.ToArray();

        harness.Scheduler.ItemsSource = ThreeAppointments();

        Assert.That(harness.VisibleAppointments, Is.EqualTo(before).AsCollection);
    }

    [Test]
    public void Refreshing_does_not_create_additional_views()
    {
        var harness = new SchedulerHarness(Monday, ThreeAppointments());

        harness.Scheduler.ItemsSource = ThreeAppointments();
        harness.Scheduler.ItemsSource = ThreeAppointments();

        Assert.That(harness.AllAppointmentViews, Has.Count.EqualTo(3));
    }

    [Test]
    public void Inserting_an_appointment_leaves_the_others_on_their_own_views()
    {
        // Positional reuse would shunt every later appointment onto a different view, repainting the
        // rest of the week for an insert at the top of it.
        var harness = new SchedulerHarness(Monday, ThreeAppointments());
        var before = harness.VisibleAppointments.ToArray();

        harness.Scheduler.ItemsSource =
        [
            TestAppointment.At(Monday, "08:00", 1, "inserted"),
            .. ThreeAppointments()
        ];

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(4));
            Assert.That(harness.VisibleAppointments, Is.SupersetOf(before));
        });
    }

    [Test]
    public void Mutating_a_subscribed_collection_repaints_without_reassigning()
    {
        // The host keeps one collection and appends to it; the control observes the change and
        // repaints rather than demanding a fresh ItemsSource.
        var appointments = new SchedulerAppointmentCollection<TestAppointment>();
        var harness = new SchedulerHarness(Monday, appointments);

        appointments.AddRange(ThreeAppointments());

        Assert.Multiple(() =>
        {
            Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(3));
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Range_adding_only_an_off_screen_month_does_not_show_it()
    {
        // The control picks out what belongs to the active surface, so the host never has to prune
        // far-off data out of its collection. Adding a month outside the loaded window repaints but
        // puts nothing on the centre page.
        var appointments = new SchedulerAppointmentCollection<TestAppointment>();
        var harness = new SchedulerHarness(Monday, appointments);

        appointments.AddRange([TestAppointment.At(Monday.AddMonths(3), "09:00", 1, "off-screen")]);

        Assert.Multiple(() =>
        {
            Assert.That(harness.CentrePageAppointments, Is.Empty);
            Assert.That(harness.VisibleAppointments, Is.Empty);
        });
    }

    [Test]
    public void Surplus_views_are_hidden_and_reused_rather_than_discarded()
    {
        var harness = new SchedulerHarness(Monday, ThreeAppointments());

        harness.Scheduler.ItemsSource = [TestAppointment.At(Monday, "09:00", 1, "a")];

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
            Assert.That(harness.AllAppointmentViews, Has.Count.EqualTo(3), "views should be pooled, not destroyed");
        });

        harness.Scheduler.ItemsSource = ThreeAppointments();

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(3));
            Assert.That(harness.AllAppointmentViews, Has.Count.EqualTo(3), "the pooled views should have been reused");
        });
    }

    [Test]
    public void A_week_swiped_after_a_drop_still_draws_it_from_the_model()
    {
        // A drop the host does not apply is drawn where the model says the moment the finger lifts —
        // see DropSettlingTests. What this adds is that the page rotation a swipe performs finds the
        // appointment in a week, rather than left over from the drag.
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1, "s")]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.LongPressDrag(grab.X, grab.Y, grab.X, grab.Y + 100);
        harness.SwipeToPage(2);

        var bounds = harness.BoundsOf(harness.VisibleAppointments.Single());

        Assert.Multiple(() =>
        {
            // Wednesday of the original week, which the swipe has made the leading page.
            Assert.That(bounds.X, Is.EqualTo(2 * SchedulerHarness.DayWidth).Within(2));
            Assert.That(bounds.Y, Is.EqualTo(100).Within(0.01));
        });
    }

    [Test]
    public void Tapping_empty_space_selects_the_slot_it_landed_in()
    {
        var harness = new SchedulerHarness(Monday);

        harness.Tap(harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:07")));

        Assert.Multiple(() =>
        {
            Assert.That(harness.CellTaps, Has.Count.EqualTo(1));
            // Rounded down into the containing 15-minute slot.
            Assert.That(harness.CellTaps[0].Slot.Start.WallClock, Is.EqualTo(Monday.AddDays(2).AddHours(10)));
            Assert.That(harness.Scheduler.SelectedSlot?.Start.WallClock, Is.EqualTo(Monday.AddDays(2).AddHours(10)));
        });
    }

    [Test]
    public void Tapping_an_appointment_reports_it_and_does_not_select_a_cell()
    {
        var appointment = TestAppointment.At(Monday.AddDays(2), "10:00", 1);
        var harness = new SchedulerHarness(Monday, [appointment]);

        harness.Tap(harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30")));

        Assert.Multiple(() =>
        {
            Assert.That(harness.AppointmentTaps, Has.Count.EqualTo(1));
            Assert.That(harness.AppointmentTaps[0].Appointment, Is.SameAs(appointment));
            Assert.That(harness.CellTaps, Is.Empty);
        });
    }

    [Test]
    public void Holding_then_dragging_reschedules_to_the_snapped_time()
    {
        var appointment = TestAppointment.At(Monday.AddDays(2), "10:00", 1);
        var harness = new SchedulerHarness(Monday, [appointment]);

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.LongPressDrag(from.X, from.Y, from.X, from.Y + 100);

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragStarts, Has.Count.EqualTo(1));
            Assert.That(harness.Drops, Has.Count.EqualTo(1));
            Assert.That(harness.Drops[0].DropStart.WallClock, Is.EqualTo(Monday.AddDays(2).AddHours(12)));
        });
    }

    [Test]
    public void Dragging_reports_each_boundary_it_crosses_once()
    {
        // Quarter-hour snapping at the default hour height is a boundary every 12.5dp. The drag below
        // travels 25 — two boundaries — in four steps, so a report per movement would give four.
        var appointment = TestAppointment.At(Monday.AddDays(2), "10:00", 1);
        var harness = new SchedulerHarness(Monday, [appointment]);
        harness.Scheduler.DragSnapMinutes = 15;

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.BeginDrag(from);

        Assert.That(harness.DropTargetChanges, Is.Empty, "picking it up has not moved it");

        foreach (var step in new[] { 6.0, 12.5, 19.0, 25.0 })
            harness.DragTo(new Point(from.X, from.Y + step));

        Assert.Multiple(() =>
        {
            Assert.That(harness.DropTargetChanges.Select(snap => snap.DropStart.WallClock), Is.EqualTo(new[]
            {
                Monday.AddDays(2).AddHours(10).AddMinutes(15),
                Monday.AddDays(2).AddHours(10).AddMinutes(30)
            }).AsCollection);
            Assert.That(harness.DropTargetChanges[0].Appointment, Is.SameAs(appointment));
        });
    }

    [Test]
    public void The_selection_marker_is_exactly_as_tall_as_the_slot()
    {
        // A quarter hour at the default hour height is 12.5dp — below the 18dp floor a short
        // appointment gets so it stays tappable. The marker is not tappable and must not claim time
        // that is not selected, so it takes the slot's own height however small that is.
        var harness = new SchedulerHarness(Monday);
        harness.Scheduler.SlotMinutes = 15;

        harness.Tap(harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:05")));

        var bounds = harness.BoundsOf(harness.CellSelectionAffordance!);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Height, Is.EqualTo(harness.Scheduler.HourHeight / 4).Within(0.01));
            Assert.That(bounds.Y, Is.EqualTo(harness.GutterYAt(TimeSpan.Parse("10:00"))).Within(0.01));
        });
    }

    [Test]
    public void A_drop_and_a_selection_use_their_own_intervals()
    {
        // Quarter-hour drops, whole-hour selection. The distances are picked so each would land
        // somewhere different under the other's interval: 110 minutes down is 11:50, which is 11:45
        // to the nearest quarter but 12:00 to the nearest hour; a tap at 10:50 floors to 10:00 on the
        // hour but would give 10:45 on the quarter.
        var appointment = TestAppointment.At(Monday.AddDays(2), "10:00", 1);
        var harness = new SchedulerHarness(Monday, [appointment]);
        harness.Scheduler.DragSnapMinutes = 15;
        harness.Scheduler.SlotMinutes = 60;

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        var oneHundredAndTenMinutes = 110.0 / 60 * harness.Scheduler.HourHeight;
        harness.LongPressDrag(from.X, from.Y, from.X, from.Y + oneHundredAndTenMinutes);

        harness.Tap(harness.PointAt(CentreSlot, 4, TimeSpan.Parse("10:50")));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Drops[0].DropStart.WallClock, Is.EqualTo(Monday.AddDays(2).AddHours(11).AddMinutes(45)));
            Assert.That(harness.Scheduler.SelectedSlot?.Start.WallClock, Is.EqualTo(Monday.AddDays(4).AddHours(10)));
            Assert.That(harness.Scheduler.SelectedSlot?.Duration, Is.EqualTo(TimeSpan.FromHours(1)));
        });
    }

    [Test]
    public void Dragging_sideways_moves_the_appointment_to_another_day()
    {
        var appointment = TestAppointment.At(Monday.AddDays(2), "10:00", 1);
        var harness = new SchedulerHarness(Monday, [appointment]);

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.LongPressDrag(from.X, from.Y, from.X + SchedulerHarness.DayWidth, from.Y);

        Assert.That(harness.Drops[0].DropStart.WallClock, Is.EqualTo(Monday.AddDays(3).AddHours(10)));
    }

    [Test]
    public void Dragging_shows_the_proposed_time_in_the_gutter()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(new Point(grab.X, grab.Y + 100));

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragTimeIndicator, Is.EqualTo("12:00"));
            // Level with the line the appointment would start on, clear of the finger.
            Assert.That(harness.DragTimeIndicatorMinutes, Is.EqualTo(12 * 60));
        });
    }

    [Test]
    public void The_gutter_time_indicator_follows_the_drag()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(new Point(grab.X, grab.Y + 50));

        Assert.That(harness.DragTimeIndicator, Is.EqualTo("11:00"));

        harness.DragTo(new Point(grab.X, grab.Y + 175));

        Assert.That(harness.DragTimeIndicator, Is.EqualTo("13:30"));
    }

    [Test]
    public void The_gutter_time_indicator_can_be_switched_off()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        harness.Scheduler.ShowDragTimeIndicator = false;
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(new Point(grab.X, grab.Y + 100));

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragTimeIndicator, Is.Null);
            // Still reschedulable — only the readout is gone.
            harness.Release(new Point(grab.X, grab.Y + 100));
            Assert.That(harness.Drops, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void The_gutter_time_indicator_clears_when_the_drag_ends()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        var target = new Point(grab.X, grab.Y + 100);

        harness.BeginDrag(grab);
        harness.DragTo(target);
        harness.Release(target);

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragTimeIndicator, Is.Null);
            Assert.That(harness.DragTimeIndicatorMinutes, Is.Null);
        });
    }

    [Test]
    public void The_timeline_has_a_viewport_shorter_than_the_day()
    {
        // What makes scrolling while dragging meaningful in the first place.
        var harness = new SchedulerHarness(Monday);

        Assert.That(harness.TimelineViewportHeight, Is.GreaterThan(0));
        Assert.That(harness.TimelineViewportHeight, Is.LessThan((23 - 8) * 50));
    }

    [Test]
    public void Dragging_against_the_bottom_edge_scrolls_towards_later_hours()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(harness.BottomEdge(grab.X));
        harness.FireEdgeScrollTimer();

        Assert.That(harness.TimelineScrollY, Is.GreaterThan(0));
    }

    [Test]
    public void Scrolling_while_dragging_carries_the_appointment_with_it()
    {
        // The finger does not move, but the hours under it do, so the time on offer has to follow.
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(harness.BottomEdge(grab.X));

        var before = harness.DragTimeIndicator;
        harness.FireEdgeScrollTimer();
        harness.FireEdgeScrollTimer();

        Assert.That(harness.DragTimeIndicator, Is.Not.EqualTo(before));
    }

    [Test]
    public void Moving_away_from_the_edge_stops_the_scrolling()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(harness.BottomEdge(grab.X));
        harness.FireEdgeScrollTimer();

        var reached = harness.TimelineScrollY;

        harness.DragTo(grab);
        harness.FireEdgeScrollTimer();

        Assert.That(harness.TimelineScrollY, Is.EqualTo(reached));
    }

    [Test]
    public void Scrolling_stops_at_the_end_of_the_day()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

        harness.BeginDrag(grab);
        harness.DragTo(harness.BottomEdge(grab.X));

        for (var i = 0; i < 50; i++)
            harness.FireEdgeScrollTimer();

        var furthest = ((23 - 8) * 50) - harness.TimelineViewportHeight;

        Assert.That(harness.TimelineScrollY, Is.EqualTo(furthest).Within(1));
    }

    [Test]
    public void Moving_without_holding_is_a_scroll_and_never_starts_a_drag()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.PressAndMove(from.X, from.Y, from.X, from.Y + 100);

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragStarts, Is.Empty);
            Assert.That(harness.Drops, Is.Empty);
            Assert.That(harness.AppointmentTaps, Is.Empty, "a gesture that moved is not a tap");
        });
    }

    [Test]
    public void A_refused_drag_never_reports_a_drop()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        harness.Scheduler.AppointmentDragStarting += (_, e) => e.Cancel = true;

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.LongPressDrag(from.X, from.Y, from.X, from.Y + 100);

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragStarts, Has.Count.EqualTo(1));
            Assert.That(harness.Drops, Is.Empty);
        });
    }

    [Test]
    public void Drag_and_drop_can_be_switched_off()
    {
        var harness = new SchedulerHarness(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1)]);
        harness.Scheduler.AllowDragAndDrop = false;

        var from = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.LongPressDrag(from.X, from.Y, from.X, from.Y + 100);

        Assert.That(harness.DragStarts, Is.Empty);
    }

    [Test]
    public void Swiping_forward_advances_the_week_and_asks_for_new_data()
    {
        var harness = new SchedulerHarness(Monday, ThreeAppointments());
        var reportsBefore = harness.VisibleDatesReports.Count;

        harness.SwipeToPage(2);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(7)));
            Assert.That(harness.VisibleDatesReports, Has.Count.GreaterThan(reportsBefore));
            Assert.That(harness.VisibleDatesReports.Last().VisibleDates[0].WallClock, Is.EqualTo(Monday.AddDays(7)));
        });
    }

    [Test]
    public void Swiping_backward_returns_to_the_previous_week()
    {
        var harness = new SchedulerHarness(Monday, ThreeAppointments());

        harness.SwipeToPage(0);

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(-7)));
    }
}
