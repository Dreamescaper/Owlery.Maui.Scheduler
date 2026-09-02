namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Geometry for the agenda surface: one column of rows, as tall as the appointments in range make it.
/// </summary>
/// <remarks>
/// The odd one of the three. A timeline measures its height in hours and a month is exactly one
/// viewport, so both can state their height as a formula; an agenda's is a function of the data and
/// is not known until the rows have been laid out — and changes again as they measure. It is
/// therefore written in rather than derived, and <see cref="PageGeometry.ContentHeight"/> is floored
/// at the viewport so an empty range still fills the screen instead of collapsing.
/// <para>
/// It also does not page. <see cref="SurfaceWidth"/> is one viewport rather than three and
/// <see cref="PageSpan"/> is zero, so the three ring-buffer slots collapse onto each other and only
/// the middle one is ever populated.
/// </para>
/// </remarks>
internal sealed class AgendaGeometry : PageGeometry
{
    private double contentHeight;

    /// <summary>Width of the leading column the day markers sit in.</summary>
    public double DayGutterWidth { get; set; } = 56;

    /// <summary>Space either side of a row, so it does not touch the edges.</summary>
    public const double RowInset = 2;

    /// <summary>What a row is assumed to be worth before it has measured.</summary>
    public double EstimatedRowHeight { get; set; } = 64;

    /// <summary>
    /// The least a row may be, however little it measures.
    /// </summary>
    /// <remarks>
    /// The agenda's counterpart to <see cref="TimelineSurface.MinimumAppointmentHeight"/>, and there
    /// for the same reason: a row that collapsed to nothing would be invisible and impossible to
    /// touch. It matters more here, because a row's height comes from a host's template rather than
    /// from arithmetic — a template that renders nothing while its data loads would otherwise take
    /// the whole list down with it.
    /// </remarks>
    public double MinimumRowHeight { get; set; } = 24;

    public double MonthSectionHeight { get; set; } = 56;

    public double WeekSectionHeight { get; set; } = 28;

    /// <summary>
    /// Whitespace between one day's rows and the next, in place of a dividing line.
    /// </summary>
    /// <remarks>
    /// Days read as groups by the gap between them rather than by a drawn separator. The gap is owned
    /// by the day that precedes it, so a day that never empties and the one that follows are separated
    /// by this much, and an empty day costs nothing.
    /// </remarks>
    public double DayGap { get; set; } = 8;

    /// <summary>Days shown before the anchor page, and after the month it names.</summary>
    public int LeadDays { get; set; } = 7;

    public int TrailDays { get; set; } = 7;

    /// <summary>Where the vertical scroll currently sits, which decides what is worth realizing.</summary>
    public double VisibleTop { get; set; }

    /// <summary>
    /// How much beyond the viewport is kept realized, as a fraction of it.
    /// </summary>
    /// <remarks>
    /// Rows are built before they are needed so that a fling meets views that already exist. Half a
    /// screen each way is the starting point; it is the dial to turn if a platform turns out to
    /// report scrolling only once it has stopped.
    /// </remarks>
    public double OverscanFactor { get; set; } = 0.5;

    public double Overscan => ViewportHeight * OverscanFactor;

    public double WindowTop => Math.Max(0, VisibleTop - Overscan);

    public double WindowBottom => VisibleTop + ViewportHeight + Overscan;

    /// <summary>An agenda is as tall as its rows, and never shorter than the screen.</summary>
    public override double ContentHeight => Math.Max(contentHeight, ViewportHeight);

    /// <summary>Nothing pages sideways, so every page sits at the same offset.</summary>
    public override double PageSpan => 0;

    /// <summary>One viewport, not three: there are no neighbouring pages to hold.</summary>
    public override double SurfaceWidth => ViewportWidth;

    public void SetContentHeight(double height) => contentHeight = Math.Max(0, height);

    /// <summary>The first day of the whole month containing a date.</summary>
    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The last day of the whole month containing a date.</summary>
    public static DateOnly LastOfMonth(DateOnly date) => FirstOfMonth(date).AddMonths(1).AddDays(-1);

    /// <summary>
    /// The first date the agenda covers for a page — the first of the whole month at the range's edge.
    /// </summary>
    /// <remarks>
    /// Always a whole month, never a mid-month slice. A partial month would show its tail under a
    /// heading and then, when the host answered, the whole month under the same heading — the same
    /// month header with different events. Rounding the edge up to the month keeps every month whole.
    /// </remarks>
    public DateOnly RangeStart(DateOnly pageStart) => FirstOfMonth(pageStart.AddDays(-LeadDays));

    /// <summary>The last date the agenda covers — the last day of the whole month at the range's edge.</summary>
    public DateOnly RangeEnd(DateOnly pageStart) => LastOfMonth(pageStart.AddMonths(1).AddDays(TrailDays - 1));

    /// <summary>
    /// How wide anything in the list column is: the viewport, less the gutter and the insets.
    /// </summary>
    /// <remarks>
    /// Named rather than inlined because it is also the width a row is measured at, and a change to
    /// it is the one thing that makes a cached measurement wrong.
    /// </remarks>
    public double RowWidth => Math.Max(1, ViewportWidth - DayGutterWidth - RowInset * 2);

    /// <summary>
    /// Where anything in the list column sits — an appointment row and both headings alike.
    /// </summary>
    /// <remarks>
    /// One rectangle for all three because they share a column: a month heading is inset past the
    /// gutter exactly as its rows are, so the heading and the rows beneath it line up on the left.
    /// The day marker is the only thing that is different, and it is what the column is inset past.
    /// </remarks>
    public Rect RowBounds(AgendaRow row) => new(DayGutterWidth + RowInset, row.Top, RowWidth, row.Height);

    /// <summary>The gutter cell beside a day's first row.</summary>
    public Rect DayMarkerBounds(AgendaRow row) => new(0, row.Top, DayGutterWidth, row.Height);
}
