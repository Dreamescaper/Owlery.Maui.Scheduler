using Microsoft.Maui.Controls.Shapes;

namespace Owlery.Maui.Scheduler.Sample.Views;

/// <summary>
/// An alternative to the control's built-in bordered <c>+</c>, bound to the selected
/// <see cref="SchedulerTimeSlot"/>. Toggled from the knobs panel to show that
/// <c>CellSelectionTemplate</c> replaces the affordance rather than decorating it.
/// </summary>
public sealed class CellSelectionBadge : Border
{
    private readonly Label label;

    public CellSelectionBadge()
    {
        label = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        Padding = new Thickness(4, 2);
        BackgroundColor = Color.FromArgb("#2F5D8A");
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 6 };
        Content = label;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        // A month cell reports a slot a whole day long; the timeline reports SnapMinutes. The
        // duration is what tells them apart.
        label.Text = BindingContext switch
        {
            SchedulerTimeSlot slot when slot.Duration >= TimeSpan.FromDays(1) => $"+ {slot.Start:d MMM}",
            SchedulerTimeSlot slot => $"+ {slot.Start:HH:mm}",
            _ => "+"
        };
    }
}
