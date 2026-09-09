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
    /// How much of a page is on screen: the control's content height less the header.
    /// </summary>
    /// <remarks>
    /// Read back off the vertical scroll view, which is exactly that height. It reports -1 until the
    /// platform has measured it, so the control falls back to its own allocation less its padding
    /// and the nominal header height — see <c>SchedulerView.BodyHeight</c>. What the control was
    /// allocated is not the answer on its own: padding and a platform inset both take height from
    /// the content without taking it from the control.
    /// </remarks>
    public double ViewportHeight { get; set; }

    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Current wall-clock time in the control's time zone. Refreshed by the minute.</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>
    /// The zone the calendar is displayed in: the axis appointments are placed on, and the clock
    /// behind <see cref="Now"/>.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>First day rendered by each physical slot, left to right.</summary>
    public DateOnly[] SlotStarts { get; } = new DateOnly[SlotCount];

    /// <summary>
    /// Horizontal shift applied to the pages while a transition is animating.
    /// </summary>
    /// <remarks>
    /// A transition usually moves where the centre page starts — three days from Wednesday becomes a
    /// week from Monday — so without this the content would jump sideways before it began changing.
    /// Holding what was already on screen in place and easing the shift to zero makes the new content
    /// grow in from whichever side it belongs on. Zero at rest, and on a surface that never animates.
    /// </remarks>
    public double AnimationOffsetX { get; set; }

    /// <summary>How wide the shared drawing surface is.</summary>
    /// <remarks>
    /// Three viewports for a surface that pages, because all three live side by side on one canvas.
    /// A surface that does not page overrides this to one, and its pages collapse onto each other.
    /// </remarks>
    public virtual double SurfaceWidth => ViewportWidth * SlotCount;

    /// <summary>How far apart the pages sit.</summary>
    public abstract double PageSpan { get; }

    /// <summary>Height of one page's scrollable content.</summary>
    public abstract double ContentHeight { get; }
}
