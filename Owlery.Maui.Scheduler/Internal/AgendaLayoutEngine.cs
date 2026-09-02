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

    /// <summary>This row's position in the table, so a correction can find it without a search.</summary>
    public int Index { get; set; }

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
        double weekSectionHeight,
        double dayGap,
        TimeZoneInfo view)
    {
        var byDay = BucketByDay(items, rangeStart, rangeEnd, view);

        var rows = new List<AgendaRow>();
        var top = 0.0;
        var gapPending = false;

        var month = default(DateOnly?);
        var week = default(DateOnly?);

        void Add(AgendaRow row)
        {
            // A new group following a day is owed the space between them. Consumed here, before the
            // first row of the next day's content — heading or appointment —, so it is never added
            // twice and empty days in between cost nothing.
            if (gapPending)
            {
                top += dayGap;
                gapPending = false;
            }

            row.Top = top;
            rows.Add(row);
            top += row.Height;
        }

        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            // The month heading comes first and comes always, even for a month holding nothing —
            // it is the one heading that keeps an empty stretch legible.
            var monthStart = new DateOnly(date.Year, date.Month, 1);
            if (month != monthStart)
            {
                month = monthStart;
                Add(new AgendaRow
                {
                    Kind = AgendaRowKind.MonthSection,
                    Date = monthStart,
                    DayCount = CountAppointments(byDay, monthStart, monthStart.AddMonths(1).AddDays(-1)),
                    Height = monthSectionHeight,
                    Measured = true
                });
            }

            if (!byDay.TryGetValue(date, out var appointments))
                continue;

            // A week heading describes a whole week, so only a week lying entirely inside the loaded
            // range earns one. A week straddling the range's edge has days the control has not loaded
            // — Monday and Tuesday before the range, say — and would advertise them, then have the
            // content shift up when they arrive. Tracking the week across the loop rather than per day
            // is what stops a week that straddles a month boundary being announced twice. (A week is
            // still skipped when it has nothing in it, by the check above.)
            var weekStart = StartOfWeek(date, firstDayOfWeek);
            if (week != weekStart)
            {
                week = weekStart;

                if (weekStart >= rangeStart && weekStart.AddDays(6) <= rangeEnd)
                {
                    Add(new AgendaRow
                    {
                        Kind = AgendaRowKind.WeekSection,
                        Date = weekStart,
                        DayCount = CountAppointments(byDay, weekStart, weekStart.AddDays(6)),
                        Height = weekSectionHeight,
                        Measured = true
                    });
                }
            }

            for (var i = 0; i < appointments.Count; i++)
            {
                Add(new AgendaRow
                {
                    Kind = AgendaRowKind.Appointment,
                    Date = date,
                    Appointment = appointments[i],
                    StartsDay = i == 0,
                    DayCount = appointments.Count,
                    Height = estimatedRowHeight
                });
            }

            // The day's rows are done; the next day's group is owed this much space between them.
            gapPending = true;
        }

        for (var i = 0; i < rows.Count; i++)
            rows[i].Index = i;

        return new AgendaPageLayout(rows, top);
    }

    /// <summary>
    /// Rewrites every offset from <paramref name="from"/> down, after a row changed height.
    /// </summary>
    /// <returns>The new total height.</returns>
    public static double Reflow(IReadOnlyList<AgendaRow> rows, int from, double dayGap)
    {
        var top = from > 0 ? rows[from - 1].Bottom : 0;

        // The row before the reflow point may have closed a day, and the gap that followed it is
        // still owed to what comes next.
        if (from > 0 && ClosesDay(rows, from - 1))
            top += dayGap;

        for (var i = from; i < rows.Count; i++)
        {
            rows[i].Top = top;
            top += rows[i].Height;

            if (ClosesDay(rows, i))
                top += dayGap;
        }

        return top;
    }

    /// <summary>Whether a row is the last the day has, and so is followed by the day gap.</summary>
    private static bool ClosesDay(IReadOnlyList<AgendaRow> rows, int index)
    {
        if (rows[index].Kind is not AgendaRowKind.Appointment)
            return false;

        var next = index + 1;

        return next < rows.Count
            && (rows[next].Kind is not AgendaRowKind.Appointment
            || rows[next].Date != rows[index].Date);
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

    private static int CountAppointments(
        IReadOnlyDictionary<DateOnly, List<ISchedulerAppointment>> byDay,
        DateOnly from,
        DateOnly to)
    {
        var count = 0;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (byDay.TryGetValue(date, out var appointments))
                count += appointments.Count;
        }

        return count;
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
        DateOnly rangeEnd,
        TimeZoneInfo view)
    {
        var byDay = new Dictionary<DateOnly, List<ISchedulerAppointment>>();
        var resolved = new Dictionary<ISchedulerAppointment, (DateTime Start, DateTime End)>();

        foreach (var item in items)
        {
            var start = item.StartIn(view);
            var date = DateOnly.FromDateTime(start);

            if (date < rangeStart || date > rangeEnd)
                continue;

            if (!byDay.TryGetValue(date, out var day))
                byDay[date] = day = [];

            day.Add(item);
            resolved[item] = (start, item.EndIn(view));
        }

        // OrderBy, not List.Sort: the framework's sort is not stable, and appointments starting at
        // the same minute must keep the order the host supplied them in — a row's index in its day
        // is its position on screen, so an unstable sort would shuffle rows between refreshes.
        foreach (var date in byDay.Keys.ToList())
        {
            byDay[date] = [.. byDay[date]
                .OrderBy(a => resolved[a].Start)
                .ThenByDescending(a => resolved[a].End - resolved[a].Start)];
        }

        return byDay;
    }

    private static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        var offset = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;

        return date.AddDays(-offset);
    }
}
