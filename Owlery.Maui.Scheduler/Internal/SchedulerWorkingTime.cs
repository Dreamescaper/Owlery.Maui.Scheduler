namespace Owlery.Maui.Scheduler.Internal;

/// <summary>The shared working-time policy used by every scheduler surface.</summary>
/// <remarks>
/// Resolves the two ways a host can describe its week — <see cref="SchedulerView.WeeklyWorkingHours"/>, or
/// <see cref="SchedulerView.WorkingDays"/> with one shared interval — into working minutes per weekday, so
/// the drawables never need to know which one was used.
/// </remarks>
internal sealed class SchedulerWorkingTime
{
    private const double MinutesPerDay = 24 * 60;

    // Indexed by DayOfWeek. Null is a non-working day; otherwise the day's working intervals in minutes,
    // sorted and merged.
    private readonly IReadOnlyList<(double Start, double End)>?[] days;

    private SchedulerWorkingTime(IReadOnlyList<(double Start, double End)>?[] days) => this.days = days;

    public static SchedulerWorkingTime FromWeeklyHours(IEnumerable<SchedulerWorkingInterval> intervals)
    {
        var days = new IReadOnlyList<(double Start, double End)>?[7];
        foreach (var group in intervals.Where(interval => interval.Start < interval.End).GroupBy(interval => interval.Day))
            days[(int)group.Key] = Merge(group.Select(interval => (Minutes(interval.Start), Minutes(interval.End))));
        return new(days);
    }

    public static SchedulerWorkingTime FromSharedInterval(IReadOnlyCollection<DayOfWeek> workingDays, TimeOnly start, TimeOnly end)
    {
        // An empty or reversed shared interval draws no hour shading, so it reads as working all day.
        IReadOnlyList<(double Start, double End)> interval = start < end
            ? [(Minutes(start), Minutes(end))]
            : [(0, MinutesPerDay)];
        var days = new IReadOnlyList<(double Start, double End)>?[7];
        foreach (var day in workingDays)
            days[(int)day] = interval;
        return new(days);
    }

    public bool IsWorkingDay(DateOnly date) => days[(int)date.DayOfWeek] is not null;

    /// <summary>The day's working intervals in minutes, sorted and merged; empty for a non-working day.</summary>
    public IReadOnlyList<(double Start, double End)> WorkingMinutes(DateOnly date) => days[(int)date.DayOfWeek] ?? [];

    private static double Minutes(TimeOnly time) => time.ToTimeSpan().TotalMinutes;

    private static (double Start, double End)[] Merge(IEnumerable<(double Start, double End)> intervals)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var interval in intervals.OrderBy(interval => interval.Start))
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            else
                merged.Add(interval);
        }
        return [.. merged];
    }
}
