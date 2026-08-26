namespace Owlery.Maui.Scheduler.Internal;

/// <summary>One appointment placed on a page, in whatever terms the surface that laid it out uses.</summary>
/// <remarks>
/// Deliberately empty apart from the appointment itself. A placement is produced by a surface and
/// handed back to the same surface to be measured, so nothing between the two needs to know whether
/// it describes a column and a span of minutes or a cell and a line within it.
/// </remarks>
internal interface IAppointmentPlacement
{
    ISchedulerAppointment Appointment { get; }
}

/// <summary>
/// What a page holds and how it is measured — the part that differs between a timeline of hours and
/// a calendar month.
/// </summary>
/// <remarks>
/// The ring buffer, the view pool and the reconciliation in <c>PopulateSlot</c> are all indifferent
/// to which of those a page is; they need to know how to step from one page to the next, what a page
/// contains, and where to put things. That is exactly this interface, and every member of it is
/// arithmetic, so both implementations are testable without the MAUI test host.
/// <para>
/// Note what is *not* here. Dragging is not, because it belongs to the timeline alone. Nor is
/// building the visual tree: a month has no gutter and does not scroll vertically, so the two
/// differ in structure rather than in a value, and structure is settled once when the mode changes
/// instead of on every page.
/// </para>
/// </remarks>
internal interface ISchedulerSurface
{
    /// <summary>The start of the page containing a date, which is also that page's identity.</summary>
    DateOnly StartOfPage(DateOnly date);

    DateOnly NextPage(DateOnly pageStart);

    DateOnly PreviousPage(DateOnly pageStart);

    /// <summary>The dates a page shows, in order.</summary>
    IReadOnlyList<DateOnly> DatesOn(DateOnly pageStart);

    bool PageContains(DateOnly pageStart, DateOnly date);

    IReadOnlyList<IAppointmentPlacement> Layout(IEnumerable<ISchedulerAppointment> items, DateOnly pageStart);

    /// <summary>Where a placement sits within its own page, before the page's own offset is applied.</summary>
    Rect BoundsFor(IAppointmentPlacement placement);

    /// <summary>Where the selected-cell affordance sits within its page.</summary>
    Rect BoundsFor(SchedulerTimeSlot slot, DateOnly pageStart);

    /// <summary>What a touch on empty space selects, or <c>null</c> when it resolves to nothing.</summary>
    SchedulerTimeSlot? SlotAt(Point point, PageSlot[] pages);
}
