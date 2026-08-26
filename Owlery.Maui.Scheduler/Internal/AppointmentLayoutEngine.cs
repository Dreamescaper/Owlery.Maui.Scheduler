namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// One appointment resolved to grid-relative coordinates, expressed as fractions of a day column
/// so the result survives a viewport resize without recomputing overlaps.
/// </summary>
internal readonly record struct PositionedAppointment(
    ISchedulerAppointment Appointment,
    int DayIndex,
    double StartMinutes,
    double EndMinutes,
    double LeftFraction,
    double WidthFraction);

/// <summary>
/// Splits a week's appointments into per-day overlap clusters and assigns each one a column,
/// the way Google Calendar lays out colliding events.
/// </summary>
internal static class AppointmentLayoutEngine
{
    public static List<PositionedAppointment> Layout(
        IEnumerable<ISchedulerAppointment> appointments,
        DateOnly weekStart,
        int startHour,
        int endHour)
    {
        var result = new List<PositionedAppointment>();
        var dayBuckets = new List<ISchedulerAppointment>[7];

        var windowStart = startHour * 60.0;
        var windowEnd = endHour * 60.0;

        foreach (var appointment in appointments)
        {
            var dayIndex = DayIndexOf(appointment, weekStart);
            if (dayIndex < 0)
                continue;

            var day = weekStart.AddDays(dayIndex).ToDateTime(TimeOnly.MinValue);

            // Tested against the appointment's own times, before any clamping. Clamping first would
            // pull an appointment that finishes before the window opens up to the window start and
            // give it a minimum-height box there, so a 02:00 item would appear at 08:00.
            var rawStart = (appointment.Start - day).TotalMinutes;
            var rawEnd = Math.Max((appointment.End - day).TotalMinutes, rawStart + MinimumMinutes);

            if (rawEnd <= windowStart || rawStart >= windowEnd)
                continue;

            // Anything that survives is clipped to the window in LayoutDay. An appointment running
            // past midnight is clipped to its own day; multi-day rendering is out of scope.
            (dayBuckets[dayIndex] ??= []).Add(appointment);
        }

        for (var dayIndex = 0; dayIndex < 7; dayIndex++)
        {
            var bucket = dayBuckets[dayIndex];
            if (bucket is null)
                continue;

            var day = weekStart.AddDays(dayIndex).ToDateTime(TimeOnly.MinValue);

            var spans = bucket
                .Select(a =>
                {
                    var start = Math.Max((a.Start - day).TotalMinutes, windowStart);
                    var end = Math.Min(Math.Max((a.End - day).TotalMinutes, start + MinimumMinutes), windowEnd);
                    return (Appointment: a, Start: start, End: end);
                })
                .OrderBy(s => s.Start)
                .ThenByDescending(s => s.End - s.Start)
                .ToList();

            LayoutDay(spans, dayIndex, result);
        }

        return result;
    }

    /// <summary>Appointments shorter than this still get a tappable box.</summary>
    private const double MinimumMinutes = 15;

    private static void LayoutDay(
        List<(ISchedulerAppointment Appointment, double Start, double End)> spans,
        int dayIndex,
        List<PositionedAppointment> result)
    {
        var clusterStart = 0;
        var clusterEnd = double.MinValue;

        for (var i = 0; i <= spans.Count; i++)
        {
            // A gap (or the end of the day) closes the current cluster: nothing after it can overlap
            // anything inside it, so columns can be assigned independently.
            if (i == spans.Count || spans[i].Start >= clusterEnd)
            {
                FlushCluster(spans, clusterStart, i, dayIndex, result);
                clusterStart = i;
                clusterEnd = double.MinValue;
            }

            if (i < spans.Count)
                clusterEnd = Math.Max(clusterEnd, spans[i].End);
        }
    }

    private static void FlushCluster(
        List<(ISchedulerAppointment Appointment, double Start, double End)> spans,
        int from,
        int to,
        int dayIndex,
        List<PositionedAppointment> result)
    {
        if (to <= from)
            return;

        // columnEnds[c] is the end of the last appointment placed in column c.
        var columnEnds = new List<double>();
        var assignedColumn = new int[to - from];

        for (var i = from; i < to; i++)
        {
            var span = spans[i];
            var column = -1;

            for (var c = 0; c < columnEnds.Count; c++)
            {
                if (columnEnds[c] <= span.Start)
                {
                    column = c;
                    break;
                }
            }

            if (column < 0)
            {
                column = columnEnds.Count;
                columnEnds.Add(span.End);
            }
            else
            {
                columnEnds[column] = span.End;
            }

            assignedColumn[i - from] = column;
        }

        var columnCount = columnEnds.Count;
        var width = 1.0 / columnCount;

        for (var i = from; i < to; i++)
        {
            var span = spans[i];
            result.Add(new PositionedAppointment(
                span.Appointment,
                dayIndex,
                span.Start,
                span.End,
                assignedColumn[i - from] * width,
                width));
        }
    }

    private static int DayIndexOf(ISchedulerAppointment appointment, DateOnly weekStart)
    {
        var index = (DateOnly.FromDateTime(appointment.Start).DayNumber - weekStart.DayNumber);
        return index is >= 0 and < 7 ? index : -1;
    }
}
