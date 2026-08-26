namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Shared geometry between the control, the grid drawable and the gutter drawable.
/// A single mutable instance keeps the drawn background and the positioned appointment views
/// from ever disagreeing about where an hour line sits.
/// </summary>
internal sealed class SchedulerGeometry
{
    public const int SlotCount = 3;

    /// <summary>Width of a single rendered week, i.e. the visible page width.</summary>
    public double ViewportWidth { get; set; }

    public double HourHeight { get; set; } = 50;

    public int StartHour { get; set; } = 8;

    public int EndHour { get; set; } = 23;

    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Current wall-clock time in the control's time zone. Refreshed by the minute.</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>Week start rendered by each physical slot, left to right.</summary>
    public DateOnly[] SlotWeeks { get; } = new DateOnly[SlotCount];

    public double DayWidth => ViewportWidth / 7;

    public double SurfaceWidth => ViewportWidth * SlotCount;

    public double ContentHeight => Math.Max(0, EndHour - StartHour) * HourHeight;

    public double WindowStartMinutes => StartHour * 60.0;

    public double WindowEndMinutes => EndHour * 60.0;

    public double YFromMinutes(double minutesOfDay) => (minutesOfDay - WindowStartMinutes) / 60.0 * HourHeight;

    public double MinutesFromY(double y) => y / HourHeight * 60.0 + WindowStartMinutes;
}
