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
/// What a page holds and how it is measured — the part that differs between a timeline of hours, a
/// calendar month and a vertical agenda.
/// </summary>
/// <remarks>
/// The view pool and the reconciliation in <c>PopulateSlot</c> are indifferent to which surface made
/// a placement. Paging surfaces additionally use the page-navigation members; an agenda gives them a
/// stable month identity while collapsing horizontal page spacing to zero. The seam stays testable
/// without the MAUI host.
/// <para>
/// Note what is *not* here. Dragging is not, because it belongs to the timeline alone. Nor is
/// building the visual tree: the three modes differ in chrome and scrolling structure, which is
/// settled once when the mode changes instead of on every placement.
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

    /// <summary>
    /// How many appointments each of a page's cells could not show, for the drawable to mark. Empty
    /// where the question does not arise.
    /// </summary>
    /// <remarks>
    /// Answered from the last <see cref="Layout"/> of that page rather than recomputed, so it must be
    /// read straight after laying the page out — which is what <c>PopulateSlot</c> does.
    /// </remarks>
    IReadOnlyList<int> OverflowFor(DateOnly pageStart);

    /// <summary>
    /// Non-appointment chrome produced by the last layout of a page. Empty on a surface whose
    /// structure is entirely drawn or fixed outside the appointment layer.
    /// </summary>
    /// <remarks>
    /// Like <see cref="OverflowFor"/>, this belongs to the layout pass and must be read immediately
    /// after it. Keeping it on the seam lets the host reconcile chrome without learning which
    /// concrete surface produced it.
    /// </remarks>
    IReadOnlyList<AgendaSectionPlacement> SectionsFor(DateOnly pageStart);
}
