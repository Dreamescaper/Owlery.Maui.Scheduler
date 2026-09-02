using Microsoft.Maui.Controls.Shapes;
using Owlery.Maui.Scheduler.Sample.Data;

namespace Owlery.Maui.Scheduler.Sample.Views;

/// <summary>
/// The agenda row template — a card as tall as the two lines it holds.
/// </summary>
/// <remarks>
/// Deliberately not the same shape as <see cref="AppointmentBox"/>. A timeline box is as tall as its
/// appointment is long and takes its time from where it sits on the grid; an agenda row has no grid
/// to sit on, so it says the time itself, and its height is whatever the text comes to.
/// <para>
/// It is the one template whose height this sample supplies up front. <see cref="RowHeight"/> states
/// that height from the appointment alone and is handed to the control through
/// <see cref="SchedulerView.AgendaRowHeight"/>, which is what keeps scrolling free of the
/// measure-and-correct pass (see DESIGN §14). The second line appears only when there is somewhere to
/// be, and the row is shorter when it does not, so <see cref="RowHeight"/> mirrors that decision.
/// Toggle the knob off and the row falls back to being measured, the sample's demonstration of the
/// difference.
/// </para>
/// </remarks>
public sealed class AgendaRow : Border
{
    private const double VerticalPadding = 8;    // Border Padding (10, 8), top and bottom
    private const double BorderStroke = 2;       // 1pt each side
    private const double VerticalMargin = 4;     // Border Margin (0, 2), top and bottom
    private const double StackSpacing = 2;       // VerticalStackLayout Spacing
    private const double SubjectLineHeight = 20; // subject, FontSize 15
    private const double DetailLineHeight = 18;  // detail, FontSize 13

    private readonly Label subject;
    private readonly Label detail;

    /// <summary>
    /// The height a row will be, from the appointment alone.
    /// </summary>
    /// <remarks>
    /// What <see cref="SchedulerView.AgendaRowHeight"/> is for: the control can lay an agenda out
    /// without measuring a row first, which is what keeps scrolling free of the correction pass that
    /// an estimate would otherwise trigger. It mirrors <see cref="OnBindingContextChanged"/> — the
    /// detail line is what makes a row taller, so the same test picks the height. The values are the
    /// template's paddings plus a line height each, deliberately slightly generous: short of text, a
    /// little extra room reads fine, clipping does not.
    /// </remarks>
    public static double RowHeight(ISchedulerAppointment appointment)
    {
        var single = VerticalPadding * 2 + BorderStroke + VerticalMargin + SubjectLineHeight;
        return appointment.Start.Minute == 0 ? single + StackSpacing + DetailLineHeight : single;
    }

    public AgendaRow()
    {
        subject = new Label
        {
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        detail = new Label
        {
            FontSize = 13,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        Padding = new Thickness(10, 8);
        Margin = new Thickness(0, 2);
        StrokeThickness = 1;
        StrokeShape = new RoundRectangle { CornerRadius = 8 };

        Content = new VerticalStackLayout { Spacing = 2, Children = { subject, detail } };
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (BindingContext is not SampleAppointment appointment)
            return;

        var ink = SamplePalette.Ink(appointment.Palette);

        BackgroundColor = SamplePalette.Fill(appointment.Palette);
        Stroke = new SolidColorBrush(ink);

        subject.TextColor = ink;
        subject.Text = appointment.Subject;

        detail.TextColor = ink;
        detail.Text = $"{appointment.Start:HH:mm}–{appointment.End:HH:mm}";

        // Every other appointment gets no second line, so the sample actually exercises rows of two
        // different heights rather than pretending they are uniform.
        detail.IsVisible = appointment.Start.Minute == 0;
    }
}
