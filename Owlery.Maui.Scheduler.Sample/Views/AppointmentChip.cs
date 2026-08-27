using Microsoft.Maui.Controls.Shapes;
using Owlery.Maui.Scheduler.Sample.Data;

namespace Owlery.Maui.Scheduler.Sample.Views;

/// <summary>
/// The month appointment template — one line about sixteen units tall, which is all a month cell
/// gives an appointment. Pooled and rebound like <see cref="AppointmentBox"/>.
/// </summary>
public sealed class AppointmentChip : Border
{
    private readonly Ellipse dot;
    private readonly Label label;

    public AppointmentChip()
    {
        dot = new Ellipse
        {
            WidthRequest = 6,
            HeightRequest = 6,
            VerticalOptions = LayoutOptions.Center
        };

        label = new Label
        {
            FontSize = 10,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalOptions = LayoutOptions.Center
        };

        Padding = new Thickness(4, 0);
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 3 };
        Content = new HorizontalStackLayout
        {
            Spacing = 4,
            Children = { dot, label }
        };
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (BindingContext is not SampleAppointment appointment)
            return;

        var ink = SamplePalette.Ink(appointment.Palette);

        BackgroundColor = SamplePalette.Fill(appointment.Palette);
        dot.Fill = new SolidColorBrush(ink);
        label.TextColor = ink;
        label.Text = $"{appointment.Start:HH:mm} {appointment.Subject}";
    }
}
