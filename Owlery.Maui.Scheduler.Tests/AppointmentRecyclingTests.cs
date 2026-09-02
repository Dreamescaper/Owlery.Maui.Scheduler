using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>Stands in for a second template, so views built from each can be told apart.</summary>
internal sealed class TestReplacementAppointmentView : ContentView;

/// <summary>
/// How appointment views are matched, reused and handed back — the cases where one can be stranded
/// on the surface belonging to no page.
/// </summary>
[TestFixture]
public class AppointmentRecyclingTests
{
    private static readonly DateTime Week = new(2026, 8, 24);

    /// <summary>An appointment whose key does not move when its times do.</summary>
    private sealed record Pinned(object Key, DateTime Start, DateTime End, string? Subject)
        : ISchedulerAppointment;

    /// <summary>A selector that would never be asked to select anything.</summary>
    private sealed class StubTemplateSelector : DataTemplateSelector
    {
        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
            => new(() => new TestAppointmentView());
    }

    [Test]
    public void A_template_selector_is_refused_where_it_is_set()
    {
        // DataTemplateSelector derives from DataTemplate, so one can be assigned and would otherwise
        // fail much later inside the pool, where a selector has no LoadTemplate to call.
        var harness = new SchedulerHarness(Week);

        var refused = Assert.Throws<NotSupportedException>(
            () => harness.Scheduler.AppointmentTemplate = new StubTemplateSelector());

        Assert.That(refused!.Message, Does.Contain("varies its own content"), "and it says what to do instead");
    }

    [Test]
    public void The_month_template_refuses_a_selector_too()
    {
        var harness = new SchedulerHarness(Week);

        Assert.Throws<NotSupportedException>(
            () => harness.Scheduler.MonthAppointmentTemplate = new StubTemplateSelector());
    }

    [Test]
    public void A_bound_view_describes_itself_for_assistive_technology()
    {
        var harness = new SchedulerHarness(Week, [TestAppointment.At(Week.AddDays(1), "10:00", 1, "Lesson")]);

        Assert.That(
            SchedulerHarness.DescriptionOf(harness.CentrePageAppointments[0]),
            Does.Contain("Lesson"));
    }

    [Test]
    public void The_description_follows_a_change_of_time_on_the_same_appointment()
    {
        // Rebuilding it is the most expensive part of a bind, so it is skipped when nothing it reads
        // has changed. This is the case that guard must not swallow: the same key — so the same view
        // is reused rather than rented afresh — carrying different times.
        var items = new ObservableCollection<ISchedulerAppointment>
        {
            new Pinned("lesson-1", Week.AddDays(1).AddHours(10), Week.AddDays(1).AddHours(11), "Lesson")
        };

        var harness = new SchedulerHarness(Week, items);
        var before = SchedulerHarness.DescriptionOf(harness.CentrePageAppointments[0]);

        items[0] = new Pinned("lesson-1", Week.AddDays(1).AddHours(14), Week.AddDays(1).AddHours(15), "Lesson");

        Assert.That(
            SchedulerHarness.DescriptionOf(harness.CentrePageAppointments[0]),
            Is.Not.EqualTo(before));
    }

