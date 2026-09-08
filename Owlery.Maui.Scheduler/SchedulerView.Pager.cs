using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>Infinite horizontal paging: snapping, rotating the ring buffer, recentring.</summary>
public partial class SchedulerView
{
    private void OnPagerScrolled(object? sender, EventArgs e)
    {
        // The header sits outside the pager, so it is the one thing here that does not move by itself
        // and has to be mirrored by hand. This runs unconditionally, including while recentring:
        // gating it meant that any programmatic scroll which did not settle exactly as expected left
        // the strip stranded at a stale offset — off-screen, reading as a missing header — until some
        // later rebuild happened to reset it.
        headerSurface.TranslationX = -pagerScroll.ScrollX;
    }

    /// <summary>
    /// Rotates the ring buffer onto the page the pager has landed on, and recentres on it.
    /// </summary>
    /// <remarks>
    /// The pager decides which page that is, because only the platform knows the release velocity.
    /// What used to be here instead — a timer that waited for the offset to go quiet and then inferred
    /// the page from it — could not do better than wait out the inertia, which is why a flick on
    /// Android was followed by a visible pause before the week settled.
    /// <para>
    /// The rotation and the recentre have to reach the screen together. They are two writes to two
    /// different things: rotating moves the appointment views onto their new pages, while the offset
    /// still points at the old one, so anything drawn in between shows a page the user never swiped
    /// to. That is why the recentre is <see cref="PagingScrollView.ScrollTo"/> rather than an awaited
    /// request — this method never yields, so no frame can be composited part-way through it.
    /// </para>
    /// </remarks>
    /// <summary>Whether this surface pages sideways at all. An agenda is one continuous page.</summary>
    private bool Paged => ViewMode is not SchedulerViewMode.Agenda;

    private void OnPageSettled(object? sender, PagingPageSettledEventArgs e)
    {
        // Without this the pager would report page 0 on a one-page surface and retreat off the front
        // of the calendar; and Recentre would scroll to an offset the surface is no longer wide
        // enough to hold.
        if (!Paged || recentring || dragArmed || ActiveGeometry.ViewportWidth <= 0)
            return;

        var page = Math.Clamp(e.Page, 0, SchedulerGeometry.SlotCount - 1);

        if (page == 1)
            return;

        recentring = true;
        try
        {
            if (page == SchedulerGeometry.SlotCount - 1)
                Advance();
            else
                Retreat();

            headerSurface.TranslationX = -ActiveGeometry.ViewportWidth;
            pagerScroll.ScrollTo(ActiveGeometry.ViewportWidth);
        }
        finally
        {
            recentring = false;
        }

        SyncSlotStarts();
        UpdateSelectionView();
        SyncDisplayDate();
        RaiseVisibleDatesChanged();
    }

    private void Advance()
    {
        var recycled = slots[0];
        slots[0] = slots[1];
        slots[1] = slots[2];
        slots[2] = recycled;

        ShiftSlot(slots[0], 0);
        ShiftSlot(slots[1], 1);

        recycled.PageStart = pageSurface.NextPage(slots[1].PageStart);
        PopulateSlot(recycled, 2);
    }

    private void Retreat()
    {
        var recycled = slots[2];
        slots[2] = slots[1];
        slots[1] = slots[0];
        slots[0] = recycled;

        ShiftSlot(slots[1], 1);
        ShiftSlot(slots[2], 2);

        recycled.PageStart = pageSurface.PreviousPage(slots[1].PageStart);
        PopulateSlot(recycled, 0);
    }

    /// <summary>
    /// Moves to a page the ring buffer already holds by sliding onto it, the way a swipe would.
    /// </summary>
    /// <remarks>
    /// A date set from outside — a "next week" button, a mini-calendar, a deep link — is most often
    /// the period either side of the one showing, and that period is already rendered and one page
    /// away. Rebuilding all three to arrive at it, with no motion to say where it came from, discards
    /// both the views and the only cue that tells the user which direction they moved in.
    /// <para>
    /// Anything further off is still a rebuild: the pages in between were never rendered, so there is
    /// nothing to slide through and an animation would only be a delay.
    /// </para>
    /// </remarks>
    private bool TrySlideToPage(DateOnly target)
    {
        if (!Paged)
            return false;

        // A drag owns the pager for its duration — scrolling is turned off and the drop target is
        // resolved against the centre page — so a period change under it goes the blunt way.
        if (dragArmed || ActiveGeometry.ViewportWidth <= 0)
            return false;

        var centre = slots[1].PageStart;
        var forward = target == pageSurface.NextPage(centre);

        if (!forward && target != pageSurface.PreviousPage(centre))
            return false;

        // Not awaited: the rotation and the date sync both happen before the first yield, so
        // everything the caller depends on has already been done by the time this returns.
        _ = SlideToAdjacentPageAsync(forward);

        return true;
    }

