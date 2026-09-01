namespace Owlery.Maui.Scheduler.Internal;

internal enum AgendaRowKind
{
    Appointment,
    MonthSection,
    WeekSection
}

/// <summary>
/// One row of the agenda, and the only thing in this control whose height is not known up front.
/// </summary>
/// <remarks>
/// Mutable on purpose. <see cref="Height"/> starts as an estimate and is replaced by what the row
/// actually measured, and <see cref="Top"/> is then corrected for every row below it. Storing the
/// offset rather than recomputing it is what makes finding the rows on screen a binary search and
/// placing one of them a field read.
/// </remarks>
internal sealed class AgendaRow
{
    public required AgendaRowKind Kind { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>The appointment on this row, or <see langword="null"/> on a section row.</summary>
    public ISchedulerAppointment? Appointment { get; init; }

    /// <summary>Whether this row is the first of its day, and so carries the day marker beside it.</summary>
    public bool StartsDay { get; init; }

    /// <summary>How many appointments that day has, for the marker to report.</summary>
    public int DayCount { get; init; }

    public double Top { get; set; }

    public double Height { get; set; }

    /// <summary>Whether <see cref="Height"/> is what the row measured, rather than the estimate.</summary>
    public bool Measured { get; set; }

    public double Bottom => Top + Height;
}

/// <summary>The rows of one agenda, and how tall they are together.</summary>
internal sealed record AgendaPageLayout(IReadOnlyList<AgendaRow> Rows, double ContentHeight);

/// <summary>An appointment on an agenda row.</summary>
/// <remarks>
/// Carries the row rather than a copy of its position, so a placement that is already on screen
/// follows a height correction without being laid out again.
/// </remarks>
internal sealed record AgendaPlacement(ISchedulerAppointment Appointment, AgendaRow Row) : IAppointmentPlacement;

/// <summary>A heading to draw, and where.</summary>
internal sealed record AgendaSectionPlacement(SchedulerAgendaSection Section, Rect Bounds);

/// <summary>
/// Turns appointments into the flat list of rows an agenda shows.
/// </summary>
/// <remarks>
/// Three grouping rules, which are not symmetric and are not meant to be. A month heading is emitted
/// for every month in the range, empty or not, so scrolling through a quiet stretch still says where
/// you are. A week heading is emitted only when that week has something in it. A day is not a row at
/// all: it is a marker beside its first appointment, so a day costs nothing beyond the appointments
/// it holds, and an empty day costs nothing at all.
/// </remarks>
internal static class AgendaLayoutEngine
{
    public static AgendaPageLayout Layout(
        IEnumerable<ISchedulerAppointment> items,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DayOfWeek firstDayOfWeek,
        double estimatedRowHeight,
        double monthSectionHeight,
        double weekSectionHeight)
    {
        var byDay = BucketByDay(items, rangeStart, rangeEnd);

        var rows = new List<AgendaRow>();
        var top = 0.0;

        var month = default(DateOnly?);
        var week = default(DateOnly?);

        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            // The month heading comes first and comes always, even for a month holding nothing —
            // it is the one heading that keeps an empty stretch legible.
            var monthStart = new DateOnly(date.Year, date.Month, 1);
            if (month != monthStart)
            {
                month = monthStart;
                rows.Add(Section(AgendaRowKind.MonthSection, monthStart, ref top, monthSectionHeight));
            }

            if (!byDay.TryGetValue(date, out var appointments))
                continue;

            // A week heading only earns its place once something in that week does. Tracking the
            // week across the loop rather than per day is what stops a week that straddles a month
            // boundary being announced twice.
            var weekStart = StartOfWeek(date, firstDayOfWeek);
            if (week != weekStart)
            {
                week = weekStart;
                rows.Add(Section(AgendaRowKind.WeekSection, weekStart, ref top, weekSectionHeight));
            }

            for (var i = 0; i < appointments.Count; i++)
            {
                rows.Add(new AgendaRow
                {
                    Kind = AgendaRowKind.Appointment,
                    Date = date,
                    Appointment = appointments[i],
                    StartsDay = i == 0,
                    DayCount = appointments.Count,
                    Top = top,
                    Height = estimatedRowHeight
                });

                top += estimatedRowHeight;
            }
        }

        return new AgendaPageLayout(rows, top);
    }

    /// <summary>
    /// Rewrites every offset from <paramref name="from"/> down, after a row changed height.
    /// </summary>
    /// <returns>The new total height.</returns>
    public static double Reflow(IReadOnlyList<AgendaRow> rows, int from)
    {
        var top = from > 0 ? rows[from - 1].Bottom : 0;

        for (var i = from; i < rows.Count; i++)
        {
            rows[i].Top = top;
            top += rows[i].Height;
        }

        return top;
    }

    /// <summary>The first row whose bottom edge is past <paramref name="y"/>, or the row count.</summary>
    /// <remarks>
    /// Rows are ordered by <see cref="AgendaRow.Top"/> and never overlap, so the window on screen is
    /// a binary search rather than a scan of everything the range holds.
    /// </remarks>
    public static int IndexAt(IReadOnlyList<AgendaRow> rows, double y)
    {
        var low = 0;
        var high = rows.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (rows[middle].Bottom > y)
                high = middle;
            else
                low = middle + 1;
        }

        return low;
    }

    private static AgendaRow Section(AgendaRowKind kind, DateOnly date, ref double top, double height)
    {
        var row = new AgendaRow { Kind = kind, Date = date, Top = top, Height = height, Measured = true };
        top += height;

        return row;
    }

    /// <summary>
    /// Groups appointments by the day they start on, dropping anything outside the range.
    /// </summary>
    /// <remarks>
    /// Ordered the same way the month engine orders a cell — by start, then longest first — and the
    /// stability of that sort is load-bearing for the same reason: a row's index in the day is its
    /// position on screen. An appointment spanning midnight belongs to the day it starts on; the
    /// control does not draw multi-day spans on any surface.
    /// </remarks>
    private static Dictionary<DateOnly, List<ISchedulerAppointment>> BucketByDay(
        IEnumerable<ISchedulerAppointment> items,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        var byDay = new Dictionary<DateOnly, List<ISchedulerAppointment>>();

        foreach (var item in items)
        {
            var date = DateOnly.FromDateTime(item.Start);

            if (date < rangeStart || date > rangeEnd)
                continue;

            if (!byDay.TryGetValue(date, out var day))
                byDay[date] = day = [];

            day.Add(item);
        }

        // OrderBy, not List.Sort: the framework's sort is not stable, and appointments starting at
        // the same minute must keep the order the host supplied them in — a row's index in its day
        // is its position on screen, so an unstable sort would shuffle rows between refreshes.
        foreach (var date in byDay.Keys.ToList())
        {
            byDay[date] = [.. byDay[date]
                .OrderBy(static a => a.Start)
                .ThenByDescending(static a => a.End - a.Start)];
        }

        return byDay;
    }

    private static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        var offset = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;

        return date.AddDays(-offset);
    }
}
