namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// The measurements every kind of page shares, whatever it happens to contain.
/// </summary>
/// <remarks>
/// A single mutable instance is handed to the control, its surface and its drawable, so the painted
/// background and the positioned views can never disagree about where anything sits.
/// <para>
/// Only what is genuinely common lives here. How tall a page's content is and how far apart pages
/// sit are both left abstract: a timeline measures its height in hours and can be mid-animation
/// between two column widths, whereas a month is exactly one viewport in each direction.
/// </para>
/// </remarks>
internal abstract class PageGeometry
{
    public const int SlotCount = 3;

    /// <summary>Width of a single rendered page, i.e. the visible width.</summary>
    public double ViewportWidth { get; set; }

    /// <summary>
    /// How much of a page is on screen: the control's height less the header.
    /// </summary>
    /// <remarks>
    /// Taken from what the control itself was allocated rather than read back off a scroll view,
    /// which reports -1 until the platform has measured it.
    /// </remarks>
    public double ViewportHeight { get; set; }

    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Current wall-clock time in the control's time zone. Refreshed by the minute.</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>First day rendered by each physical slot, left to right.</summary>
    public DateOnly[] SlotStarts { get; } = new DateOnly[SlotCount];

    public double SurfaceWidth => ViewportWidth * SlotCount;

    /// <summary>How far apart the pages sit.</summary>
    public abstract double PageSpan { get; }

    /// <summary>Height of one page's scrollable content.</summary>
    public abstract double ContentHeight { get; }
}
