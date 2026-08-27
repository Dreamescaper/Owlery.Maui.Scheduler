using Microsoft.Maui.Controls.Shapes;
using Owlery.Maui.Scheduler.Sample.Data;

namespace Owlery.Maui.Scheduler.Sample.Views;

/// <summary>
/// The timeline appointment template — a coloured box sized by the control to the appointment's
/// duration.
/// </summary>
/// <remarks>
/// Views made from <c>AppointmentTemplate</c> are pooled and rebound rather than rebuilt, so the
/// whole subtree is created once in the constructor and only its content changes. Reading the
/// appointment in <see cref="OnBindingContextChanged"/> — rather than capturing one — is what makes
/// that safe; a template that captured its appointment would keep drawing the first one it ever saw.
/// </remarks>
public sealed class AppointmentBox : Border
{
    private readonly Label subjectLabel;
    private readonly Label detailLabel;
    private readonly Label lockLabel;

    public AppointmentBox()
    {
        subjectLabel = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        detailLabel = new Label
        {
            FontSize = 10,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            Opacity = 0.8
        };

        lockLabel = new Label
        {
            Text = "\U0001F512",
            FontSize = 9,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start
        };

        var text = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children = { subjectLabel, detailLabel }
        };

        StrokeThickness = 1;
        StrokeShape = new RoundRectangle { CornerRadius = 6 };
        Content = new Grid { IsClippedToBounds = true, Children = { text, lockLabel } };

        // The control sizes the box to the appointment's duration, so a quarter-hour lesson at the
        // default hour height gets about twelve units to work with. Wired once, in the constructor —
        // a pooled view is rebound rather than rebuilt, and a handler attached per binding would
        // accumulate one subscription per appointment the view ever showed.
        SizeChanged += (_, _) => FitToHeight();
        FitToHeight();
    }

    /// <summary>Drops down to one line, then to a smaller one, as the box gets shorter.</summary>
    private void FitToHeight()
    {
        detailLabel.IsVisible = Height >= 34;
        subjectLabel.FontSize = Height < 22 ? 9 : 11;
        Padding = Height < 22 ? new Thickness(4, 0) : new Thickness(5, 3);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (BindingContext is not SampleAppointment appointment)
            return;

        var fill = SamplePalette.Fill(appointment.Palette);
        var ink = SamplePalette.Ink(appointment.Palette);

        BackgroundColor = fill;
        Stroke = ink;

        subjectLabel.TextColor = ink;
        subjectLabel.Text = $"{appointment.Subject} · {appointment.Person}";

        detailLabel.TextColor = ink;
        detailLabel.Text = $"{appointment.Start:HH:mm}–{appointment.End:HH:mm}";

        lockLabel.IsVisible = appointment.IsLocked;
    }
}
