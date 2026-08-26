using System.Globalization;
using Microsoft.Maui.Layouts;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>Taps, cell selection, and drag-and-drop.</summary>
public partial class SchedulerView
{
    private void UpdateSelectionView() =>
        cellSelection.Update(SelectedSlot, slots, MinimumAppointmentHeight, TimeFormat);

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
        if (e.Touches.Length == 0 || geometry.ViewportWidth <= 0)
            return;

        var point = new Point(e.Touches[0].X, e.Touches[0].Y);

        interactionStart = point;
        interactionMoved = false;
        pressedView = HitTestAppointment(point);

        if (pressedView is not null && AllowDragAndDrop)
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

        if (ResolveSlot(new Point(e.Touches[0].X, e.Touches[0].Y)) is not { } slot)
            return;

        SelectedSlot = slot;
        CellTapped?.Invoke(this, new SchedulerCellTappedEventArgs(slot));
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

    private SchedulerTimeSlot? ResolveSlot(Point point)
    {
        if (geometry.ViewportWidth <= 0)
            return null;

        var slotIndex = Math.Clamp((int)(point.X / geometry.ViewportWidth), 0, SchedulerGeometry.SlotCount - 1);
        var xInSlot = point.X - slotIndex * geometry.ViewportWidth;
        var dayIndex = Math.Clamp((int)(xInSlot / geometry.DayWidth), 0, geometry.VisibleDays - 1);

        var snap = Math.Max(1, SnapMinutes);
        var minutes = geometry.MinutesFromY(point.Y);
        var snapped = Math.Floor(minutes / snap) * snap;
        snapped = Math.Clamp(snapped, geometry.WindowStartMinutes, geometry.WindowEndMinutes - snap);

        var date = slots[slotIndex].PageStart.AddDays(dayIndex);
        var start = date.ToDateTime(TimeOnly.MinValue).AddMinutes(snapped);

        return new SchedulerTimeSlot(start, TimeSpan.FromMinutes(snap));
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
        SetScrollingEnabled(true);
        ClearDragIndicator();
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
    /// One-time platform setup for the two scroll views.
    /// </summary>
    /// <remarks>
    /// A UIScrollView holds back touchesBegan while it decides whether a touch is the start of a
    /// scroll. The long press that picks an appointment up needs to know about the press the moment
    /// the finger lands, so that delay is turned off.
    /// </remarks>
    private void ConfigurePlatformScrolling()
    {
#if IOS || MACCATALYST
        if (pagerScroll.Handler?.PlatformView is UIKit.UIScrollView horizontalPlatformScroll)
        {
            horizontalPlatformScroll.DelaysContentTouches = false;

            // Paging makes the platform commit to a week the moment the finger leaves the screen,
            // using the release velocity. Without it the decision waits for inertia to run out, which
            // can take a second or more, and the user sees the content drift to a stop and only then
            // slide again to settle on a week. The scroll view's frame is exactly one viewport and
            // its content exactly three, so page boundaries already fall on 0 / W / 2W.
            horizontalPlatformScroll.PagingEnabled = true;

            // Bouncing here would pull the outermost of the three rendered weeks away from the edge
            // and show empty surface behind it. There is no end of the calendar to bounce against —
            // the weeks are a ring buffer — so the rubber-banding is reporting something untrue.
            // It also keeps the scroll offset inside 0..2W, which the snap arithmetic and the
            // hand-mirrored day headers both assume.
            horizontalPlatformScroll.Bounces = false;
        }

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
    /// Stops the two scroll views from competing with an armed drag.
    /// </summary>
    /// <remarks>
    /// This is the one place the control reaches past MAUI. <c>IsEnabled = false</c> is not usable:
    /// it disables interaction for the whole subtree and cancels the very touch that is driving the
    /// drag. Both platforms expose a way to stop scrolling while still delivering touches.
    /// </remarks>
    private void SetScrollingEnabled(bool enabled)
    {
#if IOS || MACCATALYST
        if (pagerScroll.Handler?.PlatformView is UIKit.UIScrollView horizontalPlatformScroll)
            horizontalPlatformScroll.ScrollEnabled = enabled;

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

        // While a period change is sliding, the pager is between pages. Touch points arrive in
        // surface coordinates that include a scroll offset which is still moving, and the maths below
        // assumes the pager is at rest on the centre slot — so a column resolved now belongs to a week
        // that is only half on screen, and the appointment jumps a whole page sideways. Hold the
        // target instead; PageDuringDragAsync recomputes from lastDragPoint once the slide settles.
        if (pagingDuringDrag)
            return;

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
            SnapMinutes);

        var snappedX = target.XWithinPage(geometry, AppointmentGap);
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

        // Shown in the time gutter rather than over the grid: anywhere near the appointment is under
        // the finger doing the dragging, which is precisely where it cannot be read.
        if (ShowDragTimeIndicator)
        {
            gutterDrawable.HighlightMinutes = target.SnappedMinutes;
            gutterDrawable.HighlightText = dragDropStart.ToString(TimeFormat, CultureInfo.CurrentUICulture);
            gutterView.Invalidate();
        }

        UpdateEdgePaging(point);
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

    private void ClearDragIndicator()
    {
        gutterDrawable.HighlightMinutes = null;
        gutterDrawable.HighlightText = null;
        gutterView.Invalidate();
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
            var forward = edgePagingDirection > 0;

            if (forward)
                Advance();
            else
                Retreat();

            // The ghost marks a slot in the week the drag started from, so it travels with that week
            // and slides off screen once the drag has moved on.
            if (floatingView is not null)
                floatingView.TranslationX += forward ? -geometry.ViewportWidth : geometry.ViewportWidth;

            SyncSlotStarts();
            UpdateSelectionView();
            SyncDisplayDate();
            RaiseVisibleDatesChanged();

            // Rotating swaps the weeks without moving anything, so on its own the calendar simply
            // changes contents and it is hard to see that anything happened. Instead, jump to where
            // the outgoing week has landed — visually identical to the frame before — and then slide
            // across to the centre, so the change reads as the same motion as a swipe.
            var outgoing = forward ? 0 : geometry.SurfaceWidth - geometry.ViewportWidth;

            await pagerScroll.ScrollToAsync(outgoing, 0, false);
            await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, true);

            lastScrollX = geometry.ViewportWidth;
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

        var wasArmed = dragArmed;
        var appointment = floatingAppointment;

        dragArmed = false;
        dragView = null;
        pressedView = null;

        if (!wasArmed || appointment is null || floatingView is null)
            return;

        SetScrollingEnabled(true);
        ClearDragIndicator();
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
            dayIndex * geometry.DayWidth + AppointmentGap,
            geometry.YFromMinutes(dragDropStart.TimeOfDay.TotalMinutes),
            dragOriginalBounds.Width,
            dragOriginalBounds.Height));

        floatingView.TranslationX = geometry.ViewportWidth;
        floatingView.TranslationY = 0;
        floatingView.Opacity = 1;
        floatingView.ZIndex = AppointmentZIndex;
    }
}
