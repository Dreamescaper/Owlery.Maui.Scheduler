namespace Owlery.Maui.Scheduler.Internal;

/// <summary>One appointment placed on a line of a month cell.</summary>
internal sealed record MonthPlacement(
    ISchedulerAppointment Appointment,
    int CellIndex,
    int Line) : IAppointmentPlacement;

/// <summary>
/// A month page's chips, plus how many appointments each cell could not show.
/// </summary>
/// <param name="Placements">In cell order, then in the order they appear down the cell.</param>
/// <param name="OverflowByCell">
/// Indexed by cell. Zero where everything fitted; otherwise the number the "+N more" marker stands
/// for, which includes the appointment whose line the marker took.
/// </param>
internal sealed record MonthPageLayout(
    IReadOnlyList<IAppointmentPlacement> Placements,
    IReadOnlyList<int> OverflowByCell);

/// <summary>
/// Assigns each appointment a cell and a line within it, capping a cell at what it can show.
/// </summary>
/// <remarks>
/// Far simpler than <see cref="AppointmentLayoutEngine"/>, and deliberately so. A month cell has no
/// time axis, so nothing overlaps and no columns need packing — appointments are listed in the order
/// they start. There is no visible-hours window either: a 06:00 lesson is shown in a month whether or
/// not the timeline would have it on screen.
/// <para>
/// Multi-day appointments are not spanned; an appointment is placed on the day it starts, matching
/// the timeline (docs/design/README.md §14).
/// </para>
/// </remarks>
internal static class MonthLayoutEngine
{
    /// <summary>An appointment with its times already resolved into the view's zone.</summary>
    private readonly record struct Resolved(ISchedulerAppointment Appointment, DateTime Start, DateTime End);

    public static MonthPageLayout Layout(
        IEnumerable<ISchedulerAppointment> appointments,
        DateOnly gridStart,
        int linesPerCell,
        TimeZoneInfo view)
    {
        var buckets = new List<Resolved>?[MonthGeometry.CellCount];

        foreach (var appointment in appointments)
        {
            var start = appointment.StartIn(view);
            var cell = DateOnly.FromDateTime(start).DayNumber - gridStart.DayNumber;

            if (cell < 0 || cell >= MonthGeometry.CellCount)
                continue;

            (buckets[cell] ??= []).Add(new Resolved(appointment, start, appointment.EndIn(view)));
        }

        var placements = new List<IAppointmentPlacement>();
        var overflow = new int[MonthGeometry.CellCount];

        for (var cell = 0; cell < buckets.Length; cell++)
        {
            var bucket = buckets[cell];

            if (bucket is null)
                continue;

            // Ordered the way the day reads. OrderBy is stable, so appointments that start together
            // keep the order the host supplied them in — which is what lets PopulateSlot reuse views
            // positionally across a refresh without repainting them.
            var ordered = bucket
                .OrderBy(a => a.Start)
                .ThenByDescending(a => a.End - a.Start)
                .Select(a => a.Appointment)
                .ToList();

            // Everything fits, or the last line goes to the marker and one fewer appointment shows.
            var shown = ordered.Count <= linesPerCell
                ? ordered.Count
                : Math.Max(0, linesPerCell - 1);

            for (var line = 0; line < shown; line++)
                placements.Add(new MonthPlacement(ordered[line], cell, line));

            overflow[cell] = ordered.Count - shown;
        }

        return new MonthPageLayout(placements, overflow);
    }
}
