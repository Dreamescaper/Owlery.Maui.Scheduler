using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// The hour labels down the left of the timeline, and the time chip shown while dragging.
/// </summary>
/// <remarks>
/// Real labels rather than a drawn canvas. There are only a dozen or so, which is nowhere near the
/// two thousand cells that made the grid worth drawing (DESIGN.md section 7), and painting them cost
/// text that would not scale with the system font size — recorded as the ACC-9 gap until this
/// replaced it.
/// <para>
/// Positioned from the same <see cref="SchedulerGeometry.YFromMinutes"/> the grid lines are drawn
/// from, so a label cannot drift away from the line it names.
/// </para>
/// </remarks>
internal sealed class TimeGutter
{
    /// <summary>Height of one hour label, and of the chip that may cover it.</summary>
    private const double LabelHeight = 15;

    private const double IndicatorHeight = 18;

    /// <summary>
    /// Space between a label and the grid.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose: a twelve-hour locale renders "10:00 AM" where a twenty-four-hour one needs
    /// only "10:00", and the default gutter width has little to spare. A host whose users read
    /// twelve-hour time may want a wider one.
    /// </remarks>
    private const double LabelInset = 4;

    internal const string IndicatorAutomationId = "scheduler-drag-time";

    private readonly SchedulerGeometry geometry;
    private readonly Border indicator;
    private readonly Label indicatorLabel;

    private Label[] hourLabels = [];

    public TimeGutter(SchedulerGeometry geometry)
    {
        this.geometry = geometry;

        indicatorLabel = new Label
        {
            FontSize = 11,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        indicator = new Border
        {
            AutomationId = IndicatorAutomationId,
            IsVisible = false,
            InputTransparent = true,
            BackgroundColor = Color.FromArgb("#212121"),
            StrokeThickness = 0,
            Padding = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = indicatorLabel,

            // Above the hour labels. Equal ZIndex paints in the order children were added, and the
            // labels are added later — by Update, and again whenever the window changes — so without
            // this the chip is drawn underneath and the hour text shows through it.
            ZIndex = 1
        };

        // Drawn above the pager beside it, not merely next to it. MAUI leaves ClipChildren off on its
        // Android layout views so shadows can spill, which means the pager is drawn without being
        // clipped to its own bounds — and its content is three pages wide with an opaque background,
        // so the page parked to the left painted straight over these labels. Fixed chrome over
        // scrolling content should be drawn last regardless. See DESIGN.md section 19.
        View = new AbsoluteLayout { InputTransparent = true, ZIndex = 1 };
        View.Add(indicator);
    }

    /// <summary>The view to place beside the timeline.</summary>
    public AbsoluteLayout View { get; }

    /// <summary>
    /// Sizes the gutter and writes the hours into it.
    /// </summary>
    /// <remarks>
    /// The first and last hours get no label: there is no line above the one or below the other for
    /// it to sit against. The background is opaque for the reason given in the constructor.
    /// </remarks>
    public void Update(double width, string timeFormat, Color background)
    {
        View.IsVisible = true;
        View.WidthRequest = width;
        View.HeightRequest = geometry.ContentHeight;
        View.BackgroundColor = background;

        var hours = Enumerable
            .Range(geometry.StartHour + 1, Math.Max(0, geometry.EndHour - geometry.StartHour - 1))
            .ToArray();

        if (hourLabels.Length != hours.Length)
            RebuildLabels(hours.Length);

        var culture = CultureInfo.CurrentUICulture;

        for (var i = 0; i < hours.Length; i++)
        {
            hourLabels[i].Text = DateTime.Today.AddHours(hours[i]).ToString(timeFormat, culture);

            Place(hourLabels[i], new Rect(
                0,
                geometry.YFromMinutes(hours[i] * 60.0) - LabelHeight / 2,
                Math.Max(0, width - LabelInset),
                LabelHeight));
        }
    }

    /// <summary>Collapses the gutter, for a surface that has no hours to label.</summary>
    public void Hide()
    {
        View.IsVisible = false;
        View.WidthRequest = 0;
    }

    /// <summary>
    /// Puts the drag-time chip level with the line an appointment would start on.
    /// </summary>
    /// <remarks>
    /// It simply covers whichever hour label it lands on. An earlier version worked out which label
    /// was obscured enough to be worth hiding; the chip is opaque and the same height, so hiding
    /// anything underneath made no visible difference and only added arithmetic to get wrong.
    /// </remarks>
    public void ShowIndicator(double minutes, string text)
    {
        if (indicatorLabel.Text != text)
            indicatorLabel.Text = text;

        // Only written when it actually moves. This runs on every touch event of a drag, and a write
        // invalidates the gutter, which then measures and arranges all of its labels — for a chip
        // that only moves once per snap interval, not once per pixel.
        var bounds = new Rect(
            2,
            geometry.YFromMinutes(minutes) - IndicatorHeight / 2,
            Math.Max(0, View.WidthRequest - 4),
            IndicatorHeight);

        if (AbsoluteLayout.GetLayoutBounds(indicator) != bounds)
            Place(indicator, bounds);

        indicator.IsVisible = true;
    }

    public void ClearIndicator()
    {
        indicator.IsVisible = false;
        indicatorLabel.Text = null;
    }

    private void RebuildLabels(int count)
    {
        foreach (var stale in hourLabels)
            View.Remove(stale);

        hourLabels = [.. Enumerable.Range(0, count).Select(_ => new Label
        {
            FontSize = 11,
            TextColor = Color.FromArgb("#6E6E6E"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        })];

        foreach (var label in hourLabels)
            View.Add(label);
    }

    private static void Place(View view, Rect bounds)
    {
        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(view, bounds);
    }
}
