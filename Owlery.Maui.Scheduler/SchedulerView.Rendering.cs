using System.Globalization;
using Microsoft.Maui.Layouts;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>Turning appointments into placed views, and the day-count transition.</summary>
public partial class SchedulerView
{
    private PageSlot CreateSlot()
    {
        var header = new Grid();

        headerSurface.Add(header);

        return new PageSlot { Header = header };
    }

    /// <summary>How many columns a page's header has, which is not the same question in each mode.</summary>
    private int HeaderColumns => ViewMode switch
    {
        SchedulerViewMode.Month => MonthGeometry.Columns,
        SchedulerViewMode.Timeline => geometry.VisibleDays,
        // An agenda names its days down the list rather than across the top of one.
        SchedulerViewMode.Agenda => 0,
        _ => throw new NotSupportedException($"{ViewMode} has no header columns."),
    };

    /// <summary>
    /// Whether a page's header is built for the surface currently showing.
    /// </summary>
    /// <remarks>
    /// The column count alone does not answer this. A week and a month are both seven columns wide,
    /// but only the week has day numbers — so a header built for one fitted the other by width, was
    /// left alone, and then the timeline indexed number labels a month header never made. Which way
    /// it broke depended on the order a host pushed <c>ViewMode</c> and <c>VisibleDays</c>, so it
    /// crashed only sometimes.
    /// </remarks>
    private bool SlotHeaderMatchesMode(PageSlot slot) =>
        slot.DayNameLabels.Length == HeaderColumns
        && slot.DayNumberLabels.Length == (ViewMode is SchedulerViewMode.Timeline ? HeaderColumns : 0);

    /// <summary>
    /// Builds one page's header.
    /// </summary>
    /// <remarks>
    /// A month gets one of these per page too, rather than a single fixed row. The columns do mean
    /// the same thing on every month — every page starts on <see cref="FirstDayOfWeek"/> — but a row
    /// that stays put while the pages slide under it reads as though the calendar has come apart from
    /// its own heading. Moving with the page costs three more label rows and looks like one object.
    /// </remarks>
    private void BuildSlotHeader(PageSlot slot)
    {
        var columns = HeaderColumns;

        slot.Header.Clear();
        slot.Header.ColumnDefinitions =
            [.. Enumerable.Range(0, columns).Select(_ => new ColumnDefinition(GridLength.Star))];

        var header = slot.Header;
        var nameLabels = new Label[columns];
        var numberLabels = ViewMode is SchedulerViewMode.Timeline ? new Label[columns] : [];

        for (var day = 0; day < columns; day++)
        {
            var stack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 6) };

            nameLabels[day] = new Label
            {
                FontSize = 11,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = SecondaryTextColor
            };

            stack.Add(nameLabels[day]);

            // A month names its columns and stops there; the day numbers belong in the cells, where
            // they are painted with the grid.
            if (numberLabels.Length > 0)
            {
                numberLabels[day] = new Label
                {
                    FontSize = 16,
                    HorizontalTextAlignment = TextAlignment.Center
                };

                stack.Add(numberLabels[day]);
            }

