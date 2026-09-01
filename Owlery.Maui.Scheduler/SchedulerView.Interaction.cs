using System.Globalization;
using Microsoft.Maui.Layouts;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>Taps, cell selection, and drag-and-drop.</summary>
public partial class SchedulerView
{
    /// <summary>
    /// Places the marker on the selected cell, where there is one to mark.
    /// </summary>
    /// <remarks>
    /// A month does not show it. The affordance means "tap again to create something here", and a
    /// month cell has no time to create anything at — tapping one opens that day instead. Selection
    /// itself still happens, so <see cref="SelectedSlot"/> and <c>CellTapped</c> report as usual.
    /// </remarks>
    private void UpdateSelectionView() => cellSelection.Update(
        ViewMode is SchedulerViewMode.Timeline ? SelectedSlot : null,
        slots,
        ActiveGeometry,
        pageSurface,
        TimeFormat);

    // All input for the scrolling surface is handled here, on the drawing surface, rather than by
    // gesture recognizers attached to each appointment.
    //
    // The reason is StartInteraction: GraphicsView reports the raw touch lifecycle, so a press is
    // known at touch-down, before any movement. PanGestureRecognizer cannot provide that -- it only
    // reports Started once the platform has already recognised a pan, i.e. after the finger has
    // moved -- which makes "hold still to pick an appointment up" impossible to detect with it.
    // Handling everything here also means pooled views need no recognizers attached at all.
    private void OnSurfaceStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || ActiveGeometry.ViewportWidth <= 0)
            return;

        var point = new Point(e.Touches[0].X, e.Touches[0].Y);

        interactionStart = point;
        interactionMoved = false;
        pressedView = HitTestAppointment(point);

        if (pressedView is not null && DraggingEnabled)
            BeginDragCandidate(pressedView, point);
    }

    private void OnSurfaceDragInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
            return;

        var point = new Point(e.Touches[0].X, e.Touches[0].Y);
        var travelled = Math.Abs(point.X - interactionStart.X) + Math.Abs(point.Y - interactionStart.Y);

        if (travelled > TapMovementToleranceDp)
            interactionMoved = true;

        if (dragArmed)
        {
            UpdateDragPosition(point);
            return;
        }

        // Moving before the press has been held long enough means the user is scrolling.
        if (travelled > DragMovementToleranceDp)
            CancelDragCandidate();
    }

    // Raised when an ancestor scroll view claims the gesture. That is a far better "this is a scroll,
    // not a drag" signal than inferring it from a distance threshold, because it is the platform's
    // own decision.
    private void OnSurfaceCancelInteraction(object? sender, EventArgs e)
    {
        pressedView = null;
        CancelDragCandidate();
    }

    private void OnSurfaceEndInteraction(object? sender, TouchEventArgs e)
    {
        if (dragArmed)
        {
            CompleteDrag(committed: true);
            return;
        }

        var candidate = pressedView;
        var moved = interactionMoved;

        pressedView = null;
        CancelDragCandidate();

        if (moved || e.Touches.Length == 0)
            return;

        if (candidate is not null)
        {
            if (appointmentsByView.TryGetValue(candidate, out var appointment))
                AppointmentTapped?.Invoke(this, new SchedulerAppointmentTappedEventArgs(Resolve(appointment)));

            return;
        }

        if (pageSurface.SlotAt(new Point(e.Touches[0].X, e.Touches[0].Y), slots) is not { } slot)
            return;

        SelectedSlot = slot;
        CellTapped?.Invoke(this, new SchedulerCellTappedEventArgs(slot));
    }

    /// <summary>
    /// Reports the day whose header was tapped.
    /// </summary>
    /// <remarks>
    /// The strip carries all three pages and is translated by the pager offset, so a position taken
    /// against the strip is already in the same space the columns were laid out in — the same space
    /// <see cref="TimelineSurface.DateAt"/> resolves the grid in. Nothing has to undo the scroll.
    /// <para>
    /// Silent in a month. Its header names weekdays rather than dates, and a column of one stands for
    /// six of them; there is no single day to report, so nothing is reported.
    /// </para>
    /// </remarks>
    private void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (ViewMode is not SchedulerViewMode.Timeline || HeaderTapped is null)
            return;

        if (e.GetPosition(headerSurface) is not { } point)
            return;

        if (timelineSurface.DateAt(point.X, slots) is not { } date)
            return;

        HeaderTapped.Invoke(this, new SchedulerHeaderTappedEventArgs(date.ToDateTime(TimeOnly.MinValue)));
    }

    /// <summary>Reports the time the hour gutter was tapped at, snapped like a cell tap.</summary>
    private void OnGutterTapped(object? sender, double minutes)
    {
        if (TimeGutterTapped is null)
            return;

        var snapped = timelineSurface.Snap(minutes);

        TimeGutterTapped.Invoke(this, new SchedulerTimeGutterTappedEventArgs(TimeSpan.FromMinutes(snapped)));
    }

    /// <summary>Finds the topmost appointment under a surface point, mirroring the paint order.</summary>
    private View? HitTestAppointment(Point point)
    {
        for (var i = slots.Length - 1; i >= 0; i--)
        {
            var views = slots[i].Views;

            for (var v = views.Count - 1; v >= 0; v--)
            {
                var view = views[v];
                var bounds = AbsoluteLayout.GetLayoutBounds(view);
                var rect = new Rect(
                    bounds.X + view.TranslationX,
                    bounds.Y + view.TranslationY,
                    bounds.Width,
                    bounds.Height);

                if (rect.Contains(point))
                    return view;
            }
        }

        return null;
    }

    private void BeginDragCandidate(View view, Point point)
    {
        CancelDragCandidate();

        if (!appointmentsByView.ContainsKey(view))
            return;

        dragView = view;
        dragOriginalBounds = AbsoluteLayout.GetLayoutBounds(view);

        // Remember where inside the appointment the finger landed so it does not jump on pick-up.
        dragGrabOffset = new Point(
            point.X - (dragOriginalBounds.X + view.TranslationX),
            point.Y - (dragOriginalBounds.Y + view.TranslationY));

        // The timer starts at touch-down, so holding still is what wins. This is the whole reason
        // interaction is handled on the drawing surface -- see OnSurfaceStartInteraction.
        longPressTimer = Dispatcher.CreateTimer();
        longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressDelayMs);
        longPressTimer.IsRepeating = false;
        longPressTimer.Tick += (_, _) => ArmDrag();
        longPressTimer.Start();
    }

    private void ArmDrag()
    {
        longPressTimer?.Stop();
        longPressTimer = null;

        if (dragView is null || !appointmentsByView.TryGetValue(dragView, out var appointment))
            return;

        var args = new SchedulerAppointmentDragStartingEventArgs(Resolve(appointment));
        AppointmentDragStarting?.Invoke(this, args);

        if (args.Cancel)
        {
            CancelDragCandidate();
            return;
        }

        // The appointment leaves its week. Its original view stays put in the surface as a faded
        // ghost — it belongs to the week it came from and should travel with it — while a separate
        // view on the overlay follows the finger, clear of anything that scrolls.
        floatingAppointment = appointment;
        floatingView = dragView;

        DetachFromSlot(floatingView);
        floatingView.Opacity = GhostOpacity;

        if (!ShowDraggedAppointment())
        {
            floatingView.Opacity = 1;
            floatingAppointment = null;
            floatingView = null;
            CancelDragCandidate();
            return;
        }

        dragArmed = true;
        lastDropTarget = null;
        SetScrollingEnabled(false);

        UpdateDragPosition(new Point(
            dragOriginalBounds.X + dragView.TranslationX + dragGrabOffset.X,
            dragOriginalBounds.Y + dragView.TranslationY + dragGrabOffset.Y));
    }

    /// <summary>
    /// Prepares the overlay view that follows the finger, creating it on first use.
    /// </summary>
    /// <remarks>
    /// One instance is enough — only one appointment can be dragged at a time — so it is kept rather
    /// than rented, and never reparented. The pool's views all live on the scrolling surface.
    /// </remarks>
    private bool ShowDraggedAppointment()
    {
        if (floatingAppointment is null)
            return false;

        if (dragOverlayView is null)
        {
            if (AppointmentTemplate?.CreateContent() is not View created)
                return false;

            created.InputTransparent = true;
            dragOverlay.Add(created);
            dragOverlayView = created;
        }

        dragOverlayView.BindingContext = floatingAppointment;
        SetAppointmentSemantics(dragOverlayView, floatingAppointment);
        dragOverlayView.Opacity = LiftedOpacity;
        dragOverlayView.IsVisible = true;

        AbsoluteLayout.SetLayoutFlags(dragOverlayView, AbsoluteLayoutFlags.None);

        return true;
    }

    private void CancelDragCandidate()
    {
        longPressTimer?.Stop();
        longPressTimer = null;

        var wasArmed = dragArmed;

        dragArmed = false;
        dragView = null;

        if (!wasArmed)
            return;

        StopEdgePaging();
        StopEdgeScrolling();
        SetScrollingEnabled(true);
        gutter.ClearIndicator();
        HideDraggedAppointment();
        RepopulateAllSlots();
    }

    /// <summary>Takes a view out of its week so rotation and reconciliation leave it alone.</summary>
    private void DetachFromSlot(View view)
    {
        if (slotsByView.TryGetValue(view, out var slot))
            slot.Views.Remove(view);

        slotsByView.Remove(view);
    }

    /// <summary>Hands the drag's view back to the pool so the appointment is laid out normally again.</summary>
    private void ReleaseFloatingAppointment()
    {
        if (floatingView is not null)
        {
            appointmentsByView.Remove(floatingView);
            pool.Return(floatingView);
        }

        floatingView = null;
        floatingAppointment = null;
    }

    /// <summary>Takes the follower off the overlay. Done as soon as the finger lifts, whatever the outcome.</summary>
    private void HideDraggedAppointment()
    {
        if (dragOverlayView is null)
            return;

        dragOverlayView.IsVisible = false;
        dragOverlayView.BindingContext = null;
    }

    /// <summary>
    /// One-time platform setup for the timeline's vertical scrolling.
    /// </summary>
    /// <remarks>
    /// The pager needs none of this any more: paging, bouncing and the touch delay all belong to
    /// <see cref="PagingScrollView"/> now, which is the point of it existing.
    /// <para>
    /// A UIScrollView holds back touchesBegan while it decides whether a touch is the start of a
    /// scroll. The long press that picks an appointment up needs to know about the press the moment
    /// the finger lands, so that delay is turned off.
    /// </para>
    /// </remarks>
    private void ConfigurePlatformScrolling()
    {
#if IOS || MACCATALYST
        if (verticalScroll.Handler?.PlatformView is UIKit.UIScrollView verticalPlatformScroll)
        {
            verticalPlatformScroll.DelaysContentTouches = false;

            // Likewise vertically: past the first or last hour there is only blank surface below the
            // grid, which reads as the calendar coming apart rather than as an affordance.
            verticalPlatformScroll.Bounces = false;
        }
#endif
    }

    /// <summary>
    /// Stops the scroll views from competing with an armed drag.
    /// </summary>
    /// <remarks>
    /// The pager answers for itself. What is left is the timeline's vertical scroll, and it is the
    /// only place the control still reaches past MAUI. <c>IsEnabled = false</c> is not usable: it
    /// disables interaction for the whole subtree and cancels the very touch that is driving the
    /// drag. Neither is <c>ScrollOrientation.Neither</c>, tempting as it looks — it maps to
    /// <c>ScrollEnabled</c> on iOS but on Android leaves <c>OnInterceptTouchEvent</c> unguarded, so
    /// the ancestor still steals the gesture and then declines to scroll with it. See DESIGN.md
    /// section 19.
    /// </remarks>
    private void SetScrollingEnabled(bool enabled)
    {
        pagerScroll.IsScrollEnabled = enabled;

#if IOS || MACCATALYST
        if (verticalScroll.Handler?.PlatformView is UIKit.UIScrollView verticalPlatformScroll)
            verticalPlatformScroll.ScrollEnabled = enabled;
#elif ANDROID
        // Android ancestors keep intercepting until a descendant opts out for the rest of the gesture.
        if (gridView.Handler?.PlatformView is Android.Views.View platformView)
            platformView.Parent?.RequestDisallowInterceptTouchEvent(!enabled);
#endif
    }

    private void UpdateDragPosition(Point point)
    {
        if (floatingView is null)
            return;

        lastDragPoint = point;

        // While a period change is sliding, the pager is between pages, so the drop target cannot be
        // resolved: the maths below assumes the pager is at rest on the centre slot, and a column
        // resolved now belongs to a page that is only half on screen. The *follower* has no such
        // problem — the finger's position on screen is the touch point less the live scroll offsets,
        // whatever the pager is doing — so it keeps tracking. Freezing it too made the appointment sit
        // still for the length of the slide while the calendar moved under it.
        // PageDuringDragAsync recomputes the target from lastDragPoint once the slide settles.
        if (pagingDuringDrag || !PagerAtRest)
        {
            MoveDragOverlayToFinger(point);
            return;
        }

        // Scrolling is frozen for the duration of a drag and the pager always rests on the centre
        // slot, so that is the period being dropped into — whichever one has since been rotated into
        // it by edge paging.
        const int slotIndex = 1;

        var slotOffset = slotIndex * geometry.ViewportWidth;

        var target = DropTargetResolver.Resolve(
            geometry,
            slots[slotIndex].PageStart,
            point,
            dragGrabOffset,
            slotOffset,
            dragOriginalBounds.Height,
            DragSnapMinutes);

        var snappedX = target.XWithinPage(geometry, TimelineSurface.AppointmentGap);
        var snappedY = target.Y(geometry);

        // Converted out of surface space into the control's own, which is where the overlay lives.
        // Subtracting the live scroll offsets makes this self-correcting: a given finger position
        // resolves to the same place on screen wherever the pager happens to be mid-slide.
        if (dragOverlayView is not null)
        {
            AbsoluteLayout.SetLayoutBounds(dragOverlayView, new Rect(
                TimeGutterWidth + slotOffset + snappedX - pagerScroll.ScrollX,
                HeaderHeight + snappedY - verticalScroll.ScrollY,
                dragOriginalBounds.Width,
                dragOriginalBounds.Height));
        }

        dragDropStart = target.Start;
        RaiseDropTargetChangedIfMoved();

        // Shown in the time gutter rather than over the grid: anywhere near the appointment is under
        // the finger doing the dragging, which is precisely where it cannot be read.
        if (ShowDragTimeIndicator)
            gutter.ShowIndicator(target.SnappedMinutes, dragDropStart.ToString(TimeFormat, CultureInfo.CurrentUICulture));

        UpdateEdgePaging(point);
        UpdateEdgeScrolling(point);
    }

    /// <summary>Reports a change of boundary, once per boundary rather than once per movement.</summary>
    /// <remarks>
    /// Silent on the first resolve of a drag. Picking an appointment up resolves it where it already
    /// is, and a host tying feedback to this would otherwise buzz on every long press. From then on it
    /// fires whenever the drop target moves, including when edge paging carries it to another week.
    /// </remarks>
    private void RaiseDropTargetChangedIfMoved()
    {
        var previous = lastDropTarget;
        lastDropTarget = dragDropStart;

        if (previous is null || previous == dragDropStart || AppointmentDropTargetChanged is null || floatingAppointment is null)
            return;

        AppointmentDropTargetChanged.Invoke(
            this,
            new SchedulerAppointmentDropTargetChangedEventArgs(Resolve(floatingAppointment), dragDropStart));
    }

    /// <summary>
    /// Whether the pager is sitting on the centre page, which is what the drop-target maths assumes.
    /// </summary>
    /// <remarks>
    /// Asked of the offset rather than inferred from <c>pagingDuringDrag</c>. That flag is cleared
    /// when an awaited scroll returns, so anything completing the wait early — a stale finish from
    /// the platform, or a second request abandoning the pending one — let a column be resolved while
    /// the pages were still moving, and the appointment jumped. Rare, and not something tightening
    /// the plumbing can rule out; the position cannot lie.
    /// </remarks>
    private bool PagerAtRest => Math.Abs(pagerScroll.ScrollX - ActiveGeometry.ViewportWidth) < 1;

    /// <summary>
    /// Keeps the follower under the finger, without resolving where it would land.
    /// </summary>
    /// <remarks>
    /// Kept inside the visible grid. With no drop target to bound it — that is the whole point of
    /// this path — the appointment would otherwise be carried out over the gutter or the header by a
    /// finger held past the edge, which is precisely where it is: the edge is what started the paging.
    /// </remarks>
    private void MoveDragOverlayToFinger(Point point)
    {
        if (dragOverlayView is null)
            return;

        var width = dragOriginalBounds.Width;
        var height = dragOriginalBounds.Height;

        var left = TimeGutterWidth;
        var top = HeaderHeight;

        AbsoluteLayout.SetLayoutBounds(dragOverlayView, new Rect(
            Math.Clamp(
                left + point.X - pagerScroll.ScrollX - dragGrabOffset.X,
                left,
                Math.Max(left, left + ActiveGeometry.ViewportWidth - width)),
            Math.Clamp(
                top + point.Y - verticalScroll.ScrollY - dragGrabOffset.Y,
                top,
                Math.Max(top, top + ActiveGeometry.ViewportHeight - height)),
            width,
            height));
    }

    /// <summary>
    /// Holding a dragged appointment against the leading or trailing edge pages to the adjacent week,
    /// which is how an appointment is moved out of the week it started in.
    /// </summary>
    /// <remarks>
    /// A dwell rather than an immediate flip: the edges are exactly where someone drags to reach the
    /// first and last day of the week, so paging on contact would make those two columns unusable.
    /// The timer repeats, so continuing to hold keeps walking through the weeks.
    /// </remarks>
    private void UpdateEdgePaging(Point point)
    {
        if (!AllowDragAcrossPeriods || !dragArmed)
        {
            StopEdgePaging();
            return;
        }

        var direction = EdgePagingDetector.DirectionFor(geometry, point);

        if (direction == 0)
        {
            StopEdgePaging();
            return;
        }

        // Already counting down towards this edge — let it run rather than restarting the dwell.
        if (direction == edgePagingDirection)
            return;

        StopEdgePaging();
        edgePagingDirection = direction;

        edgePagingTimer = Dispatcher.CreateTimer();
        edgePagingTimer.Interval = TimeSpan.FromMilliseconds(EdgePagingDwellMs);
        edgePagingTimer.IsRepeating = true;
        edgePagingTimer.Tick += (_, _) => _ = PageDuringDragAsync();
        edgePagingTimer.Start();
    }

    /// <summary>
    /// Holding a dragged appointment against the top or bottom of the timeline scrolls it, so times
    /// that are off screen can be reached without putting the appointment down first.
    /// </summary>
    /// <remarks>
    /// No dwell here, unlike edge paging. Dragging towards an hour that is off screen means only one
    /// thing, whereas the leading and trailing columns are somewhere a person legitimately wants to
    /// drop, so those needed protecting from an immediate flip.
    /// </remarks>
    private void UpdateEdgeScrolling(Point point)
    {
        if (!dragArmed)
        {
            StopEdgeScrolling();
            return;
        }

        var direction = EdgeScrollDetector.DirectionFor(point.Y - verticalScroll.ScrollY, geometry.ViewportHeight);

        if (direction == 0)
        {
            StopEdgeScrolling();
            return;
        }

        if (direction == edgeScrollDirection)
            return;

        StopEdgeScrolling();
        edgeScrollDirection = direction;

        edgeScrollTimer = Dispatcher.CreateTimer();
        edgeScrollTimer.Interval = TimeSpan.FromMilliseconds(EdgeScrollIntervalMs);
        edgeScrollTimer.IsRepeating = true;
        edgeScrollTimer.Tick += (_, _) => ScrollDuringDrag();
        edgeScrollTimer.Start();
    }

    private void StopEdgeScrolling()
    {
        edgeScrollTimer?.Stop();
        edgeScrollTimer = null;
        edgeScrollDirection = 0;
    }

    private void ScrollDuringDrag()
    {
        if (!dragArmed || edgeScrollDirection == 0)
        {
            StopEdgeScrolling();
            return;
        }

        var furthest = Math.Max(0, geometry.ContentHeight - geometry.ViewportHeight);
        var target = Math.Clamp(verticalScroll.ScrollY + (edgeScrollDirection * EdgeScrollStepDp), 0, furthest);
        var travelled = target - verticalScroll.ScrollY;

        // Already showing as much of the day as there is in that direction.
        if (Math.Abs(travelled) < 0.5)
        {
            StopEdgeScrolling();
            return;
        }

        _ = verticalScroll.ScrollToAsync(0, target, false);

        // The finger has not moved, but the hours under it have. Touch points arrive in the timeline's
        // own coordinates rather than the screen's, so the same finger is now over a time further down
        // by exactly what was scrolled.
        lastDragPoint = new Point(lastDragPoint.X, lastDragPoint.Y + travelled);
        UpdateDragPosition(lastDragPoint);
    }

    private void StopEdgePaging()
    {
        edgePagingTimer?.Stop();
        edgePagingTimer = null;
        edgePagingDirection = 0;
    }

    private async Task PageDuringDragAsync()
    {
        if (!dragArmed || edgePagingDirection == 0)
        {
            StopEdgePaging();
            return;
        }

        // The dwell repeats, and the slide takes a moment; do not start a second one over the top.
        if (pagingDuringDrag)
            return;

        pagingDuringDrag = true;

        try
        {
            await SlideToAdjacentPageAsync(edgePagingDirection > 0);
        }
        finally
        {
            pagingDuringDrag = false;
        }

        // The finger has not moved but the week beneath it has, so the drop target and its label are
        // now for a different date. Done after the flag is cleared, since the guard in
        // UpdateDragPosition holds the target for exactly as long as it is set.
        if (dragArmed)
            UpdateDragPosition(lastDragPoint);
    }

    private void CompleteDrag(bool committed)
    {
        longPressTimer?.Stop();
        longPressTimer = null;
        StopEdgePaging();
        StopEdgeScrolling();

        var wasArmed = dragArmed;
        var appointment = floatingAppointment;

        dragArmed = false;
        dragView = null;
        pressedView = null;

        if (!wasArmed || appointment is null || floatingView is null)
            return;

        SetScrollingEnabled(true);
        gutter.ClearIndicator();
        HideDraggedAppointment();

        if (!committed)
        {
            RepopulateAllSlots();
            return;
        }

        var args = new SchedulerAppointmentDroppedEventArgs(Resolve(appointment), dragDropStart);
        AppointmentDropped?.Invoke(this, args);

        if (args.Cancel)
        {
            RepopulateAllSlots();
            return;
        }

        // Accepted: the faded original moves to where it was dropped and comes back to full strength,
        // taking over from the overlay. It stays there deliberately — the host's update is
        // asynchronous, and snapping back only to jump forward again would read as a glitch — until
        // the change is fed back through ItemsSource.
        MoveFloatingViewToDropPosition();
    }

    private void MoveFloatingViewToDropPosition()
    {
        if (floatingView is null)
            return;

        var dayIndex = Math.Clamp(
            DateOnly.FromDateTime(dragDropStart).DayNumber - slots[1].PageStart.DayNumber,
            0,
            geometry.VisibleDays - 1);

        AbsoluteLayout.SetLayoutFlags(floatingView, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(floatingView, new Rect(
            dayIndex * geometry.DayWidth + TimelineSurface.AppointmentGap,
            geometry.YFromMinutes(dragDropStart.TimeOfDay.TotalMinutes),
            dragOriginalBounds.Width,
            dragOriginalBounds.Height));

        floatingView.TranslationX = geometry.ViewportWidth;
        floatingView.TranslationY = 0;
        floatingView.Opacity = 1;
        floatingView.ZIndex = AppointmentZIndex;
    }
}
