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

    /// <summary>Rebuilds a page's day headers, which is what a change of day count needs.</summary>
    /// <summary>How many columns a page's header has, which is not the same question in each mode.</summary>
    private int HeaderColumns => ViewMode switch
    {
        SchedulerViewMode.Month => MonthGeometry.Columns,
        SchedulerViewMode.Timeline => geometry.VisibleDays,
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
        Recentre();
    }

    private void SyncSlotStarts()
    {
        for (var i = 0; i < slots.Length; i++)
            ActiveGeometry.SlotStarts[i] = slots[i].PageStart;

        gridView.Invalidate();
    }

    /// <summary>Rebinds one week from scratch: releases its views, re-lays out, and re-rents.</summary>
    /// <summary>Brings one week's appointment views in line with the data, reusing what is already there.</summary>
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
        UpdateSlotHeader(slot, slotIndex);

        IReadOnlyList<IAppointmentPlacement> positions =
            ActiveTemplate is null || ItemsSource is null || ActiveGeometry.ViewportWidth <= 0
                ? []
                : pageSurface.Layout(LayoutItems(), slot.PageStart);

        // Index what this week already has by the identity of what it is showing. Reused across
        // calls: this runs three times per rebuild, and again for every page rotation.
        reusableByKey.Clear();
        var available = reusableByKey;

        foreach (var view in slot.Views)
        {
            if (!appointmentsByView.TryGetValue(view, out var bound))
                continue;

            // Two views on one page showing the same key is out of contract (section 9), but the
            // dictionary would quietly drop whichever came first — leaving it visible, owned by no
            // page, and drifting over whatever scrolled past. Hand it straight back instead.
            if (available.TryGetValue(bound.Key, out var displaced))
                Discard(displaced);

            available[bound.Key] = view;
        }

        var arranged = new List<View>(positions.Count);

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

        // Whatever no appointment claimed is genuinely gone from this week.
        foreach (var surplus in available.Values)
            Discard(surplus);

        slot.Views.Clear();
        slot.Views.AddRange(arranged);
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

        appointmentsByView.TryGetValue(view, out var previous);

        view.BindingContext = appointment;

        // Only when the text would actually differ. Three date formats — one of them the long date
        // pattern — plus a semantic write is the largest per-appointment cost here, and a reload that
        // changed nothing used to pay it for every appointment on all three pages.
        if (!DescribesTheSame(previous, appointment))
            SetAppointmentSemantics(view, appointment);

        appointmentsByView[view] = appointment;
        slotsByView[view] = slot;

        PositionAppointmentView(view, position, slotIndex);
    }

    /// <summary>Whether two appointments would produce the same accessibility description.</summary>
    private static bool DescribesTheSame(ISchedulerAppointment? previous, ISchedulerAppointment current) =>
        previous is not null
        && previous.Start == current.Start
        && previous.End == current.End
        && previous.Subject == current.Subject;

    private void SetAppointmentSemantics(View view, ISchedulerAppointment appointment)
    {
        var culture = CultureInfo.CurrentUICulture;
        var range = $"{appointment.Start.ToString(TimeFormat, culture)} - {appointment.End.ToString(TimeFormat, culture)}";
        var day = appointment.Start.ToString("D", culture);

        SemanticProperties.SetDescription(view, string.IsNullOrEmpty(appointment.Subject)
            ? $"{day}, {range}"
            : $"{appointment.Subject}, {day}, {range}");
    }

    /// <summary>Forgets a view and hands it back to the pool.</summary>
    private void Discard(View view)
    {
        appointmentsByView.Remove(view);
        slotsByView.Remove(view);
        pool.Return(view);
    }

    private void ReleaseSlot(PageSlot slot)
    {
        foreach (var view in slot.Views)
            Discard(view);

        slot.Views.Clear();
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
        view.TranslationX = slotIndex * ActiveGeometry.PageSpan + ActiveGeometry.AnimationOffsetX;
        view.TranslationY = 0;
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
