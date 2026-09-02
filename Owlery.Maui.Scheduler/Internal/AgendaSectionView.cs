using System.Globalization;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// The heading the agenda draws when the host has not supplied a template for one.
/// </summary>
/// <remarks>
/// Plain on purpose — a month name, a week range, or a weekday over a day number. It exists so the
/// agenda renders something legible out of the box, the way the selected-cell marker does, not to be
/// the design a host ships. Anything more opinionated belongs in
/// <see cref="SchedulerView.AgendaSectionTemplate"/>.
/// <para>
/// One view renders all three kinds. Both labels are built once and toggled, because a pooled view is
/// rebound rather than rebuilt and a heading that reshaped itself per bind would pay for a fresh
/// layout every time one scrolled into view.
/// </para>
/// </remarks>
internal sealed class AgendaSectionView : ContentView
{
    private readonly Label primary;
    private readonly Label secondary;
    private readonly Label detail;
    private readonly VerticalStackLayout stack;
    private Color primaryColor = Colors.Black;
    private Color secondaryColor = Colors.Gray;

    public AgendaSectionView()
    {
        primary = new Label { LineBreakMode = LineBreakMode.TailTruncation };
        secondary = new Label { LineBreakMode = LineBreakMode.TailTruncation };
        detail = new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation };

        stack = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start,
            Children = { secondary, primary, detail }
        };

        Content = stack;
    }

    public string EmptyText { get; set; } = "No appointments";

    /// <summary>Repaints the built-in heading. A host-supplied template paints itself.</summary>
    public void UpdateAppearance(Color primaryColor, Color secondaryColor)
    {
        this.primaryColor = primaryColor;
        this.secondaryColor = secondaryColor;
        Render();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        Render();
    }

    public void Refresh() => Render();

    private void Render()
    {

        if (BindingContext is not SchedulerAgendaSection section)
            return;

        var culture = CultureInfo.CurrentUICulture;

        primary.TextColor = primaryColor;
        secondary.TextColor = secondaryColor;
        detail.TextColor = secondaryColor;

        // A month and a week are headings to scan by — sit them in the middle of their strip so the
        // band reads evenly, rather than hugging its top.
        var scanning = section.Kind is SchedulerAgendaSectionKind.Month or SchedulerAgendaSectionKind.Week;
        stack.VerticalOptions = scanning ? LayoutOptions.Center : LayoutOptions.Start;

        switch (section.Kind)
        {
            case SchedulerAgendaSectionKind.Month:
                Show(
                    section.Date.ToString("MMMM yyyy", culture),
                    null,
                    section.AppointmentCount == 0 ? EmptyText : null,
                    22,
                    FontAttributes.Bold,
                    TextAlignment.Start);
                break;

            case SchedulerAgendaSectionKind.Week:
                // A small-caps kicker beneath the month: the range, muted, tracked out, carrying the
                // month's weight instead of competing with it.
                primary.TextColor = secondaryColor;
                Show(
                    WeekRange(section.Date, culture).ToUpper(culture),
                    null,
                    null,
                    11,
                    FontAttributes.None,
                    TextAlignment.Start,
                    letterSpacing: 1.2);
                break;

            case SchedulerAgendaSectionKind.Day:
                // Weekday above the number, which is what the gutter has room for.
                Show(
                    section.Date.Day.ToString(culture),
                    culture.DateTimeFormat.GetAbbreviatedDayName(section.Date.DayOfWeek).ToUpper(culture),
                    null,
                    20,
                    FontAttributes.None,
                    TextAlignment.Center);
                break;
        }
    }

    private void Show(
        string text,
        string? above,
        string? below,
        double fontSize,
        FontAttributes attributes,
        TextAlignment alignment,
        double letterSpacing = 0)
    {
        primary.Text = text;
        primary.FontSize = fontSize;
        primary.FontAttributes = attributes;
        primary.HorizontalTextAlignment = alignment;
        primary.CharacterSpacing = letterSpacing;

        secondary.Text = above;
        secondary.FontSize = 11;
        secondary.HorizontalTextAlignment = alignment;
        secondary.IsVisible = above is not null;

        detail.Text = below;
        detail.HorizontalTextAlignment = alignment;
        detail.IsVisible = below is not null;

        stack.HorizontalOptions = alignment is TextAlignment.Center
            ? LayoutOptions.Center
            : LayoutOptions.Start;
    }

    /// <summary>A week as the span it covers — "3–9 Aug", or both months when it straddles one.</summary>
    private static string WeekRange(DateTime weekStart, CultureInfo culture)
    {
        var end = weekStart.AddDays(6);

        return weekStart.Month == end.Month
            ? $"{weekStart.Day}–{end.ToString("d MMM", culture)}"
            : $"{weekStart.ToString("d MMM", culture)} – {end.ToString("d MMM", culture)}";
    }
}
