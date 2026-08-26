using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>Infinite horizontal paging: snapping, rotating the ring buffer, recentring.</summary>
public partial class SchedulerView
{
    private void OnPagerScrolled(object? sender, ScrolledEventArgs e)
    {
        // The header sits outside the pager, so it is the one thing here that does not move by itself
        // and has to be mirrored by hand. This runs unconditionally, including while recentring:
        // gating it meant that any programmatic scroll which did not settle exactly as expected left
        // the strip stranded at a stale offset — off-screen, reading as a missing header — until some
        // later rebuild happened to reset it.
        headerSurface.TranslationX = -e.ScrollX;

        // A drag drives the pager itself, so nothing here should be mistaken for a swipe.
        if (recentring || dragArmed)
            return;

        lastScrollX = e.ScrollX;

        // Where the platform pages natively this only asks "has it arrived yet", because the target
        // was already chosen on finger release. Elsewhere it is also what picks the target, which is
        // worse: inertia has to run out first. Every new Scrolled event restarts the timer.
        snapTimer?.Stop();
        snapTimer ??= CreateSnapTimer();
        snapTimer.Start();
    }

    private IDispatcherTimer CreateSnapTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(SnapDetectionDelayMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => _ = SnapAsync();
        return timer;
    }

    private async Task SnapAsync()
    {
        if (snapping || recentring || geometry.ViewportWidth <= 0)
            return;

        // An accepted drop waits for the host to feed the change back before rejoining a week. If that
        // never comes, its view would stay pinned to the surface and drift over whatever week is
        // scrolled to next. Changing period is a safe moment to give up waiting and go back to what
        // the model says.
        if (floatingAppointment is not null)
            RepopulateAllSlots();

        var page = (int)Math.Round(lastScrollX / geometry.ViewportWidth);
        page = Math.Clamp(page, 0, SchedulerGeometry.SlotCount - 1);

        if (page == 1)
        {
            // Already centred; only correct a partial drag that did not change week.
            if (Math.Abs(lastScrollX - geometry.ViewportWidth) > 0.5)
                await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, true);
            return;
        }

        snapping = true;
        try
        {
            // Let the page the user chose settle under the finger first. This one is animated and
            // visible, so Scrolled must keep running to drag the day headers along with it.
            await pagerScroll.ScrollToAsync(page * geometry.ViewportWidth, 0, true);

            // Now rotate and jump back to the middle. Scroll handling is suppressed for this part so
            // nothing repaints the header between the rotation and the instant recentre.
            recentring = true;

            if (page == SchedulerGeometry.SlotCount - 1)
                Advance();
            else
                Retreat();

            headerSurface.TranslationX = -geometry.ViewportWidth;
            await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, false);
            lastScrollX = geometry.ViewportWidth;
        }
        finally
        {
            recentring = false;
            snapping = false;
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

    private async Task RecentreAsync(bool animated)
    {
        if (geometry.ViewportWidth <= 0)
            return;

        recentring = true;
        try
        {
            headerSurface.TranslationX = -geometry.ViewportWidth;
            await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, animated);
            lastScrollX = geometry.ViewportWidth;
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
