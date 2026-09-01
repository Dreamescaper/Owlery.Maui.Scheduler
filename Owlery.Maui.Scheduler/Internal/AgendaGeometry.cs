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

    public double MonthSectionHeight { get; set; } = 56;

    public double WeekSectionHeight { get; set; } = 28;

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

    /// <summary>The first date the agenda covers for a page.</summary>
    public DateOnly RangeStart(DateOnly pageStart) => pageStart.AddDays(-LeadDays);

    /// <summary>The last date the agenda covers for a page — the anchor month, plus a tail.</summary>
    public DateOnly RangeEnd(DateOnly pageStart) => pageStart.AddMonths(1).AddDays(TrailDays - 1);

    /// <summary>Where a row sits across the surface, past the gutter its day marker occupies.</summary>
    public Rect RowBounds(AgendaRow row) => new(
        DayGutterWidth + RowInset,
        row.Top,
        Math.Max(1, ViewportWidth - DayGutterWidth - RowInset * 2),
        row.Height);

    /// <summary>A month heading reaches both edges; nothing else does.</summary>
    public Rect MonthBounds(AgendaRow row) => new(0, row.Top, Math.Max(1, ViewportWidth), row.Height);

    /// <summary>A week heading lines up with the rows beneath it rather than with the gutter.</summary>
    public Rect WeekBounds(AgendaRow row) => new(
        DayGutterWidth + RowInset,
        row.Top,
        Math.Max(1, ViewportWidth - DayGutterWidth - RowInset * 2),
        row.Height);

    /// <summary>The gutter cell beside a day's first row.</summary>
    public Rect DayMarkerBounds(AgendaRow row) => new(0, row.Top, DayGutterWidth, row.Height);
}
