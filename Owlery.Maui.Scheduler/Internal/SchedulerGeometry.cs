namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Shared geometry between the control, the grid drawable and the gutter drawable.
/// A single mutable instance keeps the drawn background and the positioned appointment views
/// from ever disagreeing about where an hour line sits.
/// </summary>
internal sealed class SchedulerGeometry
{
    public const int SlotCount = 3;

    /// <summary>Width of a single rendered page, i.e. the visible width.</summary>
    public double ViewportWidth { get; set; }

    /// <summary>
    /// How much of the day is on screen: the control's height less the day headers.
    /// </summary>
    /// <remarks>
    /// Taken from what the control itself was allocated rather than read back off the timeline's
    /// scroll view, which reports -1 until the platform has measured it.
    /// </remarks>
    public double ViewportHeight { get; set; }

    /// <summary>How many days a page shows. Seven for a week, one for a day, and anything between.</summary>
    public int VisibleDays { get; set; } = 7;

    /// <summary>
    /// Day width to use instead of the one implied by <see cref="VisibleDays"/>, while a change of
    /// day count is being animated. Everything measures through <see cref="DayWidth"/>, so setting
    /// this widens or narrows the grid, the appointments and the headers together.
    /// </summary>
    public double? DayWidthOverride { get; set; }

    /// <summary>
    /// Horizontal shift applied to the centre page while a day count change is animating.
    /// </summary>
    /// <remarks>
    /// A new day count usually moves where the page starts — three days from Wednesday becomes a week
    /// from Monday — so without this the content would jump sideways before it began resizing. Holding
    /// the day that was already on screen in place and easing the shift to zero makes the new days
    /// grow in from whichever side they belong on.
    /// </remarks>
    public double AnimationOffsetX { get; set; }

    public double HourHeight { get; set; } = 50;

    public int StartHour { get; set; } = 8;

    public int EndHour { get; set; } = 23;

    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Current wall-clock time in the control's time zone. Refreshed by the minute.</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>First day rendered by each physical slot, left to right.</summary>
    public DateOnly[] SlotStarts { get; } = new DateOnly[SlotCount];

    public double DayWidth => DayWidthOverride ?? ViewportWidth / Math.Max(1, VisibleDays);

    public double SurfaceWidth => ViewportWidth * SlotCount;

    /// <summary>
    /// How far apart the pages sit. Normally one viewport, but while a day count change is animating
    /// the columns are not yet their final width, so the pages have to be spaced by what they
    /// currently measure or they would overlap each other.
    /// </summary>
    public double PageSpan => VisibleDays * DayWidth;

    public double ContentHeight => Math.Max(0, EndHour - StartHour) * HourHeight;

    public double WindowStartMinutes => StartHour * 60.0;

    public double WindowEndMinutes => EndHour * 60.0;

    public double YFromMinutes(double minutesOfDay) => (minutesOfDay - WindowStartMinutes) / 60.0 * HourHeight;

    public double MinutesFromY(double y) => y / HourHeight * 60.0 + WindowStartMinutes;
}