    [Test]
    public void A_burst_of_collection_changes_rebuilds_once()
    {
        // A host that loads in chunks announces its collection once per chunk. Each announcement used
        // to rebuild all three pages on the spot; a single change of view was measured doing it eight
        // times over.
        var items = new ObservableCollection<ISchedulerAppointment>();
        var harness = new SchedulerHarness(Week, items);

        harness.Dispatcher.DeferDispatch = true;

        for (var day = 1; day <= 4; day++)
            items.Add(TestAppointment.At(Week.AddDays(day), "10:00", 1));

        Assert.That(harness.Dispatcher.PendingDispatches, Has.Count.EqualTo(1), "four changes, one rebuild");

        harness.Dispatcher.RunPendingDispatches();

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(4), "and it reads the final state");
    }

    [Test]
    public void A_later_change_queues_a_fresh_rebuild()
    {
        var items = new ObservableCollection<ISchedulerAppointment>();
        var harness = new SchedulerHarness(Week, items);

        harness.Dispatcher.DeferDispatch = true;

        items.Add(TestAppointment.At(Week.AddDays(1), "10:00", 1));
        harness.Dispatcher.RunPendingDispatches();

        items.Add(TestAppointment.At(Week.AddDays(2), "10:00", 1));

        Assert.That(harness.Dispatcher.PendingDispatches, Has.Count.EqualTo(1), "the gate reopens once it has run");
    }

    [Test]
    public void An_unloaded_view_stops_watching_the_collection()
    {
        // A host is told to keep one long-lived collection and change it in place, so the collection
        // outlives the view drawing it — and the subscription is a reference from the collection back
        // to the view. Held past Unloaded, it keeps a discarded view alive for as long as the host
        // holds its data.
        var items = new ObservableCollection<ISchedulerAppointment>();
        var harness = new SchedulerHarness(Week, items);

        harness.DetachFromWindow();
        harness.Dispatcher.DeferDispatch = true;

        items.Add(TestAppointment.At(Week.AddDays(1), "10:00", 1));

        Assert.That(harness.Dispatcher.PendingDispatches, Is.Empty, "nothing is listening while unloaded");
    }

    [Test]
    public void A_reloaded_view_watches_again_and_catches_up_on_what_it_missed()
    {
        // Detaching is only safe because nothing is lost by it: the collection is read afresh on
        // every repopulate, so whatever changed while the view was away arrives with the reload.
        var items = new ObservableCollection<ISchedulerAppointment>();
        var harness = new SchedulerHarness(Week, items);

        harness.DetachFromWindow();
        items.Add(TestAppointment.At(Week.AddDays(1), "10:00", 1));
        harness.ReattachToWindow();

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(1), "the change made while away");

        items.Add(TestAppointment.At(Week.AddDays(2), "12:00", 1));

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(2), "and it is watching again");
    }

    [Test]
    public void A_duplicate_key_on_one_page_does_not_strand_a_view()
    {
        // Two appointments sharing a key within a period is out of contract, but the reconciliation
        // indexed views by key and so quietly dropped whichever came first — leaving it visible,
        // owned by no page, drifting over whatever scrolled past.
        var items = new ObservableCollection<ISchedulerAppointment>
        {
            TestAppointment.At(Week.AddDays(1), "10:00", 1),
            TestAppointment.At(Week.AddDays(1), "10:00", 1)
        };

        var harness = new SchedulerHarness(Week, items);

        // Anything that re-runs the reconciliation will do.
        items.Add(TestAppointment.At(Week.AddDays(2), "12:00", 1));

        Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(3));
    }

    [Test]
    public void Changing_the_template_mid_drag_does_not_leave_the_dragged_view_behind()
    {
        // A drag takes its view out of every page, so none of the template handler's cleanup reached
        // it: not in a slot, not among the pool's spares, and RepopulateAllSlots declines to run
        // while a drag is armed. It survived into a pool that no longer matched it.
        var harness = new SchedulerHarness(Week, [TestAppointment.At(Week.AddDays(1), "10:00", 1)]);

        harness.BeginDrag(harness.PointAt(1, 1, TimeSpan.FromHours(10)));
        Assert.That(harness.DraggedAppointment, Is.Not.Null, "the drag is armed");

        harness.Scheduler.AppointmentTemplate =
            new DataTemplate(() => new TestReplacementAppointmentView());

        // Counting is not enough to catch this: without the cancel the surface also holds exactly one
        // view, because RepopulateAllSlots declines to build any while the drag is armed. What tells
        // the two apart is which template the survivor came from.
        Assert.That(
            harness.AllAppointmentViews,
            Is.Empty,
            "nothing built from the old template is left on the surface");
    }
}
