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
internal sealed class CellSelectionOverlay(
    AbsoluteLayout host,
    int zIndex,
    Color initialBackgroundColor,
    Color initialBorderColor,
    Color initialTextColor)
{
    /// <summary>Point size of the built-in "+" when the slot has room for it.</summary>
    private const double GlyphFontSize = 20;

    /// <summary>Line height a glyph occupies, as a multiple of its point size.</summary>
    private const double GlyphLineHeightRatio = 1.4;

    /// <summary>Below this the box is left empty rather than filled with an illegible mark.</summary>
    private const double SmallestGlyphBox = 10;

    private View? view;
    private Border? defaultBorder;
    private Label? defaultLabel;
    private Color backgroundColor = initialBackgroundColor;
    private Color borderColor = initialBorderColor;
    private Color textColor = initialTextColor;

    /// <summary>Optional host-supplied appearance. Changing it discards the view built from the old one.</summary>
    public DataTemplate? Template { get; set; }

    public void Reset()
    {
        if (view is null)
            return;

        host.Remove(view);
        view = null;
        defaultBorder = null;
        defaultLabel = null;
    }

    /// <summary>Updates the built-in affordance without touching a host-supplied template.</summary>
    public void UpdateAppearance(Color background, Color border, Color text)
    {
        backgroundColor = background;
        borderColor = border;
        textColor = text;

        if (defaultBorder is not null)
        {
            defaultBorder.BackgroundColor = background;
            defaultBorder.Stroke = border;
        }

        if (defaultLabel is not null)
            defaultLabel.TextColor = text;
    }

    /// <summary>Places the marker on the given slot, or hides it when nothing is selected or on screen.</summary>
    public void Update(
        SchedulerTimeSlot? selected,
        PageSlot[] pages,
        PageGeometry geometry,
        ISchedulerSurface surface,
        string timeFormat)
    {
        if (selected is not { } slot || geometry.ViewportWidth <= 0)
        {
            Hide();
            return;
        }

        var pageIndex = PageIndexFor(DateOnly.FromDateTime(slot.Start.WallClock), pages, surface);

        if (pageIndex < 0)
        {
            Hide();
            return;
        }

        view ??= Create();
        view.BindingContext = slot;
        view.IsVisible = true;

        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
        var requested = surface.BoundsFor(slot, pages[pageIndex].PageStart);
        AbsoluteLayout.SetLayoutBounds(view, requested);
        FitGlyph(requested.Height);
        view.TranslationX = pageIndex * geometry.PageSpan + geometry.AnimationOffsetX;

        var culture = CultureInfo.CurrentUICulture;
        SemanticProperties.SetDescription(
            view,
            $"{slot.Start.WallClock.ToString("D", culture)} {slot.Start.WallClock.ToString(timeFormat, culture)}");
    }

    /// <summary>Which rendered page holds a date, or -1 when none of them do.</summary>
    private static int PageIndexFor(DateOnly date, PageSlot[] pages, ISchedulerSurface surface)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            if (surface.PageContains(pages[i].PageStart, date))
                return i;
        }

        return -1;
    }

    private void Hide()
    {
        if (view is not null)
            view.IsVisible = false;
    }

    /// <summary>Sizes the built-in glyph to the box, or drops it when there is no room for one.</summary>
    /// <remarks>
    /// A quarter hour at the default hour height is 12.5dp and a 20pt "+" wants 27, so the label
    /// overflowed its border and was left showing whichever part of itself happened to fall inside —
    /// on Android, the bottom of the stroke, which reads as a stray tick rather than a plus. Nothing
    /// here touches a host-supplied template: a host that provides its own view sizes it itself.
    /// </remarks>
    private void FitGlyph(double height)
    {
        if (defaultLabel is null)
            return;

        defaultLabel.IsVisible = height >= SmallestGlyphBox;
        defaultLabel.FontSize = Math.Min(GlyphFontSize, height / GlyphLineHeightRatio);
    }

    private View Create()
    {
        if (Template?.CreateContent() is View custom)
        {
            Prepare(custom);
            return custom;
        }

        defaultLabel = new Label
        {
            Text = "+",
            FontSize = GlyphFontSize,
            TextColor = textColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        defaultBorder = new Border
        {
            BackgroundColor = backgroundColor,
            Stroke = borderColor,
            StrokeThickness = 1,
            Content = defaultLabel
        };

        Prepare(defaultBorder);
        return defaultBorder;
    }

    private void Prepare(View created)
    {
        created.InputTransparent = true;
        created.ZIndex = zIndex;
        host.Add(created);
    }
}
