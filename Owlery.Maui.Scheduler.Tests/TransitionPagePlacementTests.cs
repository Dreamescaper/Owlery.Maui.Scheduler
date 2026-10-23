using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// Which pages a day-count transition re-places frame by frame. The question that matters is that a
/// page left alone is never one on screen, in either direction — skipping is invisible only then.
/// </summary>
/// <remarks>
/// The frames are produced by a real <see cref="DayCountTransition"/> over a real
/// <see cref="SchedulerGeometry"/>, the way <c>ChangeVisibleDays</c> drives them, so the pages sit
/// where the control would put them rather than where a formula restated here says.
/// </remarks>
[TestFixture]
public class TransitionPagePlacementTests
{
    private const double Viewport = 700;

    // The pager rests on the centre page.
    private const double ScrollX = Viewport;

    private static readonly DateOnly Monday = new(2026, 8, 24);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);

    /// <summary>The geometry at each frame of a transition, from its first to its last.</summary>
    private static IEnumerable<SchedulerGeometry> Frames(
        double fromDayWidth, double toDayWidth, DateOnly fromPage, int fromDays, DateOnly toPage, int toDays)
    {
        var transition = new DayCountTransition(fromDayWidth, toDayWidth, fromPage, fromDays);
        transition.AimAt(toPage, toDays);

        for (var frame = 0; frame <= 20; frame++)
        {
            transition.Progress = frame / 20.0;

            yield return new SchedulerGeometry
            {
                ViewportWidth = Viewport,
                VisibleDays = toDays,
                DayWidthOverride = transition.DayWidth,
                AnimationOffsetX = transition.OffsetX
            };
        }
    }

    private static IEnumerable<SchedulerGeometry> DayGrowingIntoItsWeek() =>
        Frames(Viewport, Viewport / 7, Wednesday, 1, Monday, 7);

    private static IEnumerable<SchedulerGeometry> WeekShrinkingOntoWednesday() =>
        Frames(Viewport / 7, Viewport, Monday, 7, Wednesday, 1);

    [Test]
    public void Growing_a_day_into_a_week_never_brings_the_neighbouring_weeks_on_screen()
    {
        var frames = DayGrowingIntoItsWeek().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frames.All(g => TransitionPagePlacement.IsOnScreen(g, 1, ScrollX)), Is.True);
            Assert.That(frames.Any(g => TransitionPagePlacement.IsOnScreen(g, 0, ScrollX)), Is.False);
            Assert.That(frames.Any(g => TransitionPagePlacement.IsOnScreen(g, 2, ScrollX)), Is.False);
        });
    }

    /// <summary>
    /// The direction that rules out skipping the side pages wholesale: the new pages are a day wide,
    /// so Tuesday and Thursday sit beside Wednesday as it widens, and leave the screen only on the
    /// last frame.
    /// </summary>
    [Test]
    public void Shrinking_a_week_onto_a_day_keeps_the_neighbouring_days_on_screen_until_the_last_frame()
    {
        var frames = WeekShrinkingOntoWednesday().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frames[..^1].All(g => TransitionPagePlacement.IsOnScreen(g, 0, ScrollX)), Is.True);
            Assert.That(frames[..^1].All(g => TransitionPagePlacement.IsOnScreen(g, 2, ScrollX)), Is.True);
            Assert.That(TransitionPagePlacement.IsOnScreen(frames[^1], 0, ScrollX), Is.False);
            Assert.That(TransitionPagePlacement.IsOnScreen(frames[^1], 2, ScrollX), Is.False);
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void A_page_left_alone_is_never_one_that_was_last_placed_on_screen(bool shrinking, bool skipsAny)
    {
        var placement = new TransitionPagePlacement();
        var lastPlacedOnScreen = new bool?[PageGeometry.SlotCount];
        var skipped = 0;

        // What ChangeVisibleDays does: the rebuild before the first frame placed every page at rest.
        placement.MovedAll();

        foreach (var geometry in shrinking ? WeekShrinkingOntoWednesday() : DayGrowingIntoItsWeek())
        {
            for (var slot = 0; slot < PageGeometry.SlotCount; slot++)
            {
                var onScreen = TransitionPagePlacement.IsOnScreen(geometry, slot, ScrollX);

                if (placement.ShouldPlace(slot, onScreen))
                {
                    lastPlacedOnScreen[slot] = onScreen;
                    continue;
                }

                skipped++;
                Assert.That(onScreen, Is.False, $"slot {slot} skipped while on screen");
                Assert.That(lastPlacedOnScreen[slot], Is.False, $"slot {slot} skipped where it was left on screen");
            }
        }

        // Growing, proof that the run skips anything at all rather than passing by placing every
        // page; shrinking, that nothing on screen is mistaken for something that can be left.
        Assert.That(skipped > 0, Is.EqualTo(skipsAny));
    }

    [Test]
    public void A_page_that_leaves_the_screen_is_placed_once_more_and_then_left_alone()
    {
        var placement = new TransitionPagePlacement();

        Assert.Multiple(() =>
        {
            Assert.That(placement.ShouldPlace(0, onScreen: true), Is.True);
            Assert.That(placement.ShouldPlace(0, onScreen: false), Is.True, "moved off screen");
            Assert.That(placement.ShouldPlace(0, onScreen: false), Is.False);
        });
    }

    [Test]
    public void A_page_moved_by_something_else_is_placed_again_even_off_screen()
    {
        var placement = new TransitionPagePlacement();

        placement.ShouldPlace(2, onScreen: false);
        placement.Moved(2);

        Assert.That(placement.ShouldPlace(2, onScreen: false), Is.True);
    }
}
