namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Geometry for the month surface: a fixed grid of six week rows by seven day columns, sized to fit
/// the viewport exactly.
/// </summary>
/// <remarks>
/// The row count is fixed rather than the five or six a given month actually needs. A varying count
/// would make <see cref="PageGeometry.ContentHeight"/> depend on which page you are looking at, and
/// all three slots share one surface and one canvas — so the neighbours either side would be drawn
/// at the centre page's row height. The cost is that a 28-day February beginning on
/// <see cref="PageGeometry.FirstDayOfWeek"/> shows two full rows of the following month.
/// </remarks>
internal sealed class MonthGeometry : PageGeometry
{
    public const int Columns = 7;

    public const int Rows = 6;

    public const int CellCount = Columns * Rows;

    /// <summary>Height reserved at the top of a cell for its day number.</summary>
    public const double DayNumberHeight = 18;

    /// <summary>Height of one appointment chip.</summary>
    public const double ChipHeight = 16;

    /// <summary>Space between chips, and between the last chip and the bottom of its cell.</summary>
    public const double ChipGap = 2;

    /// <summary>Space left either side of a chip, so it does not touch the cell's borders.</summary>
    public const double ChipInset = 2;

    /// <summary>A month is never taller than the viewport — it does not scroll.</summary>
    public override double ContentHeight => ViewportHeight;

    public override double PageSpan => ViewportWidth;

    public double CellWidth => ViewportWidth / Columns;

    public double CellHeight => ContentHeight / Rows;

    /// <summary>
    /// How many chips fit in a cell beneath its day number.
    /// </summary>
    /// <remarks>
    /// A day with more than this many appointments gives its last line over to a "+N more" marker,
    /// so the number of appointments actually shown is one less. See <see cref="MonthLayoutEngine"/>.
    /// </remarks>
    public int LinesPerCell => Math.Max(0, (int)((CellHeight - DayNumberHeight) / (ChipHeight + ChipGap)));

    /// <summary>
    /// The date in the top-left cell of the grid for a month.
    /// </summary>
    /// <remarks>
    /// A month page is identified by its first day, but the grid opens on whichever
    /// <see cref="PageGeometry.FirstDayOfWeek"/> falls on or before it — so the leading cells belong
    /// to the previous month and are drawn muted.
    /// </remarks>
    public DateOnly GridStart(DateOnly monthStart)
    {
        var first = new DateOnly(monthStart.Year, monthStart.Month, 1);
        var diff = ((int)first.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;

        return first.AddDays(-diff);
    }

    /// <summary>Which of the 42 cells a date occupies on a month's page, or -1 when it is not on it.</summary>
    public int CellIndexOf(DateOnly monthStart, DateOnly date)
    {
        var index = date.DayNumber - GridStart(monthStart).DayNumber;

        return index >= 0 && index < CellCount ? index : -1;
    }

    public Rect CellBounds(int cellIndex) =>
        new(cellIndex % Columns * CellWidth, cellIndex / Columns * CellHeight, CellWidth, CellHeight);
}
