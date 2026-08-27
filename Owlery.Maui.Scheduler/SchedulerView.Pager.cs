using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>Infinite horizontal paging: snapping, rotating the ring buffer, recentring.</summary>
public partial class SchedulerView
{
    private void OnPagerScrolled(object? sender, PagingScrolledEventArgs e)
    {
        // The header sits outside the pager, so it is the one thing here that does not move by itself
        // and has to be mirrored by hand. This runs unconditionally, including while recentring:
        // gating it meant that any programmatic scroll which did not settle exactly as expected left
        // the strip stranded at a stale offset — off-screen, reading as a missing header — until some
        // later rebuild happened to reset it.
        headerSurface.TranslationX = -e.ScrollX;
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
    private void OnPageSettled(object? sender, PagingPageSettledEventArgs e)
    {
        if (recentring || dragArmed || ActiveGeometry.ViewportWidth <= 0)
            return;

        // An accepted drop waits for the host to feed the change back before rejoining a week. If that
        // never comes, its view would stay pinned to the surface and drift over whatever week is
        // scrolled to next. Changing period is a safe moment to give up waiting and go back to what
        // the model says.
        if (floatingAppointment is not null)
            RepopulateAllSlots();

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

    /// <summary>Puts the pager back on the centre page without anything being drawn in between.</summary>
    private void Recentre()
    {
        if (ActiveGeometry.ViewportWidth <= 0)
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

        // Taken from the rendered dates rather than from the page starts, because a page does not
        // have to begin on the first date it shows — a month grid opens on the tail of the previous
        // month, and the host has to be told to fetch that far back.
        var first = pageSurface.DatesOn(slots[0].PageStart)[0];
        var last = pageSurface.DatesOn(slots[2].PageStart)[^1];

        VisibleDatesChanged?.Invoke(this, new SchedulerVisibleDatesChangedEventArgs(
            visible,
            first.ToDateTime(TimeOnly.MinValue),
            last.ToDateTime(TimeOnly.MaxValue)));
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
