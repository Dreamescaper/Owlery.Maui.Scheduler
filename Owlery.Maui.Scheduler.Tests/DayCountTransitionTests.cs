using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// The shift a day-count change is held still by, asked the only question that matters: is the day
/// that was already on screen drawn where it already was?
/// </summary>
/// <remarks>
/// Answered through a real <see cref="SchedulerGeometry"/>, so a column is placed here by the same
/// <c>PageSpan</c> and <c>DayWidth</c> the control places it by in <c>ApplyDayWidth</c>, rather than
/// by the formula written out a second time and left to drift.
/// </remarks>
[TestFixture]
public class DayCountTransitionTests
{
    private const double Viewport = 700;
    private const int CentreSlot = 1;

    private static readonly DateOnly Monday = new(2026, 8, 24);
    private static readonly DateOnly Thursday = Monday.AddDays(3);
    private static readonly DateOnly Friday = Monday.AddDays(4);

    /// <summary>A week collapsing onto a single day, before it has been aimed anywhere.</summary>
    private static DayCountTransition WeekCollapsingToADay() =>
        new(Viewport / 7, Viewport, Monday, 7);

    /// <summary>A page of <paramref name="dayCount"/> days, at rest or part-way through a transition.</summary>
    private static SchedulerGeometry Page(int dayCount, DayCountTransition? transition = null) => new()
    {
        ViewportWidth = Viewport,
        VisibleDays = dayCount,
        DayWidthOverride = transition?.DayWidth,
        AnimationOffsetX = transition?.OffsetX ?? 0
    };

    /// <summary>Where a date is drawn on the centre page, in the surface's own coordinates.</summary>
    private static double ColumnX(SchedulerGeometry geometry, DateOnly pageStart, DateOnly date) =>
        (CentreSlot * geometry.PageSpan) + geometry.AnimationOffsetX
        + ((date.DayNumber - pageStart.DayNumber) * geometry.DayWidth);

    [Test]
    public void Shrinking_a_week_onto_one_day_starts_with_that_day_where_it_already_was()
    {
        var transition = WeekCollapsingToADay();

        transition.AimAt(Friday, 1);

        Assert.That(
            ColumnX(Page(1, transition), Friday, Friday),
            Is.EqualTo(ColumnX(Page(7), Monday, Friday)).Within(0.01));
    }

    [Test]
    public void Growing_one_day_into_a_week_starts_with_that_day_where_it_already_was()
    {
        var transition = new DayCountTransition(Viewport, Viewport / 7, Thursday, 1);

        transition.AimAt(Monday, 7);

        Assert.That(
            ColumnX(Page(7, transition), Monday, Thursday),
            Is.EqualTo(ColumnX(Page(1), Thursday, Thursday)).Within(0.01));
    }

    [Test]
    public void Re_aiming_moves_the_anchor_to_the_page_asked_for()
    {
        // A host that opens a day writes the date and the day count one at a time, so a transition
        // aimed at Thursday is what the second write finds when the day wanted is Friday.
        var transition = WeekCollapsingToADay();
        transition.AimAt(Thursday, 1);

        transition.AimAt(Friday, 1);

        Assert.That(
            ColumnX(Page(1, transition), Friday, Friday),
            Is.EqualTo(ColumnX(Page(7), Monday, Friday)).Within(0.01));
    }

    [Test]
    public void A_page_sharing_no_day_with_the_one_it_came_from_is_not_held_still()
    {
        // Nothing on screen belongs to the new page, so there is nothing to hold in place — and
        // compensating anyway would fly a month's worth of columns past on the way in.
        var transition = WeekCollapsingToADay();

        transition.AimAt(Monday.AddDays(40), 1);

        Assert.That(transition.OffsetX, Is.Zero);
    }

    [Test]
    public void The_transition_settles_at_the_new_column_width_with_no_shift_left()
    {
        var transition = WeekCollapsingToADay();
        transition.AimAt(Friday, 1);

        transition.Progress = 1;

        Assert.Multiple(() =>
        {
            Assert.That(transition.DayWidth, Is.EqualTo(Viewport).Within(0.01));
            Assert.That(transition.OffsetX, Is.Zero);
        });
    }
}
