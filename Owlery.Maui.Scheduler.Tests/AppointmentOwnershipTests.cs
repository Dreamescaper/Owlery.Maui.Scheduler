namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// Every appointment view on the surface belongs to a page.
/// </summary>
/// <remarks>
/// One that does not is drawn but unreachable — see <see cref="SchedulerHarness.OrphanedAppointments"/>
/// — and nothing recovers it, so it outlives the gesture that stranded it and every swipe after.
/// These cover the ways a view leaves its page, because that is where it can be left behind.
/// </remarks>
[TestFixture]
public class AppointmentOwnershipTests
{
    private static readonly DateTime Monday = new(2026, 8, 24);
    private const int CentreSlot = 1;

    private static TestAppointment Wednesday10() => TestAppointment.At(Monday.AddDays(2), "10:00", 1, "session");

    private static SchedulerHarness Loaded() => new(Monday, [Wednesday10(), TestAppointment.At(Monday.AddDays(1), "09:00", 1, "other")]);

    private static Point Grab(SchedulerHarness harness) => harness.PointAt(CentreSlot, 2, TimeSpan.Parse("10:30"));

    [Test]
    public void Nothing_is_stranded_by_an_ordinary_drop()
    {
        var harness = Loaded();
        var grab = Grab(harness);

        harness.BeginDrag(grab);
        var target = new Point(grab.X, grab.Y + 100);
        harness.DragTo(target);
        harness.Release(target);

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_by_a_refused_drop()
    {
        var harness = Loaded();
        harness.Scheduler.AppointmentDropped += (_, e) => e.Cancel = true;
        var grab = Grab(harness);

        harness.BeginDrag(grab);
        var target = new Point(grab.X, grab.Y + 100);
        harness.DragTo(target);
        harness.Release(target);

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_by_a_refused_pick_up()
    {
        var harness = Loaded();
        harness.Scheduler.AppointmentDragStarting += (_, e) => e.Cancel = true;

        harness.BeginDrag(Grab(harness));

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_when_an_ancestor_claims_the_gesture()
    {
        var harness = Loaded();
        var grab = Grab(harness);

        harness.BeginDrag(grab);
        harness.DragTo(new Point(grab.X, grab.Y + 60));
        harness.CancelInteraction();

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_by_a_drop_into_another_week()
    {
        var harness = Loaded();
        var grab = Grab(harness);
        harness.BeginDrag(grab);

        var edge = harness.TrailingEdge(grab.Y);
        harness.DragTo(edge);
        harness.FireEdgePagingTimer();
        harness.Release(edge);

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_when_the_weeks_are_swiped_after_a_drop()
    {
        // An accepted drop leaves its view out of the pages until the host feeds the change back. A
        // host that never does is the case this is really about: the page change gives up waiting.
        var harness = Loaded();
        var grab = Grab(harness);

        harness.BeginDrag(grab);
        var target = new Point(grab.X, grab.Y + 100);
        harness.DragTo(target);
        harness.Release(target);

        harness.SwipeToPage(2);
        harness.SwipeToPage(2);

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_when_the_collection_is_replaced_during_a_drag()
    {
        var harness = Loaded();
        var grab = Grab(harness);

        harness.BeginDrag(grab);
        harness.Scheduler.ItemsSource = [Wednesday10()];
        var target = new Point(grab.X, grab.Y + 100);
        harness.DragTo(target);
        harness.Release(target);

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_when_the_view_mode_changes_during_a_drag()
    {
        var harness = Loaded();

        harness.BeginDrag(Grab(harness));
        harness.Scheduler.ViewMode = SchedulerViewMode.Month;

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }

    [Test]
    public void Nothing_is_stranded_when_the_template_changes_during_a_drag()
    {
        var harness = Loaded();

        harness.BeginDrag(Grab(harness));
        harness.Scheduler.AppointmentTemplate = new DataTemplate(() => new TestAppointmentView());

        Assert.That(harness.OrphanedAppointments, Is.Empty);
    }
}