            header.Add(stack, day);
        }

        slot.DayNameLabels = nameLabels;
        slot.DayNumberLabels = numberLabels;
    }

    private void RebuildAll(DateOnly centrePage)
    {
        // Same reasoning as in SnapAsync: do not carry a waiting drop across a change of period.
        if (!dragArmed)
            ReleaseFloatingAppointment();

        slots[0].PageStart = pageSurface.PreviousPage(centrePage);
        slots[1].PageStart = centrePage;
        slots[2].PageStart = pageSurface.NextPage(centrePage);

        for (var i = 0; i < slots.Length; i++)
            PopulateSlot(slots[i], i);

        SyncSlotStarts();
        UpdateSelectionView();
        RaiseVisibleDatesChanged();
        ApplyContentExtent();
        Recentre();
    }

    private void SyncSlotStarts()
    {
        for (var i = 0; i < slots.Length; i++)
            ActiveGeometry.SlotStarts[i] = slots[i].PageStart;

        gridView.Invalidate();
    }

    /// <summary>Brings one slot's appointment views in line with the data, reusing what is already there.</summary>
    /// <remarks>
    /// This reconciles rather than rebuilds. Releasing every view and renting them back would hide and
    /// re-show each one, and the pool is a stack, so the views would come back in reverse order — each
    /// bound to a *different* appointment than before. A refresh that returned identical data would
    /// still repaint every appointment on screen, which reads as a flash.
    /// <para>
    /// Views are therefore matched to appointments by <see cref="ISchedulerAppointment.Key"/>: a view
    /// already showing an appointment keeps showing it, is rebound to an equivalent item, renders
    /// identically, and nothing visibly happens. Only a genuine surplus or shortfall touches the pool.
    /// </para>
    /// <para>
    /// Reusing them positionally instead would be cheaper and is what this used to do, but it only
    /// holds while the page's contents are unchanged. Insert one appointment in the morning and every
    /// view after it is rebound to a different item — a repaint of the whole page for one arrival,
    /// which is the case the reconciliation exists to avoid.
    /// </para>
    /// </remarks>
    private void PopulateSlot(PageSlot slot, int slotIndex)
    {
        if (ViewMode is SchedulerViewMode.Agenda && slotIndex != CentreSlot)
        {
            ReleaseSlot(slot);
            return;
        }

        UpdateSlotHeader(slot, slotIndex);

        IReadOnlyList<IAppointmentPlacement> positions;

        if (ActiveTemplate is null || ItemsSource is null || ActiveGeometry.ViewportWidth <= 0)
        {
            positions = [];

            // Nothing to lay out — and the agenda answers SectionsFor from its last slice, so leaving
            // it alone would keep the previous window's headings and day markers on screen, placed
            // over the space every appointment row has just been released from.
            if (ViewMode is SchedulerViewMode.Agenda)
                agendaSurface.ClearWindow();
        }
        else
        {
            positions = pageSurface.Layout(LayoutItems(), slot.PageStart);
        }

        if (ViewMode is SchedulerViewMode.Agenda)
        {
            var shift = agendaSurface.TakeContentShift();

            if (Math.Abs(shift) >= 0.5)
            {
                ShiftContentAbove(shift);
                positions = agendaSurface.Slice();
            }
        }

        // Index what this slot already has by the identity of what it is showing. Reused across
        // calls: this runs three times per rebuild, and again for every page rotation.
        reusableByKey.Clear();
        var available = reusableByKey;

        foreach (var view in slot.Views)
        {
            if (!appointmentsByView.TryGetValue(view, out var bound))
            {
                // On a page but bound to nothing, so no position can claim it and the clear below is
                // the last reference to it. Skipping it left it drawn and owned by nobody — see
                // UnownedViews.
                Discard(view);
                continue;
            }

            // Two views on one page showing the same key is out of contract (section 9), but the
            // dictionary would quietly drop whichever came first — leaving it visible, owned by no
            // page, and drifting over whatever scrolled past. Hand it straight back instead.
            if (available.TryGetValue(bound.Key, out var displaced))
                Discard(displaced);

            available[bound.Key] = view;
        }

        reusableArrangedViews.Clear();
        reusableArrangedViews.EnsureCapacity(positions.Count);
        var arranged = reusableArrangedViews;

        foreach (var position in positions)
        {
            if (!available.Remove(position.Appointment.Key, out var view))
            {
                view = pool.Rent();

                if (view is null)
                    break;
            }

            arranged.Add(view);
            BindAppointmentView(view, position, slot, slotIndex);
        }

        slot.Positions.Clear();

        // AddRange over a Take iterator cannot pre-size the list, and in the ordinary case there is
        // nothing to take — every position found a view.
        if (arranged.Count == positions.Count)
        {
            slot.Positions.AddRange(positions);
        }
        else
        {
            for (var i = 0; i < arranged.Count; i++)
                slot.Positions.Add(positions[i]);
        }

        // Whatever no appointment claimed is genuinely gone from this slot.
        foreach (var surplus in available.Values)
            Discard(surplus);

        slot.Views.Clear();
        slot.Views.AddRange(arranged);

        PopulateSections(slot, slotIndex);
        MeasureAgendaRows(slot, slotIndex);
    }

    /// <summary>
    /// Places the agenda's headings and day markers, reconciled by the date they name.
    /// </summary>
    /// <remarks>
    /// The same shape as the appointment reconciliation above, keyed by date and kind instead of by
    /// appointment. They are kept in their own list rather than among <see cref="PageSlot.Views"/>
    /// because hit-testing walks that one and resolves what it finds to an appointment — a heading in
    /// there would be picked up as a press and then do nothing at all.
    /// </remarks>
    private void PopulateSections(PageSlot slot, int slotIndex)
    {
        if (ViewMode is not SchedulerViewMode.Agenda)
        {
            ReleaseSectionViews(slot);
            return;
        }

        var sections = pageSurface.SectionsFor(slot.PageStart);

        sectionPool.Template ??= ActiveSectionTemplate;

        reusableSections.Clear();
        var spare = reusableSections;

        foreach (var existing in slot.SectionViews)
        {
            if (existing.BindingContext is not SchedulerAgendaSection section)
            {
                sectionPool.Return(existing);
                continue;
            }

            var key = (section.Date, section.Kind);

            if (!spare.TryAdd(key, existing))
                sectionPool.Return(existing);
        }

        slot.SectionViews.Clear();

        foreach (var placement in sections)
        {
            var key = (placement.Section.Date, placement.Section.Kind);

            if (!spare.Remove(key, out var view))
            {
                view = sectionPool.Rent();

                if (view is null)
                    break;
            }

            slot.SectionViews.Add(view);
            BindSectionView(view, placement, slotIndex);
        }

        foreach (var surplus in spare.Values)
            sectionPool.Return(surplus);
    }

    private void BindSectionView(View view, AgendaSectionPlacement placement, int slotIndex)
    {
        var wasShowing = view.BindingContext as SchedulerAgendaSection;

        if (wasShowing != placement.Section)
            view.BindingContext = placement.Section;

        if (view is AgendaSectionView built)
        {
            built.EmptyText = AgendaEmptyText;
            built.UpdateAppearance(PrimaryTextColor, SecondaryTextColor);
        }

        // A heading is what a screen reader uses to find its way down the list, so it is described
        // rather than left as decoration — and only rewritten when it actually says something new.
        if (wasShowing is null || wasShowing.Date != placement.Section.Date || wasShowing.Kind != placement.Section.Kind)
        {
            SemanticProperties.SetDescription(
                view,
                placement.Section.Date.ToString("D", CultureInfo.CurrentUICulture));
        }

        if (AbsoluteLayout.GetLayoutBounds(view) != placement.Bounds)
        {
            AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(view, placement.Bounds);
        }

        view.TranslationX = slotIndex * ActiveGeometry.PageSpan + ActiveGeometry.AnimationOffsetX;
        view.TranslationY = 0;
        view.ZIndex = AppointmentZIndex;
    }

    /// <summary>
    /// Measures the agenda rows just placed, and corrects the table where the estimate was wrong.
    /// </summary>
    /// <remarks>
    /// The one place in this control that measures anything. Every other surface knows a view's size
    /// before it builds it — a timeline from the appointment's duration, a month from a fixed chip —
    /// but an agenda row is as tall as its content, and its content is the host's.
    /// <para>
    /// A row is measured once. The result is kept on the row, so scrolling back over it costs
    /// nothing, and a correction reflows only the rows below the first one that moved.
    /// </para>
    /// </remarks>
    private void MeasureAgendaRows(PageSlot slot, int slotIndex)
    {
        if (ViewMode is not SchedulerViewMode.Agenda)
            return;

        List<(AgendaRow Row, double Height)>? measured = null;

        for (var i = 0; i < slot.Views.Count && i < slot.Positions.Count; i++)
        {
            if (slot.Positions[i] is not AgendaPlacement placement || placement.Row.Measured)
                continue;

            var bounds = agendaGeometry.RowBounds(placement.Row);
            var size = slot.Views[i].Measure(bounds.Width, double.PositiveInfinity);

            (measured ??= []).Add((placement.Row, size.Height));
        }

        if (measured is null)
        {
            ApplyContentExtent();
            return;
        }

        var correction = agendaSurface.ApplyMeasuredHeights(measured);

        ApplyContentExtent();
        ShiftContentAbove(correction.ShiftAbove);

        if (!correction.LayoutChanged)
            return;

        // Corrected rows have moved, and shorter ones may have brought more into view. Running the
        // pass again re-places what is showing and realizes anything newly revealed; the cap is what
        // keeps a template that cannot settle on a height from taking the frame with it.
        if (agendaMeasurePasses >= MaxAgendaMeasurePasses)
            return;

        agendaMeasurePasses++;

        try
        {
            PopulateSlot(slot, slotIndex);
        }
        finally
        {
            agendaMeasurePasses--;
        }
    }

    /// <summary>Hands a page's headings back, without touching the appointments beside them.</summary>
    private void ReleaseSectionViews(PageSlot slot)
    {
        foreach (var view in slot.SectionViews)
            sectionPool.Return(view);

        slot.SectionViews.Clear();
    }

    /// <summary>
    /// Every view a page is currently showing, plus the drag's own while there is one.
    /// </summary>
    /// <remarks>
    /// Exists so the tests can hold the control to the rule that nothing else is drawn. A view that
    /// is visible and on no page reads as an appointment and then refuses to be pressed —
    /// <c>HitTestAppointment</c> walks the pages, so the press falls through to the cell underneath —
    /// and because only a page's views are translated when the weeks rotate, it stays on the column
    /// it was last left at while every other week scrolls past it. The drag's view is the one
    /// legitimate exception: it is deliberately out of the pages while it is being carried, and again
    /// while an accepted drop waits for the host to feed the change back. So is the follower on the
    /// overlay, for as long as a drag is armed.
    /// </remarks>
    internal IEnumerable<View> PlacedViews
    {
        get
        {
            foreach (var slot in slots)
            {
                foreach (var view in slot.Views)
                    yield return view;

                foreach (var view in slot.SectionViews)
                    yield return view;
            }

            if (floatingView is not null)
                yield return floatingView;

            // The follower on the overlay, which is legitimate only for as long as a finger is
            // carrying it. It is not on the scrolling surface and it takes no input, so one left
            // behind sits over the calendar at a fixed place and cannot be pressed.
            if (dragArmed && dragOverlayView is not null)
                yield return dragOverlayView;
        }
    }

    /// <summary>
    /// The appointments the weeks lay out. An appointment being dragged is excluded: it is represented
    /// by a floating view that belongs to no week, which is what lets the weeks rotate underneath it
    /// while it stays under the finger.
    /// </summary>
    private IEnumerable<ISchedulerAppointment> LayoutItems()
    {
        var items = ItemsSource ?? [];

        // Matched by key, not by instance: the collection may have been rebuilt since the drag began.
        return floatingAppointment is null
            ? items
            : items.Where(item => !Equals(item.Key, floatingAppointment.Key));
    }

    /// <summary>
    /// Exchanges an appointment for whichever instance currently represents it.
    /// </summary>
    /// <remarks>
    /// Everything the control hands back to the host goes through here, so a handler that acts on
    /// what it was given is acting on something the host is still displaying. Without it, an
    /// appointment picked up before a reload would be reported afterwards as an orphan, and mutating
    /// it would silently do nothing.
    /// </remarks>
    private ISchedulerAppointment Resolve(ISchedulerAppointment appointment)
    {
        if (ItemsSource is null)
            return appointment;

        foreach (var item in ItemsSource)
        {
            if (Equals(item.Key, appointment.Key))
                return item;
        }

        return appointment;
    }

    private void BindAppointmentView(View view, IAppointmentPlacement position, PageSlot slot, int slotIndex)
    {
        var appointment = position.Appointment;

        if (!ReferenceEquals(view.BindingContext, appointment))
            view.BindingContext = appointment;

        // Only when the text would actually differ. Three date formats — one of them the long date
        // pattern — plus a semantic write is the largest per-appointment cost here, and a reload that
        // changed nothing used to pay it for every appointment on all three pages.
        var described = (
            Start: appointment.StartIn(TimeZone),
            End: appointment.EndIn(TimeZone),
            appointment.Subject);

        if (!describedByView.TryGetValue(view, out var previouslyDescribed) || previouslyDescribed != described)
        {
            SetAppointmentSemantics(view, described.Start, described.End, described.Subject);
            describedByView[view] = described;
        }

        appointmentsByView[view] = appointment;
        slotsByView[view] = slot;

        PositionAppointmentView(view, position, slotIndex);
    }

    /// <remarks>Times are the ones already resolved into <see cref="TimeZone"/>, not the host's raw values.</remarks>
    private void SetAppointmentSemantics(View view, DateTime start, DateTime end, string? subject)
    {
        var culture = CultureInfo.CurrentUICulture;
        var range = $"{start.ToString(TimeFormat, culture)} - {end.ToString(TimeFormat, culture)}";
        var day = start.ToString("D", culture);

        SemanticProperties.SetDescription(view, string.IsNullOrEmpty(subject)
            ? $"{day}, {range}"
            : $"{subject}, {day}, {range}");
    }

    /// <summary>Forgets a view and hands it back to the pool.</summary>
    private void Discard(View view)
    {
        appointmentsByView.Remove(view);
        describedByView.Remove(view);
        slotsByView.Remove(view);
        pool.Return(view);
    }

    private void ReleaseSlot(PageSlot slot)
    {
        foreach (var view in slot.Views)
            Discard(view);

        slot.Views.Clear();
        ReleaseSectionViews(slot);
    }

    private void UpdateSlotHeader(PageSlot slot, int slotIndex)
    {
        var culture = CultureInfo.CurrentUICulture;

        if (ViewMode is not SchedulerViewMode.Timeline)
        {
            for (var column = 0; column < slot.DayNameLabels.Length; column++)
            {
                var day = (DayOfWeek)(((int)FirstDayOfWeek + column) % 7);

                slot.DayNameLabels[column].Text = culture.DateTimeFormat
                    .GetAbbreviatedDayName(day)
                    .ToUpper(culture);
                slot.DayNameLabels[column].TextColor = SecondaryTextColor;
            }

            slot.Header.TranslationX = slotIndex * ActiveGeometry.PageSpan + ActiveGeometry.AnimationOffsetX;
            return;
        }

        var today = DateOnly.FromDateTime(geometry.Now);

        for (var day = 0; day < slot.DayNameLabels.Length; day++)
        {
            var date = slot.PageStart.AddDays(day);
            var isToday = ShowCurrentDayHighlight && date == today;

            slot.DayNameLabels[day].Text = culture.DateTimeFormat
                .GetAbbreviatedDayName(date.DayOfWeek)
                .ToUpper(culture);
            slot.DayNameLabels[day].TextColor = SecondaryTextColor;

            var number = slot.DayNumberLabels[day];
            number.Text = date.Day.ToString(culture);
            number.FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None;
            number.TextColor = isToday ? CurrentDayTextColor : PrimaryTextColor;
        }

        slot.Header.TranslationX = slotIndex * geometry.PageSpan + geometry.AnimationOffsetX;
    }

    private void PositionAppointmentView(View view, IAppointmentPlacement position, int slotIndex)
    {
        // Only write bounds that actually changed: an unchanged write still costs a layout pass.
        var bounds = pageSurface.BoundsFor(position);

        if (AbsoluteLayout.GetLayoutBounds(view) != bounds)
        {
            AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(view, bounds);
        }

        // The slot offset lives in TranslationX so rotating pages never triggers a layout pass.
        var translationX = slotIndex * ActiveGeometry.PageSpan + ActiveGeometry.AnimationOffsetX;

        if (Math.Abs(view.TranslationX - translationX) >= 0.5)
            view.TranslationX = translationX;

        if (Math.Abs(view.TranslationY) >= 0.5)
            view.TranslationY = 0;

        if (view.ZIndex != AppointmentZIndex)
            view.ZIndex = AppointmentZIndex;
    }

    /// <summary>Moves an untouched week to a new physical position — the cheap half of a rotation.</summary>
    private void ShiftSlot(PageSlot slot, int slotIndex)
    {
        var offset = slotIndex * ActiveGeometry.PageSpan;

        foreach (var view in slot.Views)
            view.TranslationX = offset;

        slot.Header.TranslationX = offset;
    }
}
