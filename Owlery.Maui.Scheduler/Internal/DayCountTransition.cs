namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// A day count growing or shrinking into place: the column width reached so far, and the shift that
/// holds what was already on screen still while it does.
/// </summary>
/// <remarks>
/// Pages are spaced by whatever they currently measure, so changing the day count moves where the
/// centre page sits — three days from Wednesday becomes a week from Monday — and without a
/// compensating shift the calendar would slide sideways before it began changing width. Easing that
/// shift to zero is what makes the new days grow in from the side they belong on.
/// <para>
/// The page is aimed at rather than fixed when the transition is built. A host that changes the day
/// count and the date together writes the two properties one at a time, so whichever lands second
/// finds a transition already running for the page the other one implied. Re-aiming corrects it
/// where it stands, which restarting could not: the columns would snap back to the width they
/// started from.
/// </para>
/// </remarks>
internal sealed class DayCountTransition(
    double viewportWidth,
    double fromDayWidth,
    double toDayWidth,
    DateOnly fromPageStart,
    int fromDayCount)
{
    private double startOffset;

    /// <summary>How far through the transition the columns are, already eased.</summary>
    public double Progress { get; set; }

    /// <summary>The column width at the progress reached so far.</summary>
    public double DayWidth => fromDayWidth + ((toDayWidth - fromDayWidth) * Progress);

    /// <summary>How much of the shift is still owed at the progress reached so far.</summary>
    public double OffsetX => startOffset * (1 - Progress);

    /// <summary>
    /// Points the transition at the page it is settling on.
    /// </summary>
    /// <remarks>
    /// The shift puts the day that was already on screen back where it was, so the columns either
    /// side of it grow in from the edge they belong on — Monday and Tuesday from the left, Saturday
    /// and Sunday from the right — instead of the whole page sliding sideways first. It has to undo
    /// both the page-span the centre slot already sits at and the columns preceding the anchored day,
    /// measured in the width the columns are coming *from*.
    /// <para>
    /// Only when the two pages share a day. With nothing on screen to hold still there is nothing to
    /// compensate for, and compensating anyway would fly the calendar in from wherever the new page
    /// would have been — which for a date a month away is most of a year of columns.
    /// </para>
    /// </remarks>
    public void AimAt(DateOnly toPageStart, int toDayCount)
    {
        var shiftedDays = fromPageStart.DayNumber - toPageStart.DayNumber;

        startOffset = Overlaps(toPageStart, toDayCount)
            ? viewportWidth - ((toDayCount + shiftedDays) * fromDayWidth)
            : 0;
    }

    private bool Overlaps(DateOnly toPageStart, int toDayCount) =>
        toPageStart.DayNumber < fromPageStart.DayNumber + fromDayCount
        && fromPageStart.DayNumber < toPageStart.DayNumber + toDayCount;
}
