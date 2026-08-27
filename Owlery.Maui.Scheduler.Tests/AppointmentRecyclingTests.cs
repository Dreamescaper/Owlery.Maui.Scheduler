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
