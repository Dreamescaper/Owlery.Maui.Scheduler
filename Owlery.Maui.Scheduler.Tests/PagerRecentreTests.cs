using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// Putting the pager back on the centre page after the surface underneath it has changed width.
/// </summary>
/// <remarks>
/// Leaving an agenda is the case that broke. An agenda's surface is one viewport wide and a
/// timeline's is three, so the recentre onto the middle page is asked for while the platform scroll
/// view still measures the agenda — and a platform scroll view clamps an offset its content cannot
/// reach, silently and unrecoverably.
/// </remarks>
[TestFixture]
public class PagerRecentreTests
{
    private static readonly DateTime Thursday = new(2026, 9, 3);

    /// <summary>
    /// The half that actually broke: whether an offset may be written yet.
    /// </summary>
    /// <remarks>
    /// The control-side test below cannot catch this. There is no handler in the headless host, so
    /// the harness applies whatever offset is asked for and no clamp is ever modelled — which is why
    /// the arithmetic is a type of its own rather than a condition inside the iOS handler.
    /// </remarks>
    [TestCase(348d, 400d, 1200d, true, TestName = "Fits when the content spans three pages")]
    [TestCase(348d, 400d, 400d, false, TestName = "Does not fit while the content is still one page")]
    [TestCase(0d, 400d, 400d, true, TestName = "The first page fits a single-page surface")]
    [TestCase(348d, 0d, 1200d, false, TestName = "Does not fit before the view has a frame")]
    [TestCase(348d, 400d, 748d, true, TestName = "Fits exactly at the last reachable offset")]
    [TestCase(348d, 400d, 747d, false, TestName = "Does not fit one point short of it")]
    public void An_offset_fits_only_once_the_content_reaches_it(
        double offsetX,
        double viewportWidth,
        double contentWidth,
        bool expected)
    {
        Assert.That(PagingOffset.Fits(offsetX, viewportWidth, contentWidth), Is.EqualTo(expected));
    }

    /// <summary>
    /// The cross-platform half asks for the centre of the surface it is changing *to*.
    /// </summary>
    /// <remarks>
    /// A guard against the other way this could break: the gutter appears with the timeline, so the
    /// viewport is narrower than the agenda's and reading the width off the wrong geometry would ask
    /// for a whole gutter too far. It passed before the fix as well — the control was always asking
    /// for the right offset, and the platform was throwing it away.
    /// </remarks>
    [Test]
    public void Leaving_the_agenda_recentres_on_a_whole_page()
    {
        var harness = new SchedulerHarness(
            Thursday,
            viewMode: SchedulerViewMode.Agenda,
            visibleDays: 1);

        harness.PagerScrolls.Clear();

        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.That(harness.PagerScrolls, Is.Not.Empty, "Changing surface should have recentred the pager.");

        var (scrollX, _) = harness.PagerScrolls[^1];

        Assert.That(scrollX, Is.EqualTo(SchedulerHarness.PageWidth),
            "The pager must come to rest on a whole page, not between two.");
    }
}
