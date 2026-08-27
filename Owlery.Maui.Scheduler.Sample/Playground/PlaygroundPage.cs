using Owlery.Maui.Scheduler.Sample.Data;
using Owlery.Maui.Scheduler.Sample.Views;

namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// The whole playground: the control under a strip of navigation chrome, a log of what it reported,
/// and a drawer of knobs over the top.
/// </summary>
public sealed class PlaygroundPage : ContentPage
{
    /// <summary>What the top bar's − and + buttons step through.</summary>
    private static readonly int[] CountLadder = [0, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];

    private const int InitialCount = 100;
    private const double MaxDrawerWidth = 340;

    private readonly SchedulerView scheduler;
    private readonly AppointmentSource source;
    private readonly DragPolicy policy = new();
    // Filling the row rather than centring in it: a label that starts empty measures to nothing, and
    // an iOS cell arranged to zero height does not come back when the text arrives.
    private readonly Label title = new()
    {
        FontSize = 17,
        FontAttributes = FontAttributes.Bold,
        TextColor = Theme.Ink,
        VerticalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.TailTruncation
    };
    private readonly Label countLabel = new()
    {
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        TextColor = Theme.Accent,
        WidthRequest = 52,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center
    };
    private readonly Label log = new()
    {
        FontSize = 11,
        TextColor = Theme.Muted,
        MaxLines = 3,
        LineBreakMode = LineBreakMode.TailTruncation
    };

    private readonly List<string> entries = [];
    private readonly BoxView scrim;
    private readonly KnobsPanel knobs;

