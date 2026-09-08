using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// The shift a day-count change is held still by, asked the only question that matters: is the day
/// that was already on screen drawn where it already was?
/// </summary>
/// <remarks>
/// Answered in the coordinates the control places pages in — a page sits at its slot index times the
/// page span, plus the transition's shift — so the numbers here mean what they mean in
/// <c>ApplyDayWidth</c> rather than being the formula written out twice.
/// </remarks>
[TestFixture]
public class DayCountTransitionTests
{
    private const double Viewport = 700;
    private const int CentreSlot = 1;

    private static readonly DateOnly Monday = new(2026, 8, 24);
    private static readonly DateOnly Thursday = Monday.AddDays(3);
    private static readonly DateOnly Friday = Monday.AddDays(4);

    /// <summary>Where a given date is drawn while a page of <paramref name="dayCount"/> days is showing.</summary>
    private static double ColumnX(DateOnly pageStart, int dayCount, double dayWidth, DateOnly date, double shift) =>
        (CentreSlot * dayCount * dayWidth) + shift + ((date.DayNumber - pageStart.DayNumber) * dayWidth);

    [Test]
    public void Shrinking_a_week_onto_one_day_starts_with_that_day_where_it_already_was()
    {
        var transition = new DayCountTransition(Viewport, Viewport / 7, Viewport, Monday, 7);

        transition.AimAt(Friday, 1);

        Assert.That(
            ColumnX(Friday, 1, transition.DayWidth, Friday, transition.OffsetX),
            Is.EqualTo(ColumnX(Monday, 7, Viewport / 7, Friday, 0)).Within(0.01));
    }

    [Test]
    public void Growing_one_day_into_a_week_starts_with_that_day_where_it_already_was()
    {
        var transition = new DayCountTransition(Viewport, Viewport, Viewport / 7, Thursday, 1);

        transition.AimAt(Monday, 7);

        Assert.That(
            ColumnX(Monday, 7, transition.DayWidth, Thursday, transition.OffsetX),
            Is.EqualTo(ColumnX(Thursday, 1, Viewport, Thursday, 0)).Within(0.01));
    }

    [Test]
    public void Re_aiming_moves_the_anchor_to_the_page_asked_for()
    {
        // A host that opens a day writes the date and the day count one at a time, so a transition
        // aimed at Thursday is what the second write finds when the day wanted is Friday.
        var transition = new DayCountTransition(Viewport, Viewport / 7, Viewport, Monday, 7);
        transition.AimAt(Thursday, 1);

        transition.AimAt(Friday, 1);

        Assert.That(
            ColumnX(Friday, 1, transition.DayWidth, Friday, transition.OffsetX),
            Is.EqualTo(ColumnX(Monday, 7, Viewport / 7, Friday, 0)).Within(0.01));
    }

    [Test]
    public void A_page_sharing_no_day_with_the_one_it_came_from_is_not_held_still()
    {
        // Nothing on screen belongs to the new page, so there is nothing to hold in place — and
        // compensating anyway would fly a month's worth of columns past on the way in.
        var transition = new DayCountTransition(Viewport, Viewport / 7, Viewport, Monday, 7);

        transition.AimAt(Monday.AddDays(40), 1);

        Assert.That(transition.OffsetX, Is.Zero);
    }

    [Test]
    public void The_transition_settles_at_the_new_column_width_with_no_shift_left()
    {
        var transition = new DayCountTransition(Viewport, Viewport / 7, Viewport, Monday, 7);
        transition.AimAt(Friday, 1);

        transition.Progress = 1;

        Assert.Multiple(() =>
        {
            Assert.That(transition.DayWidth, Is.EqualTo(Viewport).Within(0.01));
            Assert.That(transition.OffsetX, Is.Zero);
        });
    }
}
