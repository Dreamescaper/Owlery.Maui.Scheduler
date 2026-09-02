namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Resolves an appointment's times into the zone the calendar is displayed in.
/// </summary>
/// <remarks>
/// The one place a host's <see cref="DateTime"/> is interpreted. Everything that lays appointments
/// out goes through here, so the rules live in a single function rather than being re-derived per
/// surface.
/// <para>
/// <see cref="DateTimeKind"/> says what a value means, and
/// <see cref="ISchedulerAppointment.TimeZone"/> speaks only when the kind does not:
/// </para>
/// <list type="table">
/// <item><term><c>Utc</c></term><description>an instant; converted, and any appointment zone ignored.</description></item>
/// <item><term><c>Local</c></term><description>an instant on this device; converted, appointment zone ignored.</description></item>
/// <item><term><c>Unspecified</c>, no zone</term><description>floating — used as it stands, never converted.</description></item>
/// <item><term><c>Unspecified</c> + zone</term><description>wall-clock in that zone; converted.</description></item>
/// </list>
/// <para>
/// The floating row is the one that matters for cost. It is the default, it is what a host that does
/// not care about zones supplies, and it does no work at all — see <see cref="In"/>.
/// </para>
/// </remarks>
internal static class AppointmentTime
{
    /// <summary>The appointment's start, in <paramref name="view"/>.</summary>
    public static DateTime StartIn(this ISchedulerAppointment appointment, TimeZoneInfo view) =>
        In(appointment.Start, appointment.TimeZone, view);

    /// <summary>The appointment's end, in <paramref name="view"/>.</summary>
    public static DateTime EndIn(this ISchedulerAppointment appointment, TimeZoneInfo view) =>
        In(appointment.End, appointment.TimeZone, view);

    /// <summary>
    /// Resolves one value against the rules above, as wall-clock in <paramref name="view"/> with
    /// <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    /// <remarks>
    /// Two fast paths, and they are the point rather than a tidy-up. A floating value and a value
    /// already in the view's zone are returned untouched, so the overwhelmingly common case costs a
    /// switch and a reference comparison instead of a trip through the time-zone database — which
    /// runs per appointment, per slot, three slots per rebuild, over a collection hosts are told to
    /// keep wholly loaded. Removing them would make a repopulate scale by tzdb lookups.
    /// See <c>docs/design/verification.md</c> (§15) for what the layout costs without them.
    /// </remarks>
    public static DateTime In(DateTime value, TimeZoneInfo? source, TimeZoneInfo view)
    {
        switch (value.Kind)
        {
            case DateTimeKind.Utc:
                return Unspecified(TimeZoneInfo.ConvertTimeFromUtc(value, view));

            case DateTimeKind.Local:
                return TimeZoneInfo.Local.Equals(view)
                    ? Unspecified(value)
                    : Unspecified(TimeZoneInfo.ConvertTime(value, TimeZoneInfo.Local, view));

            default:
                // Floating, or already where it needs to be.
                return source is null || source.Equals(view)
                    ? Unspecified(value)
                    : Unspecified(TimeZoneInfo.ConvertTimeFromUtc(ToUtc(value, source), view));
        }
    }

    /// <summary>
    /// A wall-clock time in <paramref name="zone"/> as an instant, resolving the two clock readings
    /// that a zone's own transitions make nonsense.
    /// </summary>
    /// <remarks>
    /// A host can hand over 02:30 on a spring-forward morning, which never happens, or 01:30 on a
    /// fall-back morning, which happens twice. <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>
    /// throws on the first and quietly takes standard time on the second, and throwing inside a
    /// layout pass over tens of thousands of items is not an option. So: an invalid time is moved
    /// forward by the gap that swallowed it, and an ambiguous one is read as its first occurrence —
    /// the larger offset, which is the earlier instant. Neither throws. The same policy resolves
    /// <see cref="SchedulerMoment.ToDateTimeUtc"/>, so the control answers the question the same way
    /// whichever direction it is asked from.
    /// </remarks>
    public static DateTime ToUtc(DateTime wallClock, TimeZoneInfo zone)
    {
        var value = Unspecified(wallClock);

        if (zone.IsInvalidTime(value))
            value += GapAt(value, zone);

        if (zone.IsAmbiguousTime(value))
            return DateTime.SpecifyKind(value - Latest(zone.GetAmbiguousTimeOffsets(value)), DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeToUtc(value, zone);
    }

    /// <summary>How much clock time a transition swallowed, measured around it rather than at it.</summary>
    /// <remarks>
    /// Read a day either side: asking for the offset *at* an invalid time answers with the standard
    /// one, which is the very thing being corrected for.
    /// </remarks>
    private static TimeSpan GapAt(DateTime invalid, TimeZoneInfo zone) =>
        zone.GetUtcOffset(invalid.AddDays(1)) - zone.GetUtcOffset(invalid.AddDays(-1));

    /// <summary>
    /// The larger of the offsets an ambiguous reading can carry, which is its first occurrence.
    /// </summary>
    private static TimeSpan Latest(TimeSpan[] offsets)
    {
        var largest = offsets[0];

        for (var i = 1; i < offsets.Length; i++)
        {
            if (offsets[i] > largest)
                largest = offsets[i];
        }

        return largest;
    }

    private static DateTime Unspecified(DateTime value) =>
        value.Kind is DateTimeKind.Unspecified ? value : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
}
