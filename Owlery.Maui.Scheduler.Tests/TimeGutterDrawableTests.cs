using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class TimeGutterDrawableTests
{
    private static TimeGutterDrawable Gutter(string? at = null)
    {
        var geometry = new SchedulerGeometry { StartHour = 8, EndHour = 23, HourHeight = 50, ViewportWidth = 348 };
        var drawable = new TimeGutterDrawable(geometry);

        if (at is not null)
            drawable.HighlightMinutes = TimeSpan.Parse(at).TotalMinutes;

        return drawable;
    }

    [Test]
    public void No_hour_label_is_hidden_when_nothing_is_being_dragged()
    {
        var gutter = Gutter();

        Assert.That(gutter.LabelledHours().Any(gutter.IsHourLabelHidden), Is.False);
    }

    [Test]
    public void The_hour_label_the_indicator_sits_on_gives_way()
    {
        var gutter = Gutter("14:00");

        Assert.That(gutter.IsHourLabelHidden(14), Is.True);
    }

    [Test]
    public void A_quarter_to_the_hour_does_not_hide_the_hour_label()
    {
        // The indicator ends about six pixels above the 14:00 label at this hour height, so hiding it
        // leaves a gap in the gutter for no reason.
        var gutter = Gutter("13:45");

        Assert.That(gutter.IsHourLabelHidden(14), Is.False);
    }

    [Test]
    public void A_quarter_past_the_hour_leaves_the_label_alone_too()
    {
        // Symmetrical with the quarter-to case now that labels are centred on their own line rather
        // than hanging below it.
        var gutter = Gutter("14:15");

        Assert.Multiple(() =>
        {
            Assert.That(gutter.IsHourLabelHidden(14), Is.False);
            Assert.That(gutter.IsHourLabelHidden(15), Is.False);
        });
    }

    [Test]
    public void The_first_and_last_lines_get_no_label()
    {
        // Centred on lines that sit on the very edges of the surface, they would be cut in half.
        var gutter = Gutter();

        Assert.That(gutter.LabelledHours(), Is.EqualTo(Enumerable.Range(9, 14)).AsCollection);
    }

    [Test]
    public void A_half_hour_indicator_does_not_hide_the_hour_label()
    {
        // The two boxes graze by about a pixel here. Treating that as a collision drops an entry out
        // of the gutter for something nobody can see.
        var gutter = Gutter("12:30");

        Assert.Multiple(() =>
        {
            Assert.That(gutter.IsHourLabelHidden(12), Is.False);
            Assert.That(gutter.IsHourLabelHidden(13), Is.False);
        });
    }

    [Test]
    public void Only_the_label_actually_covered_gives_way()
    {
        var gutter = Gutter("14:00");

        Assert.Multiple(() =>
        {
            Assert.That(gutter.IsHourLabelHidden(13), Is.False);
            Assert.That(gutter.IsHourLabelHidden(15), Is.False);
        });
    }
}
