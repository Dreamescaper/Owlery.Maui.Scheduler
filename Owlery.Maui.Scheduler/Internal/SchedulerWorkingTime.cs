namespace Owlery.Maui.Scheduler.Internal;

/// <summary>The shared working-time policy used by every scheduler surface.</summary>
internal static class SchedulerWorkingTime
{
    public static bool IsWorkingDay(DateOnly date, IReadOnlyCollection<DayOfWeek> workingDays) =>
        workingDays.Contains(date.DayOfWeek);

    public static bool HasSameDayInterval(TimeOnly start, TimeOnly end) => start < end;

    public static double Minutes(TimeOnly time) => time.ToTimeSpan().TotalMinutes;
}
