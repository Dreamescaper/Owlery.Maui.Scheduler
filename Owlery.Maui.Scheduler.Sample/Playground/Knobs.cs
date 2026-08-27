using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// The handful of widgets the knobs panel is built from. Nothing here is control-specific — it is
/// only enough chrome to keep <see cref="KnobsPanel"/> a list of properties rather than a list of
/// layout code.
/// </summary>
public static class Knobs
{
    public static Label Section(string title) => new()
    {
        Text = title.ToUpperInvariant(),
        FontSize = 11,
        FontAttributes = FontAttributes.Bold,
        TextColor = Theme.Muted,
        Margin = new Thickness(0, 14, 0, 2)
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        TextColor = Theme.Muted
    };

    public static View Toggle(string title, bool initial, Action<bool> onChanged)
    {
        var toggle = new Switch { IsToggled = initial, VerticalOptions = LayoutOptions.Center };
        toggle.Toggled += (_, e) => onChanged(e.Value);

        var label = new Label
        {
            Text = title,
            FontSize = 13,
            TextColor = Theme.Ink,
            VerticalOptions = LayoutOptions.Center
        };

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(label);
        row.Add(toggle, 1);
        return row;
    }

    /// <summary>
    /// A slider with the live value beside its title. <paramref name="apply"/> returns the value that
    /// was actually taken, so a knob that clamps — StartHour against EndHour — reads back honestly.
    /// </summary>
    public static View Slide(string title, double min, double max, double initial, string format, Func<double, double> apply)
    {
        var value = new Label
        {
            Text = initial.ToString(format),
            FontSize = 12,
            TextColor = Theme.Accent,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.End
        };

        var slider = new Slider(min, max, initial) { MinimumTrackColor = Theme.Accent };
        slider.ValueChanged += (_, e) => value.Text = apply(e.NewValue).ToString(format);

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = title, FontSize = 13, TextColor = Theme.Ink });
        header.Add(value, 1);

        return new VerticalStackLayout { Children = { header, slider } };
    }

    /// <summary>A row of plain buttons, used for the actions that are not a property.</summary>
    public static View Actions(params (string Text, Action OnTap)[] actions)
    {
        var row = new FlexLayout { Wrap = FlexWrap.Wrap, Direction = FlexDirection.Row };

        foreach (var (text, onTap) in actions)
        {
            var button = SmallButton(text, Theme.Unselected, Theme.Ink);
            button.Clicked += (_, _) => onTap();
            AddWrapping(row, button);
        }

        return row;
    }

    /// <summary>
    /// Adds a button to a wrapping row. A <see cref="FlexLayout"/> shrinks its children to fit a
    /// line by default, which silently truncates the labels; these must keep the width they measured
    /// and wrap onto the next line instead.
    /// </summary>
    internal static void AddWrapping(FlexLayout row, Button button)
    {
        button.Margin = new Thickness(0, 4, 6, 0);
        FlexLayout.SetShrink(button, 0);
        FlexLayout.SetBasis(button, FlexBasis.Auto);
        row.Add(button);
    }

    internal static Button SmallButton(string text, Color background, Color foreground) => new()
    {
        Text = text,
        FontSize = 12,
        Padding = new Thickness(10, 4),
        MinimumHeightRequest = 0,
        HeightRequest = 32,
        CornerRadius = 6,
        BackgroundColor = background,
        TextColor = foreground
    };

    public static Border Card(View content) => new()
    {
        Padding = 12,
        BackgroundColor = Colors.White,
        Stroke = Theme.Line,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 10 },
        Content = content
    };
}
