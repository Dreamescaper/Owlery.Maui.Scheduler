using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// The hour labels down the gutter, which are real views rather than a drawn canvas.
/// </summary>
[TestFixture]
public class TimeGutterTests
{
    private static readonly DateTime Week = new(2026, 8, 24);

    [Test]
    public void The_hours_between_the_first_and_the_last_are_labelled()
    {
        // 08:00 to 23:00 leaves 09:00 through 22:00: there is no line above the first hour or below
        // the last for a label to sit against.
        var harness = new SchedulerHarness(Week);

        Assert.That(harness.HourLabels, Has.Count.EqualTo(14));
        Assert.Multiple(() =>
        {
            Assert.That(harness.HourLabels[0].Text, Is.EqualTo("09:00"));
            Assert.That(harness.HourLabels[^1].Text, Is.EqualTo("22:00"));
        });
    }

    [Test]
    public void A_label_is_centred_on_the_line_it_names()
    {
        var harness = new SchedulerHarness(Week);

        var bounds = AbsoluteLayout.GetLayoutBounds(harness.HourLabels[0]);
        var centre = bounds.Y + bounds.Height / 2;

        // 09:00 is one hour into an 08:00 window.
        Assert.That(centre, Is.EqualTo(harness.Scheduler.HourHeight).Within(0.001));
    }

    [Test]
    public void Changing_the_window_relabels_the_gutter()
    {
        var harness = new SchedulerHarness(Week);

        harness.Scheduler.StartHour = 6;
        harness.Scheduler.EndHour = 10;

        Assert.That(harness.HourLabels.Select(label => label.Text), Is.EqualTo(new[] { "07:00", "08:00", "09:00" }));
    }

    [Test]
    public void The_drag_chip_covers_the_label_it_lands_on_rather_than_hiding_it()
    {
        // The chip is opaque and the same height, so there is nothing to gain from hiding what is
        // underneath it — and an obscured-label calculation to get wrong.
        var harness = new SchedulerHarness(Week, [TestAppointment.At(Week.AddDays(1), "10:00", 1)]);

        harness.BeginDrag(harness.PointAt(1, 1, TimeSpan.FromHours(10)));
        harness.DragTo(harness.PointAt(1, 1, TimeSpan.FromHours(12)));

        Assert.That(harness.DragTimeIndicator, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(harness.HourLabels, Has.Count.EqualTo(14), "no label is removed");
            Assert.That(harness.HourLabels.All(label => label.IsVisible), "and none is hidden");
        });
    }

    [Test]
    public void The_chip_is_drawn_over_the_label_it_covers()
    {
        // Equal ZIndex paints in the order children were added, and the labels are added after the
        // chip — so without this the hour text shows through it.
        var harness = new SchedulerHarness(Week, [TestAppointment.At(Week.AddDays(1), "10:00", 1)]);

        harness.BeginDrag(harness.PointAt(1, 1, TimeSpan.FromHours(10)));
        harness.DragTo(harness.PointAt(1, 1, TimeSpan.FromHours(12)));

        Assert.That(
            harness.DragIndicatorView.ZIndex,
            Is.GreaterThan(harness.HourLabels.Max(label => label.ZIndex)));
    }

    [Test]
    public void The_chip_goes_away_when_the_drag_ends()
    {
        var harness = new SchedulerHarness(Week, [TestAppointment.At(Week.AddDays(1), "10:00", 1)]);

        harness.BeginDrag(harness.PointAt(1, 1, TimeSpan.FromHours(10)));
        harness.DragTo(harness.PointAt(1, 1, TimeSpan.FromHours(12)));
        harness.Release(harness.PointAt(1, 1, TimeSpan.FromHours(12)));

        Assert.That(harness.DragTimeIndicator, Is.Null);
    }
}
