using Microsoft.Maui.Layouts;

namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// A titled row of mutually exclusive choices. Wraps, so a long list of options — every day of the
/// week, say — stays inside the panel.
/// </summary>
public sealed class Segmented<T> : ContentView
{
    private readonly List<(T Value, Button Button)> options = [];
    private readonly Action<T> onChanged;
    private T current;

    public Segmented(string title, IEnumerable<(string Text, T Value)> choices, T initial, Action<T> onChanged)
    {
        this.onChanged = onChanged;
        current = initial;

        var row = new FlexLayout { Wrap = FlexWrap.Wrap, Direction = FlexDirection.Row };

        foreach (var (text, value) in choices)
        {
            var button = Knobs.SmallButton(text, Theme.Unselected, Theme.Ink);
            button.Clicked += (_, _) => Select(value);
            options.Add((value, button));
            Knobs.AddWrapping(row, button);
        }

        Content = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = title, FontSize = 13, TextColor = Theme.Ink },
                row
            }
        };

        Repaint();
    }

    /// <summary>Selects a value and reports it. Selecting what is already selected does nothing.</summary>
    public void Select(T value)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return;

        current = value;
        Repaint();
        onChanged(value);
    }

    /// <summary>Moves the highlight without reporting — for when the control changed the value itself.</summary>
    public void Show(T value)
    {
        current = value;
        Repaint();
    }

    private void Repaint()
    {
        foreach (var (value, button) in options)
        {
            var selected = EqualityComparer<T>.Default.Equals(current, value);
            button.BackgroundColor = selected ? Theme.Accent : Theme.Unselected;
            button.TextColor = selected ? Colors.White : Theme.Ink;
        }
    }
}
