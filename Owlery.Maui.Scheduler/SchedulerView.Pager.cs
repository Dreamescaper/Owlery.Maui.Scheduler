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
    /// Moves to another page by sliding onto it, the way a swipe would, however far off it is.
    /// </summary>
    /// <remarks>
    /// A date set from outside — a "next week" button, a mini-calendar, a deep link — lands on a page
    /// either before or after the one showing. Rebuilding all three to arrive at it, with no motion to
    /// say where it came from, discards the only cue that tells the user which direction they moved
    /// in. So the destination is always slid in from the side it lies on: forward from the right,
    /// back from the left.
    /// <para>
    /// This is a one-page slide whatever the distance, not a pass through the pages in between —
    /// those were never rendered. Only the destination is laid out before the slide starts, and only
    /// if the page beside the centre is not already it; its neighbours are laid out once the slide has
    /// finished. See <see cref="SlideToPageAsync"/>.
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

        // Not awaited: the rotation and the date sync both happen before the first yield, so
        // everything the caller depends on has already been done by the time this returns.
        _ = SlideToPageAsync(target, forward: target > slots[1].PageStart);

        return true;
    }

    /// <summary>
    /// Brings a page in beside the centre, slides onto it, and only then lays out its neighbours and
    /// raises <see cref="VisibleDatesChanged"/>.
    /// </summary>
    /// <remarks>
    /// Until the slide has finished, the page left behind the centre is the one just slid away from,
    /// and the page on the far side is whatever the rotation recycled — neither is the destination's
    /// neighbour unless it happened to be adjacent. Laying them out first would put two pages of work
    /// between the request and the first frame of motion, for pages that are off screen throughout.
    /// <para>
    /// The price is that the ring buffer is briefly not contiguous, so the pager takes no swipe until
    /// it is again: a swipe back mid-slide would land on the page the user had just left, labelled as
    /// a page they had not. Waiting a few hundred milliseconds for the slide to finish is cheaper than
    /// any repair — swapping the partly visible outgoing page for the right one shows as a jump.
    /// </para>
    /// <para>
    /// A second navigation arriving mid-slide supersedes the first. Its rotation carries on from
    /// wherever the pages are, and only the latest slide puts the neighbours back: the earlier one
    /// finishing would otherwise re-lay the page the later one is sliding away from, while it is on
    /// screen.
    /// </para>
    /// </remarks>
    private async Task SlideToPageAsync(DateOnly target, bool forward)
    {
        var slide = ++navigationSlide;
        var incoming = forward ? SchedulerGeometry.SlotCount - 1 : 0;

        if (slots[incoming].PageStart != target)
        {
            slots[incoming].PageStart = target;
            PopulateSlot(slots[incoming], incoming);
        }

        navigationSliding = true;
        pagerScroll.IsScrollEnabled = false;

        var viewportWidth = ActiveGeometry.ViewportWidth;

        Rotate(forward);

        SyncSlotStarts();
        UpdateSelectionView();
        SyncDisplayDate();

        var outgoing = forward ? 0 : ActiveGeometry.SurfaceWidth - viewportWidth;

        await pagerScroll.ScrollToAsync(outgoing, false);

        // Superseded before it could start moving: the later slide owns the pager now.
        if (slide != navigationSlide)
            return;

        await pagerScroll.ScrollToAsync(viewportWidth, true);

        if (slide == navigationSlide)
            FinishNavigationSlide();
    }

    /// <summary>
    /// Lays out the centre page's neighbours, hands the pager back to the user, and tells the host.
    /// </summary>
    /// <remarks>
    /// Also called on unloading, since a slide cut off there may never report that it finished — and
    /// would leave the pager refusing every swipe once the view came back.
    /// </remarks>
    private void FinishNavigationSlide()
    {
        EndNavigationSlide();

        // Whatever happened to the pages meanwhile — a mode switch, a rebuild — they are laid out
        // around the centre page as it now is, and a slot already holding its neighbour is left alone.
        if (!Paged)
            return;

        PopulateNeighbour(0, pageSurface.PreviousPage(slots[1].PageStart));
        PopulateNeighbour(SchedulerGeometry.SlotCount - 1, pageSurface.NextPage(slots[1].PageStart));

        SyncSlotStarts();
        UpdateSelectionView();
        RaiseVisibleDatesChanged();
    }

    /// <summary>Stops a navigation slide from doing anything further, and gives swipes back.</summary>
    /// <remarks>
    /// Called on its own by <see cref="RebuildAll"/>, which lays out all three pages and recentres
    /// itself. A slide still waiting to animate captured the page width from before the rebuild, so
    /// letting it carry on would slide the pager to an offset the new pages no longer sit at — a
    /// host setting the date and switching to the month in one handler is exactly that.
    /// </remarks>
    private void EndNavigationSlide()
    {
        // Anything still awaiting an earlier slide is now stale.
        navigationSlide++;
        navigationSliding = false;

        if (Paged && !dragArmed)
            pagerScroll.IsScrollEnabled = true;
    }

    /// <summary>Moves the ring buffer by one page without laying anything out.</summary>
    /// <remarks>
    /// The recycled slot keeps the page it held, placed on the far side, until the slide that
    /// asked for the rotation lays it out afresh. Moved all the same, because it was just on screen
    /// behind the outgoing page and would otherwise be drawn there, over it.
    /// </remarks>
    private void Rotate(bool forward)
    {
        if (forward)
        {
            var recycled = slots[0];
            slots[0] = slots[1];
            slots[1] = slots[2];
            slots[2] = recycled;
        }
        else
        {
            var recycled = slots[2];
            slots[2] = slots[1];
            slots[1] = slots[0];
            slots[0] = recycled;
        }

        for (var i = 0; i < slots.Length; i++)
            ShiftSlot(slots[i], i);
    }

    private void PopulateNeighbour(int slotIndex, DateOnly page)
    {
        if (slots[slotIndex].PageStart == page)
            return;

        slots[slotIndex].PageStart = page;
        PopulateSlot(slots[slotIndex], slotIndex);
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
