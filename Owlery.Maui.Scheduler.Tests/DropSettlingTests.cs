namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// What the calendar shows once a drag has been released.
/// </summary>
/// <remarks>
/// The control moves nothing itself: it lays out from <c>ItemsSource</c> as that reads when the
/// <c>AppointmentDropped</c> handler returns. So a host applies the move while handling the drop, and
/// a host that wants none does nothing — including the host that has nothing to do because the
/// appointment was dropped on the time it already had.
/// </remarks>
[TestFixture]
public class DropSettlingTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private const int CentreSlot = 1;

    /// <summary>Wednesday 10:00, which the timeline draws 100 units down.</summary>
    private const double TenAmY = 100;

    private const double TwoHoursDown = 100;

    private static SchedulerHarness Loaded() =>
        new(Monday, [TestAppointment.At(Monday.AddDays(2), "10:00", 1, "session")]);

    private static Point Grab(SchedulerHarness harness) => harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

    /// <summary>Applies the drop the way a host is asked to: the same appointment, starting elsewhere.</summary>
    private static void ApplyDrops(SchedulerHarness harness) =>
        harness.Scheduler.AppointmentDropped += (_, e) =>
            harness.Scheduler.ItemsSource = [((TestAppointment)e.Appointment).MovedTo(e.DropStart.WallClock)];

    [Test]
    public void A_drop_the_host_applies_is_drawn_at_its_new_time()
    {
        var harness = Loaded();
        ApplyDrops(harness);
        var grab = Grab(harness);

        harness.LongPressDrag(grab.X, grab.Y, grab.X, grab.Y + TwoHoursDown);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Drops[0].DropStart.WallClock, Is.EqualTo(Monday.AddDays(2).AddHours(12)));
            Assert.That(harness.BoundsOf(harness.VisibleAppointments.Single()).Y, Is.EqualTo(TenAmY + TwoHoursDown).Within(0.01));
            Assert.That(harness.OrphanedAppointments, Is.Empty);
        });
    }

    [Test]
    public void A_drop_the_host_does_nothing_with_is_drawn_where_it_already_was()
    {
        var harness = Loaded();
        var grab = Grab(harness);

        harness.LongPressDrag(grab.X, grab.Y, grab.X, grab.Y + TwoHoursDown);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Drops, Has.Count.EqualTo(1), "the host is still told where it was dropped");
            Assert.That(harness.BoundsOf(harness.VisibleAppointments.Single()).Y, Is.EqualTo(TenAmY).Within(0.01));
            Assert.That(harness.OrphanedAppointments, Is.Empty);
        });
    }

    [Test]
    public void An_appointment_dropped_where_it_already_was_can_be_picked_up_again()
    {
        // Returning an appointment to the time it started at gives the host nothing to apply, which is
        // the drop most likely to leave a view outside the pages — where nothing can hit-test it, so
        // the appointment stops answering the long press that would pick it up.
        var harness = Loaded();
        var grab = Grab(harness);

        harness.LongPressDrag(grab.X, grab.Y, grab.X, grab.Y);
        harness.LongPressDrag(grab.X, grab.Y, grab.X, grab.Y + TwoHoursDown);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Drops, Has.Count.EqualTo(2));
            Assert.That(harness.Drops[1].DropStart.WallClock, Is.EqualTo(Monday.AddDays(2).AddHours(12)));
        });
    }

    [Test]
    public void An_appointment_dropped_where_it_already_was_still_answers_a_tap()
    {
        var harness = Loaded();
        var grab = Grab(harness);

        harness.LongPressDrag(grab.X, grab.Y, grab.X, grab.Y);
        harness.Tap(grab);

        Assert.Multiple(() =>
        {
            Assert.That(harness.AppointmentTaps, Has.Count.EqualTo(1));
            // A press that finds no appointment lands on the grid instead, and marks the cell.
            Assert.That(harness.CellTaps, Is.Empty);
        });
    }
}
