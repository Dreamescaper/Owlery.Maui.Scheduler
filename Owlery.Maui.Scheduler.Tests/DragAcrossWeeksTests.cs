namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class DragAcrossWeeksTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private const int CentreSlot = 1;

    private static TestAppointment Wednesday10() => TestAppointment.At(Monday.AddDays(2), "10:00", 1, "session");

    private static (SchedulerHarness Harness, Point Grab) DragInProgress(bool acrossPeriods = true)
    {
        var harness = new SchedulerHarness(Monday, [Wednesday10()]);
        harness.Scheduler.AllowDragAcrossPeriods = acrossPeriods;

        var grab = harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));
        harness.BeginDrag(grab);

        return (harness, grab);
    }

    [Test]
    public void Holding_against_the_trailing_edge_pages_to_the_next_week()
    {
        var (harness, grab) = DragInProgress();

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(7)));
    }

    [Test]
    public void Holding_against_the_leading_edge_pages_to_the_previous_week()
    {
        var (harness, grab) = DragInProgress();

        harness.DragTo(harness.LeadingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(-7)));
    }

    [Test]
    public void Continuing_to_hold_keeps_walking_through_the_weeks()
    {
        var (harness, grab) = DragInProgress();

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.FireEdgePagingTimer();
        harness.FireEdgePagingTimer();

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday.AddDays(14)));
    }

    [Test]
    public void Dropping_after_paging_reschedules_into_the_week_now_on_screen()
    {
        var (harness, grab) = DragInProgress();
        var edge = harness.TrailingEdge(grab.Y);

        harness.DragTo(edge);
        harness.FireEdgePagingTimer();
        harness.Release(edge);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Drops, Has.Count.EqualTo(1));
            // Sunday of the following week, keeping the time the drag was holding.
            Assert.That(harness.Drops[0].DropStart, Is.EqualTo(Monday.AddDays(13).AddHours(10)));
        });
    }

    [Test]
    public void Moving_away_from_the_edge_stops_the_paging()
    {
        var (harness, grab) = DragInProgress();

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.DragTo(grab);
        harness.FireEdgePagingTimer();

        Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday));
    }

    [Test]
    public void Picking_an_appointment_up_leaves_a_faded_ghost_where_it_started()
    {
        var (harness, _) = DragInProgress();

        var ghost = harness.GhostAppointment;

        Assert.Multiple(() =>
        {
            Assert.That(ghost, Is.Not.Null);
            Assert.That(ghost!.Opacity, Is.EqualTo(0.5).Within(0.001));
            Assert.That(harness.BoundsOf(ghost).Y, Is.EqualTo(100).Within(0.01), "the ghost stays at the original time");
            Assert.That(harness.DraggedAppointment, Is.Not.Null, "the follower lives on the overlay");
        });
    }

    [Test]
    public void Paging_during_a_drag_does_not_duplicate_the_appointment()
    {
        var (harness, grab) = DragInProgress();

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        // Only the ghost is on the scrolling surface — the follower is on the overlay — and nothing
        // from the new week's layout: the appointment is left out of the weeks while it is dragged.
        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
            Assert.That(harness.DraggedAppointment, Is.Not.Null);
        });
    }

    [Test]
    public void Changing_week_mid_drag_slides_rather_than_swapping_in_place()
    {
        var (harness, grab) = DragInProgress();
        harness.PagerScrolls.Clear();

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        // Rotating alone would change the contents without moving anything, which is hard to read as
        // a change of week. Instead the pager jumps to where the outgoing week landed — no visible
        // change — and then animates back to the centre.
        Assert.That(harness.PagerScrolls, Is.EqualTo(new[]
        {
            (0d, false),
            (SchedulerHarness.PageWidth, true)
        }).AsCollection);
    }

    [Test]
    public void Changing_week_backwards_mid_drag_slides_the_other_way()
    {
        var (harness, grab) = DragInProgress();
        harness.PagerScrolls.Clear();

        harness.DragTo(harness.LeadingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        Assert.That(harness.PagerScrolls, Is.EqualTo(new[]
        {
            (SchedulerHarness.PageWidth * 2, false),
            (SchedulerHarness.PageWidth, true)
        }).AsCollection);
    }

    [Test]
    public void The_dragged_appointment_is_unaffected_by_the_surface_scrolling()
    {
        // The point of keeping it off the scrolling surface: no compensation, nothing to get behind.
        var (harness, _) = DragInProgress();
        var before = harness.DraggedAppointmentBounds;

        harness.ScrollPagerTo(SchedulerHarness.PageWidth / 2);
        harness.ScrollPagerTo(0);

        Assert.That(harness.DraggedAppointmentBounds, Is.EqualTo(before));
    }

    [Test]
    public void No_cell_is_resolved_while_the_pager_is_off_centre()
    {
        // The in-flight flag is cleared when an awaited scroll returns, so anything completing that
        // wait early — a stale finish, a second request abandoning the pending one — used to let a
        // column be resolved while the pages were still moving. The offset is asked directly now, so
        // an off-centre pager holds the target back on its own.
        var (harness, grab) = DragInProgress();

        var before = harness.DragTimeIndicator;
        Assert.That(before, Is.Not.Null, "a settled pager resolves a target");

        harness.ScrollPagerTo(SchedulerHarness.PageWidth * 1.5);
        harness.DragTo(new Point(grab.X, grab.Y + 60));

        Assert.That(
            harness.DragTimeIndicator,
            Is.EqualTo(before),
            "an hour later under the finger, but the pages are half on screen so nothing is resolved");
    }

    [Test]
    public void The_appointment_stays_inside_the_grid_while_a_period_change_is_sliding()
    {
        // The finger is at the edge — that is what started the paging — so without a bound the
        // follower would be carried out over the gutter and the header.
        var (harness, grab) = DragInProgress();
        var edge = harness.TrailingEdge(grab.Y);
        harness.DragTo(edge);

        harness.DeferPagerScrolls = true;
        harness.FireEdgePagingTimer();
        harness.DragTo(new Point(edge.X + 400, edge.Y + 900));

        var bounds = harness.DraggedAppointmentBounds;

        Assert.Multiple(() =>
        {
            Assert.That(bounds.X, Is.GreaterThanOrEqualTo(SchedulerHarness.GutterWidth - 0.01));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(SchedulerHarness.ViewWidth + 0.01));
            Assert.That(bounds.Y, Is.GreaterThanOrEqualTo(harness.Scheduler.HeaderHeight - 0.01));
            Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(SchedulerHarness.ViewHeight + 0.01));
        });
    }

    [Test]
    public void The_appointment_follows_the_finger_while_a_period_change_is_still_sliding()
    {
        var (harness, grab) = DragInProgress();
        var edge = harness.TrailingEdge(grab.Y);
        harness.DragTo(edge);

        var beforeSlide = harness.DraggedAppointmentBounds;

        // Hold the slide in flight, then move the finger while the pager is between pages.
        harness.DeferPagerScrolls = true;
        harness.FireEdgePagingTimer();
        harness.DragTo(new Point(edge.X, edge.Y + 50));

        Assert.That(
            harness.DraggedAppointmentBounds.Y,
            Is.EqualTo(beforeSlide.Y + 50).Within(0.01),
            "the finger's position on screen is the touch point less the live scroll offsets, which "
            + "holds whatever the pager is doing — freezing it left the appointment sitting still "
            + "while the calendar moved under it");

        harness.CompletePendingScrolls();

        Assert.That(harness.DragTimeIndicator, Is.Not.Null, "and the drop target catches up once the slide settles");
    }

    [Test]
    public void The_ghost_travels_with_the_week_it_came_from()
    {
        var (harness, grab) = DragInProgress();
        var before = harness.BoundsOf(harness.GhostAppointment!);

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        var after = harness.BoundsOf(harness.GhostAppointment!);

        Assert.That(after.X, Is.EqualTo(before.X - SchedulerHarness.PageWidth).Within(0.01));
    }

    [Test]
    public void The_ghost_goes_when_the_finger_lifts()
    {
        var (harness, grab) = DragInProgress();
        var target = new Point(grab.X, grab.Y + 100);

        harness.DragTo(target);
        harness.Release(target);

        Assert.Multiple(() =>
        {
            Assert.That(harness.GhostAppointment, Is.Null);
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Edge_paging_can_be_switched_off()
    {
        var (harness, grab) = DragInProgress(acrossPeriods: false);
        var edge = harness.TrailingEdge(grab.Y);

        harness.DragTo(edge);
        harness.FireEdgePagingTimer();
        harness.Release(edge);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday));
            // Still reschedulable, just never out of the week it started in.
            Assert.That(harness.Drops[0].DropStart, Is.EqualTo(Monday.AddDays(6).AddHours(10)));
        });
    }

    [Test]
    public void A_refused_drop_after_paging_puts_the_appointment_back_where_it_was()
    {
        var (harness, grab) = DragInProgress();
        harness.Scheduler.AppointmentDropped += (_, e) => e.Cancel = true;
        var edge = harness.TrailingEdge(grab.Y);

        harness.DragTo(edge);
        harness.FireEdgePagingTimer();
        harness.Release(edge);

        // The original week is now the leading slot, so the appointment is drawn one page to the left.
        var bounds = harness.BoundsOf(harness.VisibleAppointments.Single());

        Assert.Multiple(() =>
        {
            Assert.That(bounds.X, Is.EqualTo(2 * SchedulerHarness.DayWidth).Within(2));
            Assert.That(bounds.Y, Is.EqualTo(100).Within(0.01));
        });
    }

    [Test]
    public void Replacing_the_collection_mid_drag_does_not_duplicate_the_dragged_appointment()
    {
        var (harness, grab) = DragInProgress();

        // What a host does when edge paging asks it for the weeks it is moving through: the same
        // appointments come back as different instances.
        harness.Scheduler.ItemsSource = [Wednesday10()];

        harness.DragTo(harness.TrailingEdge(grab.Y));
        harness.FireEdgePagingTimer();

        // Still only the ghost on the surface — the replacement must not be laid out as a second copy
        // of the appointment being dragged.
        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
            Assert.That(harness.DraggedAppointment, Is.Not.Null);
        });
    }

    [Test]
    public void A_drop_reports_the_instance_the_host_is_showing_now_not_the_one_picked_up()
    {
        var (harness, grab) = DragInProgress();
        var pickedUp = harness.Scheduler.ItemsSource!.Single();

        // What a host does when edge paging asks it for the weeks it is moving through.
        var replacement = Wednesday10();
        harness.Scheduler.ItemsSource = [replacement];

        var edge = harness.TrailingEdge(grab.Y);
        harness.DragTo(edge);
        harness.FireEdgePagingTimer();
        harness.Release(edge);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Drops, Has.Count.EqualTo(1));
            // Handing back the object the drag started with would leave a handler mutating an orphan.
            Assert.That(harness.Drops[0].Appointment, Is.SameAs(replacement));
            Assert.That(harness.Drops[0].Appointment, Is.Not.SameAs(pickedUp));
            Assert.That(harness.Drops[0].DropStart, Is.EqualTo(Monday.AddDays(13).AddHours(10)));
        });
    }

    [Test]
    public void A_collection_replaced_during_a_drag_is_applied_once_the_drag_ends()
    {
        var (harness, grab) = DragInProgress();
        harness.Scheduler.AppointmentDropped += (_, e) => e.Cancel = true;

        harness.Scheduler.ItemsSource =
        [
            Wednesday10(),
            TestAppointment.At(Monday.AddDays(1), "09:00", 1, "arrived-mid-drag")
        ];

        var target = new Point(grab.X, grab.Y + 100);
        harness.DragTo(target);
        harness.Release(target);

        Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(2), "the deferred change should land when the drag finishes");
    }

    [Test]
    public void An_ordinary_drag_still_reschedules_within_the_week()
    {
        var (harness, grab) = DragInProgress();

        harness.DragTo(new Point(grab.X, grab.Y + 100));
        harness.Release(new Point(grab.X, grab.Y + 100));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(Monday));
            Assert.That(harness.Drops[0].DropStart, Is.EqualTo(Monday.AddDays(2).AddHours(12)));
        });
    }
}
