namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Geometry for the timeline surface, where the vertical axis is time.
/// </summary>
internal sealed class SchedulerGeometry : PageGeometry
{
    /// <summary>How many days a page shows. Seven for a week, one for a day, and anything between.</summary>
    public int VisibleDays { get; set; } = 7;

    /// <summary>
    /// Day width to use instead of the one implied by <see cref="VisibleDays"/>, while a change of
    /// day count is being animated. Everything measures through <see cref="DayWidth"/>, so setting
    /// this widens or narrows the grid, the appointments and the headers together.
    /// </summary>
    public double? DayWidthOverride { get; set; }

    public double HourHeight { get; set; } = 50;

    public int StartHour { get; set; } = 8;

    public int EndHour { get; set; } = 23;

    public double DayWidth => DayWidthOverride ?? ViewportWidth / Math.Max(1, VisibleDays);

    /// <summary>
    /// How far apart the pages sit. Normally one viewport, but while a day count change is animating
    /// the columns are not yet their final width, so the pages have to be spaced by what they
    /// currently measure or they would overlap each other.
    /// </summary>
    public override double PageSpan => VisibleDays * DayWidth;

    public override double ContentHeight => Math.Max(0, EndHour - StartHour) * HourHeight;

    public double WindowStartMinutes => StartHour * 60.0;

    public double WindowEndMinutes => EndHour * 60.0;

    public double YFromMinutes(double minutesOfDay) => (minutesOfDay - WindowStartMinutes) / 60.0 * HourHeight;

    public double MinutesFromY(double y) => y / HourHeight * 60.0 + WindowStartMinutes;
}