    public PlaygroundPage()
    {
        scheduler = new SchedulerView
        {
            TimeZone = TimeZoneInfo.Local,
            AppointmentTemplate = new DataTemplate(static () => new AppointmentBox()),
            MonthAppointmentTemplate = new DataTemplate(static () => new AppointmentChip())
        };

        scheduler.VisibleDatesChanged += OnVisibleDatesChanged;
        scheduler.CellTapped += OnCellTapped;
        scheduler.AppointmentTapped += OnAppointmentTapped;
        scheduler.AppointmentDragStarting += OnAppointmentDragStarting;
        scheduler.AppointmentDropped += OnAppointmentDropped;

        source = new AppointmentSource(scheduler);
        source.Changed += (_, _) => countLabel.Text = $"{source.Count:N0}";

        scrim = new BoxView { Color = Theme.Scrim, IsVisible = false, Opacity = 0 };
        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _ = SetDrawerOpen(false)) });

        knobs = new KnobsPanel(scheduler, source, policy, () => _ = SetDrawerOpen(false))
        {
            WidthRequest = MaxDrawerWidth,
            HorizontalOptions = LayoutOptions.End,
            IsVisible = false,
            TranslationX = MaxDrawerWidth
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        root.Add(TopBar());
        root.Add(scheduler, 0, 1);
        root.Add(LogBar(), 0, 2);

        Grid.SetRowSpan(scrim, 3);
        Grid.SetRowSpan(knobs, 3);
        root.Add(scrim);
        root.Add(knobs);

        BackgroundColor = Colors.White;
        Content = root;
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.All);

        source.SetCount(InitialCount);
    }

    private View TopBar()
    {
        var knobsButton = Knobs.SmallButton("Knobs", Theme.Accent, Colors.White);
        knobsButton.Clicked += (_, _) => _ = SetDrawerOpen(true);

        var fewer = Stepper("−", -1);
        var more = Stepper("+", 1);

        var top = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        top.Add(title);
        top.Add(fewer, 1);
        top.Add(countLabel, 2);
        top.Add(more, 3);
        top.Add(knobsButton, 4);

        var previous = Knobs.SmallButton("‹", Theme.Unselected, Theme.Ink);
        previous.Clicked += (_, _) => Step(-1);

        var next = Knobs.SmallButton("›", Theme.Unselected, Theme.Ink);
        next.Clicked += (_, _) => Step(1);

        var today = Knobs.SmallButton("Today", Theme.Unselected, Theme.Ink);
        today.Clicked += (_, _) => scheduler.DisplayDate = DateTime.Today;

        var navigation = new HorizontalStackLayout
        {
            Spacing = 6,
            Children = { previous, today, next }
        };

        return new VerticalStackLayout
        {
            Padding = new Thickness(12, 8),
            Spacing = 6,
            BackgroundColor = Theme.Chrome,
            Children = { top, navigation }
        };
    }

    private Button Stepper(string text, int direction)
    {
        var button = Knobs.SmallButton(text, Theme.Unselected, Theme.Ink);
        button.WidthRequest = 34;
        button.Clicked += (_, _) => source.SetCount(NextOnLadder(source.Count, direction));
        return button;
    }

    private View LogBar() => new Border
    {
        Padding = new Thickness(12, 6),
        BackgroundColor = Theme.Chrome,
        Stroke = Theme.Line,
        StrokeThickness = 0,
        Content = log
    };

    /// <summary>Steps to the neighbouring rung, so + and − stay useful from ten items to five thousand.</summary>
    private static int NextOnLadder(int count, int direction)
    {
        if (direction > 0)
        {
            foreach (var rung in CountLadder)
            {
                if (rung > count)
                    return rung;
            }

            return CountLadder[^1];
        }

        for (var i = CountLadder.Length - 1; i >= 0; i--)
        {
            if (CountLadder[i] < count)
                return CountLadder[i];
        }

        return 0;
    }

    /// <summary>Pages by whatever a page currently is — a month, or as many days as are visible.</summary>
    private void Step(int direction) =>
        scheduler.DisplayDate = scheduler.ViewMode is SchedulerViewMode.Month
            ? scheduler.DisplayDate.AddMonths(direction)
            : scheduler.DisplayDate.AddDays(direction * scheduler.VisibleDays);

    /// <summary>
    /// Keeps a strip of the calendar — and of the scrim that closes the drawer — visible on a narrow
    /// phone, where a fixed 340 would cover almost the whole screen.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
            return;

        var drawer = Math.Min(MaxDrawerWidth, Math.Max(240, width - 56));

        if (Math.Abs(knobs.WidthRequest - drawer) < 0.5)
            return;

        knobs.WidthRequest = drawer;

        if (!knobs.IsVisible)
            knobs.TranslationX = drawer;
    }

    private async Task SetDrawerOpen(bool open)
    {
        if (open)
        {
            scrim.IsVisible = true;
            knobs.IsVisible = true;
            await Task.WhenAll(scrim.FadeToAsync(1, 150), knobs.TranslateToAsync(0, 0, 180, Easing.CubicOut));
            return;
        }

        await Task.WhenAll(scrim.FadeToAsync(0, 150), knobs.TranslateToAsync(knobs.WidthRequest, 0, 180, Easing.CubicIn));
        scrim.IsVisible = false;
        knobs.IsVisible = false;
    }

    private void OnVisibleDatesChanged(object? sender, SchedulerVisibleDatesChangedEventArgs e)
    {
        title.Text = Describe(e.VisibleDates);
        Log($"VisibleDatesChanged · {e.VisibleDates.Count} days, prefetch {e.PrefetchFrom:d MMM}–{e.PrefetchTo:d MMM}");
    }

    private string Describe(IReadOnlyList<DateTime> dates)
    {
        if (dates.Count == 0)
            return string.Empty;

        // A month reports all 42 cells, so its first and last days belong to the neighbouring
        // months; the middle of the grid is the month actually being shown.
        if (scheduler.ViewMode is SchedulerViewMode.Month)
            return dates[dates.Count / 2].ToString("MMMM yyyy");

        var first = dates[0];
        var last = dates[^1];

        if (first.Date == last.Date)
            return first.ToString("ddd d MMM yyyy");

        return first.Month == last.Month
            ? $"{first.Day}–{last:d MMM yyyy}"
            : $"{first:d MMM}–{last:d MMM yyyy}";
    }

    private void OnCellTapped(object? sender, SchedulerCellTappedEventArgs e) =>
        Log($"CellTapped · {e.Slot.Start:ddd d MMM HH:mm} for {e.Slot.Duration.TotalMinutes:F0} min");

    private void OnAppointmentTapped(object? sender, SchedulerAppointmentTappedEventArgs e) =>
        Log($"AppointmentTapped · {Name(e.Appointment)} at {e.Appointment.Start:ddd d MMM HH:mm}");

    private void OnAppointmentDragStarting(object? sender, SchedulerAppointmentDragStartingEventArgs e)
    {
        // Cancel is read the moment this returns, so the decision has to be made here and now —
        // an await before setting it would be too late.
        if (policy.BlockLocked && e.Appointment is SampleAppointment { IsLocked: true })
        {
            e.Cancel = true;
            Log($"AppointmentDragStarting · refused, {Name(e.Appointment)} is locked");
            return;
        }

        Log($"AppointmentDragStarting · {Name(e.Appointment)}");
    }

    private void OnAppointmentDropped(object? sender, SchedulerAppointmentDroppedEventArgs e)
    {
        if (policy.RejectDrops)
        {
            e.Cancel = true;
            Log($"AppointmentDropped · rejected, {Name(e.Appointment)} stays at {e.Appointment.Start:HH:mm}");
            return;
        }

        if (e.Appointment is not SampleAppointment appointment)
            return;

        // The control leaves the appointment where it was dropped and does not touch the model. The
        // move only becomes real when the host writes it down and re-emits ItemsSource.
        appointment.MoveTo(e.DropStart);
        source.Publish();

        Log($"AppointmentDropped · {Name(e.Appointment)} → {e.DropStart:ddd d MMM HH:mm}");
    }

    private static string Name(ISchedulerAppointment appointment) =>
        appointment is SampleAppointment sample ? $"{sample.Subject} · {sample.Person}" : appointment.Subject ?? "?";

    private void Log(string message)
    {
        entries.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");

        if (entries.Count > 3)
            entries.RemoveRange(3, entries.Count - 3);

        log.Text = string.Join('\n', entries);
    }
}
