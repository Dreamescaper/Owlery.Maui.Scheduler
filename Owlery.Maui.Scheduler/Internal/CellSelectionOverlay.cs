using System.Globalization;
using Microsoft.Maui.Layouts;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// The marker drawn over the selected empty cell, and the only view that represents it.
/// </summary>
/// <remarks>
/// One instance is enough — only one cell is ever selected — so it is created on first use and
/// re-placed afterwards. It is anchored to a column like an appointment, which is why it has to be
/// re-placed whenever the columns move, not only when the selection changes.
/// </remarks>
internal sealed class CellSelectionOverlay(AbsoluteLayout host, SchedulerGeometry geometry, int zIndex)
{
    private View? view;

    /// <summary>Optional host-supplied appearance. Changing it discards the view built from the old one.</summary>
    public DataTemplate? Template { get; set; }

    public void Reset()
    {
        if (view is null)
            return;

        host.Remove(view);
        view = null;
    }

    /// <summary>Places the marker on the given slot, or hides it when nothing is selected or on screen.</summary>
    public void Update(SchedulerTimeSlot? selected, PageSlot[] pages, double minimumHeight, string timeFormat)
    {
        if (selected is not { } slot || geometry.ViewportWidth <= 0)
        {
            Hide();
            return;
        }

        var pageIndex = PageIndexFor(DateOnly.FromDateTime(slot.Start), pages);

        if (pageIndex < 0)
        {
            Hide();
            return;
        }

        view ??= Create();
        view.BindingContext = slot;
        view.IsVisible = true;

        var dayIndex = DateOnly.FromDateTime(slot.Start).DayNumber - pages[pageIndex].PageStart.DayNumber;
        var y = geometry.YFromMinutes(slot.Start.TimeOfDay.TotalMinutes);
        var height = Math.Max(minimumHeight, geometry.YFromMinutes(slot.End.TimeOfDay.TotalMinutes) - y);

        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(view, new Rect(dayIndex * geometry.DayWidth, y, geometry.DayWidth, height));
        view.TranslationX = pageIndex * geometry.PageSpan + geometry.AnimationOffsetX;

        var culture = CultureInfo.CurrentUICulture;
        SemanticProperties.SetDescription(
            view,
            $"{slot.Start.ToString("D", culture)} {slot.Start.ToString(timeFormat, culture)}");
    }

    /// <summary>Which rendered page holds a date, or -1 when none of them do.</summary>
    private int PageIndexFor(DateOnly date, PageSlot[] pages)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            var offset = date.DayNumber - pages[i].PageStart.DayNumber;

            if (offset >= 0 && offset < geometry.VisibleDays)
                return i;
        }

        return -1;
    }

    private void Hide()
    {
        if (view is not null)
            view.IsVisible = false;
    }

    private View Create()
    {
        var created = Template?.CreateContent() as View ?? new Border
        {
            BackgroundColor = Color.FromArgb("#F3E8FC"),
            Stroke = Color.FromArgb("#DAB8F4"),
            StrokeThickness = 1,
            Content = new Label
            {
                Text = "+",
                FontSize = 20,
                TextColor = Color.FromArgb("#6B3FA0"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        created.InputTransparent = true;
        created.ZIndex = zIndex;
        host.Add(created);

        return created;
    }
}
