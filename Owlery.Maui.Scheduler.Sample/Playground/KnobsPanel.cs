using System.ComponentModel;
using Owlery.Maui.Scheduler.Sample.Data;
using Owlery.Maui.Scheduler.Sample.Views;

namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// The drawer of knobs. Every row writes straight to the property it is named after, so the panel
/// doubles as a list of what the control exposes.
/// </summary>
public sealed class KnobsPanel : ContentView
{
    private readonly SchedulerView scheduler;
    private readonly AppointmentSource source;
    private readonly Label countCaption;
    private readonly Segmented<int> countPresets;
    private readonly Segmented<int> dayCounts;

    public KnobsPanel(SchedulerView scheduler, AppointmentSource source, DragPolicy policy, Action onClose)
    {
        this.scheduler = scheduler;
        this.source = source;

        countCaption = Knobs.Caption(string.Empty);

        countPresets = new Segmented<int>(
            "Appointments",
            [("0", 0), ("25", 25), ("100", 100), ("500", 500), ("2 000", 2000), ("5 000", 5000)],
            source.Count,
            source.SetCount);

        dayCounts = new Segmented<int>(
            "VisibleDays",
            [("1", 1), ("2", 2), ("3", 3), ("5", 5), ("7", 7)],
            scheduler.VisibleDays,
            days => scheduler.VisibleDays = days);

        var viewModes = new Segmented<SchedulerViewMode>(
            "ViewMode",
            [("Timeline", SchedulerViewMode.Timeline), ("Month", SchedulerViewMode.Month)],
            scheduler.ViewMode,
            mode => scheduler.ViewMode = mode);

        var content = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Header(onClose),

                Knobs.Section("Data"),
                countPresets,
                Knobs.Actions(
                    ("−25", () => Nudge(-25)),
                    ("+25", () => Nudge(25)),
                    ("−250", () => Nudge(-250)),
                    ("+250", () => Nudge(250)),
                    ("Regenerate", source.Reseed)),
                countCaption,

                Knobs.Section("View"),
                viewModes,
                dayCounts,
                new Segmented<DayOfWeek>("FirstDayOfWeek",
                    [("Mon", DayOfWeek.Monday), ("Tue", DayOfWeek.Tuesday), ("Wed", DayOfWeek.Wednesday),
                     ("Thu", DayOfWeek.Thursday), ("Fri", DayOfWeek.Friday), ("Sat", DayOfWeek.Saturday),
                     ("Sun", DayOfWeek.Sunday)],
                    scheduler.FirstDayOfWeek, day => scheduler.FirstDayOfWeek = day),
                Knobs.Caption("FirstDayOfWeek only applies to a seven-day page. Shorter pages start on DisplayDate."),
                new Segmented<string>("TimeZone", [("Local", "local"), ("UTC", "utc")], "local", SetTimeZone),
                Knobs.Caption("The control converts nothing. Changing the zone moves the current-time line and which column counts as today."),

                Knobs.Section("Timeline"),
                Knobs.Slide("StartHour", 0, 23, scheduler.StartHour, "F0",
                    hour => scheduler.StartHour = Math.Min((int)hour, scheduler.EndHour - 1)),
                Knobs.Slide("EndHour", 1, 24, scheduler.EndHour, "F0",
                    hour => scheduler.EndHour = Math.Max((int)hour, scheduler.StartHour + 1)),
                Knobs.Slide("HourHeight", 24, 160, scheduler.HourHeight, "F0",
                    height => scheduler.HourHeight = Math.Round(height)),
                Knobs.Slide("TimeGutterWidth", 0, 120, scheduler.TimeGutterWidth, "F0",
                    width => scheduler.TimeGutterWidth = Math.Round(width)),
                Knobs.Slide("HeaderHeight", 32, 120, scheduler.HeaderHeight, "F0",
                    height => scheduler.HeaderHeight = Math.Round(height)),
                new Segmented<string>("TimeFormat", [("HH:mm", "HH:mm"), ("h:mm tt", "h:mm tt"), ("h:mm", "h:mm")],
                    scheduler.TimeFormat, format => scheduler.TimeFormat = format),
                Knobs.Actions(
                    ("ScrollToTime 06:00", () => scheduler.ScrollToTime(TimeSpan.FromHours(6))),
                    ("12:00", () => scheduler.ScrollToTime(TimeSpan.FromHours(12))),
                    ("20:00", () => scheduler.ScrollToTime(TimeSpan.FromHours(20)))),

                Knobs.Section("Month"),
                new Segmented<string>("MonthOverflowFormat",
                    [("+{0} more", "+{0} more"), ("+{0}", "+{0}"), ("{0} hidden", "{0} hidden")],
                    scheduler.MonthOverflowFormat, format => scheduler.MonthOverflowFormat = format),

                Knobs.Section("Interaction"),
                new Segmented<int>("SnapMinutes", [("5", 5), ("10", 10), ("15", 15), ("30", 30), ("60", 60)],
                    scheduler.SnapMinutes, minutes => scheduler.SnapMinutes = minutes),
                Knobs.Toggle("AllowDragAndDrop", scheduler.AllowDragAndDrop, on => scheduler.AllowDragAndDrop = on),
                Knobs.Toggle("AllowDragAcrossPeriods", scheduler.AllowDragAcrossPeriods, on => scheduler.AllowDragAcrossPeriods = on),
                Knobs.Toggle("ShowDragTimeIndicator", scheduler.ShowDragTimeIndicator, on => scheduler.ShowDragTimeIndicator = on),
                Knobs.Toggle("Cancel drags of locked items", policy.BlockLocked, on => policy.BlockLocked = on),
                Knobs.Toggle("Cancel every drop", policy.RejectDrops, on => policy.RejectDrops = on),
                Knobs.Caption("The last two are host policy, answered in AppointmentDragStarting and AppointmentDropped."),

                Knobs.Section("Appearance"),
                Knobs.Toggle("IsBusy", scheduler.IsBusy, on => scheduler.IsBusy = on),
                new Segmented<string>("GridBackgroundColor",
                    [("White", "#FFFFFF"), ("Paper", "#FBF8F2"), ("Slate", "#EEF1F5")],
                    "#FFFFFF", hex => scheduler.GridBackgroundColor = Color.FromArgb(hex)),
                Knobs.Toggle("Custom CellSelectionTemplate", false, UseCustomCellTemplate),
                Knobs.Caption("Off draws the control's own bordered + box.")
            }
        };

        source.Changed += (_, _) => RestateCount();
        RestateCount();

        scheduler.PropertyChanged += OnSchedulerPropertyChanged;

        BackgroundColor = Theme.Chrome;
        Content = new ScrollView { Padding = new Thickness(16, 12, 16, 24), Content = content };
    }

    private View Header(Action onClose)
    {
        var close = Knobs.SmallButton("Done", Theme.Accent, Colors.White);
        close.Clicked += (_, _) => onClose();

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label
        {
            Text = "Knobs",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Theme.Ink,
            VerticalOptions = LayoutOptions.Center
        });
        row.Add(close, 1);
        return row;
    }

    private void Nudge(int delta)
    {
        source.SetCount(source.Count + delta);
        countPresets.Show(source.Count);
    }

    private void RestateCount()
    {
        var perDay = source.Count / (double)SampleDataGenerator.WindowDays;
        countCaption.Text =
            $"{source.Count:N0} appointments spread over {SampleDataGenerator.WindowDays} days centred on today — about {perDay:F1} a day.";
        countPresets.Show(source.Count);
    }

    /// <summary>
    /// Follows the control when something other than this drawer changes a property it shows.
    /// </summary>
    /// <remarks>
    /// Tapping a day header drops the page to a single day, and the knob went on reading 7 — a panel
    /// whose whole purpose is to describe the control was describing something that was not there.
    /// Watching for the change rather than being told about it keeps that true for whatever moves the
    /// property next; <see cref="Segmented{T}.Show"/> repaints without reporting, so nothing bounces
    /// back into the control.
    /// </remarks>
    private void OnSchedulerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == SchedulerView.VisibleDaysProperty.PropertyName)
            dayCounts.Show(scheduler.VisibleDays);
    }

    private void SetTimeZone(string id) =>
        scheduler.TimeZone = id == "utc" ? TimeZoneInfo.Utc : TimeZoneInfo.Local;

    private void UseCustomCellTemplate(bool on) =>
        scheduler.CellSelectionTemplate = on ? new DataTemplate(static () => new CellSelectionBadge()) : null;
}