    /// <summary>
    /// Rotates one page in the given direction and slides onto it, as a swipe would.
    /// </summary>
    /// <remarks>
    /// Rotating swaps the pages over without moving anything, so on its own the calendar simply
    /// changes contents where it stands and it is hard to see that anything happened. Instead, jump
    /// to where the outgoing page has landed — visually identical to the frame before — and then
    /// slide across to the centre, so the change reads as the same motion as a swipe.
    /// <para>
    /// Only the page rotated in is laid out. The two that were already rendered keep the views they
    /// have, which is why this is worth reaching for whenever the destination is adjacent:
    /// <see cref="RebuildAll"/> repopulates all three, and the page being moved to is one of them.
    /// </para>
    /// </remarks>
    private async Task SlideToAdjacentPageAsync(bool forward)
    {
        var viewportWidth = ActiveGeometry.ViewportWidth;

        if (forward)
            Advance();
        else
            Retreat();

        // The ghost marks a slot in the period a drag started from, so it travels with that period
        // and slides off screen once the drag has moved on. Null unless a drag is in flight.
        if (floatingView is not null)
            floatingView.TranslationX += forward ? -viewportWidth : viewportWidth;

        SyncSlotStarts();
        UpdateSelectionView();
        SyncDisplayDate();
        RaiseVisibleDatesChanged();

        var outgoing = forward ? 0 : ActiveGeometry.SurfaceWidth - viewportWidth;

        await pagerScroll.ScrollToAsync(outgoing, false);
        await pagerScroll.ScrollToAsync(viewportWidth, true);
    }

    /// <summary>Puts the pager back on the centre page without anything being drawn in between.</summary>
    private void Recentre()
    {
        if (!Paged || ActiveGeometry.ViewportWidth <= 0)
            return;

        recentring = true;
        try
        {
            headerSurface.TranslationX = -ActiveGeometry.ViewportWidth;
            pagerScroll.ScrollTo(ActiveGeometry.ViewportWidth);
        }
        finally
        {
            recentring = false;
        }
    }

    private void RaiseVisibleDatesChanged()
    {
        var visible = pageSurface.DatesOn(slots[1].PageStart)
            .Select(date => date.ToDateTime(TimeOnly.MinValue))
            .ToArray();

        DateOnly first;
        DateOnly last;

        if (ViewMode is SchedulerViewMode.Agenda)
        {
            // Every date in the agenda's loaded range is reachable by vertical scrolling. Reporting
            // neighbouring month identities would claim that data is rendered when it is not.
            first = DateOnly.FromDateTime(visible[0]);
            last = DateOnly.FromDateTime(visible[^1]);
        }
        else
        {
            // Taken from the rendered dates rather than from the page starts, because a page does
            // not have to begin on the first date it shows — a month grid opens on the tail of the
            // previous month, and the host has to be told to fetch that far back.
            first = pageSurface.DatesOn(slots[0].PageStart)[0];
            last = pageSurface.DatesOn(slots[2].PageStart)[^1];
        }

        var key = new VisibleDatesReportKey(
            ViewMode,
            DateOnly.FromDateTime(visible[0]),
            DateOnly.FromDateTime(visible[^1]),
            visible.Length,
            first,
            last);

        // A report is a statement of effective range, not a trace of internal rebuilds. Store the
        // key before invoking the host so a synchronous ItemsSource response cannot re-enter with
        // the same report.
        if (lastVisibleDatesReport == key)
            return;

        lastVisibleDatesReport = key;

        VisibleDatesChanged?.Invoke(this, new SchedulerVisibleDatesChangedEventArgs(
            [.. visible.Select(Moment)],
            Moment(first.ToDateTime(TimeOnly.MinValue)),
            Moment(last.ToDateTime(TimeOnly.MaxValue))));
    }

    private void SyncDisplayDate()
    {
        suppressDisplayDateSync = true;
        try
        {
            DisplayDate = slots[1].PageStart.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            suppressDisplayDateSync = false;
        }
    }
}
