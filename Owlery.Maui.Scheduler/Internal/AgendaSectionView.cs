using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

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
/// layout every time one scrolled into view. The day number has a second label of its own because it
/// is the only kind that can sit in a filled circle, and mutating a shared label's shape on every
/// rebind would be worse than carrying one more.
/// </para>
/// </remarks>
internal sealed class AgendaSectionView : ContentView
{
    private readonly Label primary;
    private readonly Label secondary;
    private readonly Label detail;
    private readonly Label dayNumber;
    private readonly Border dayCircle;
    private readonly VerticalStackLayout stack;
    private AgendaSectionAppearance appearance =
        new(Colors.Black, Colors.Gray, Colors.Black, Colors.White, false, default);

    public AgendaSectionView()
    {
        primary = new Label { LineBreakMode = LineBreakMode.TailTruncation };
        secondary = new Label { LineBreakMode = LineBreakMode.TailTruncation };
        detail = new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation };

        dayNumber = new Label
        {
            FontSize = 20,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        dayCircle = new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            Padding = 0,
            WidthRequest = 28,
            HeightRequest = 28,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false,
            Content = dayNumber
        };

        stack = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start,
            Children = { secondary, dayCircle, primary, detail }
        };

        Content = stack;
    }

    public string EmptyText { get; set; } = "No appointments";

    /// <summary>Repaints the built-in heading. A host-supplied template paints itself.</summary>
    /// <remarks>
    /// Guarded because every heading in the window is bound again on every realization pass, and
    /// <see cref="Render"/> formats a month name, a weekday or a week range against the current
    /// culture each time. Colours only move when the host changes its appearance, which repopulates
    /// everything anyway; a heading that is merely still on screen has nothing to repaint. A heading
    /// bound to a new section renders from <see cref="OnBindingContextChanged"/>, so nothing here is
    /// what keeps its text current.
    /// </remarks>
    public void UpdateAppearance(AgendaSectionAppearance appearance)
    {
        if (this.appearance == appearance)
            return;

        this.appearance = appearance;
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

        primary.TextColor = appearance.PrimaryColor;
        secondary.TextColor = appearance.SecondaryColor;
        detail.TextColor = appearance.SecondaryColor;

        // A month and a week are headings to scan by — sit them in the middle of their strip so the
        // band reads evenly, rather than hugging its top.
        var scanning = section.Kind is SchedulerAgendaSectionKind.Month or SchedulerAgendaSectionKind.Week;
        stack.VerticalOptions = scanning ? LayoutOptions.Center : LayoutOptions.Start;

        switch (section.Kind)
        {
            case SchedulerAgendaSectionKind.Month:
                Show(
                    section.Date.ToString("MMMM yyyy", culture),
                    section.AppointmentCount == 0 ? EmptyText : null,
                    22,
                    FontAttributes.Bold);
                break;

            case SchedulerAgendaSectionKind.Week:
                // A small-caps kicker beneath the month: the range, muted, tracked out, carrying the
                // month's weight instead of competing with it.
                primary.TextColor = appearance.SecondaryColor;
                Show(
                    WeekRange(section.Date, culture).ToUpper(culture),
                    null,
                    11,
                    FontAttributes.None,
                    letterSpacing: 1.2);
                break;

            case SchedulerAgendaSectionKind.Day:
                ShowDayNumber(section, culture);
                break;
        }
    }

    /// <summary>
    /// The day marker: the weekday above a number that sits in a filled circle on today.
    /// </summary>
    /// <remarks>
    /// The number's own label is used rather than the shared <see cref="primary"/>, so a heading
    /// reused for a month or a week never has to shed a shape it was given for a day.
    /// </remarks>
    private void ShowDayNumber(SchedulerAgendaSection section, CultureInfo culture)
    {
        var isToday = appearance.ShowCurrentDayCircle
            && DateOnly.FromDateTime(section.Date) == appearance.Today;

        dayNumber.Text = section.Date.Day.ToString(culture);
        dayNumber.TextColor = isToday ? appearance.NumberColor : appearance.PrimaryColor;

        dayCircle.BackgroundColor = isToday ? appearance.CircleColor : null;
        dayCircle.IsVisible = true;
        primary.IsVisible = false;

        Surrounds(
            culture.DateTimeFormat.GetAbbreviatedDayName(section.Date.DayOfWeek).ToUpper(culture),
            null,
            TextAlignment.Center);
    }

    /// <summary>A month name or a week range: the heading kinds that read across, not down.</summary>
    private void Show(
        string text,
        string? below,
        double fontSize,
        FontAttributes attributes,
        double letterSpacing = 0)
    {
        primary.Text = text;
        primary.FontSize = fontSize;
        primary.FontAttributes = attributes;
        primary.HorizontalTextAlignment = TextAlignment.Start;
        primary.CharacterSpacing = letterSpacing;
        primary.IsVisible = true;

        dayCircle.IsVisible = false;

        Surrounds(null, below, TextAlignment.Start);
    }

    /// <summary>
    /// The kicker above and the note below, whichever of the three kinds is showing.
    /// </summary>
    /// <remarks>
    /// Shared because the two that carry it disagree only on where they sit: a month or a week reads
    /// from the left, a day marker down the centre of the gutter. The stack takes its alignment from
    /// the same answer, so the labels and the column they sit in cannot drift apart.
    /// </remarks>
    private void Surrounds(string? above, string? below, TextAlignment alignment)
    {
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

/// <summary>
/// Everything the built-in agenda heading paints from, as one value so the repaint guard is a single
/// comparison rather than one per field.
/// </summary>
internal readonly record struct AgendaSectionAppearance(
    Color PrimaryColor,
    Color SecondaryColor,
    Color CircleColor,
    Color NumberColor,
    bool ShowCurrentDayCircle,
    DateOnly Today);
