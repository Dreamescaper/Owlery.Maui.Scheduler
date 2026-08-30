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
                new Segmented<string>("Working hours",
                    [("09–17", "09-17"), ("08:30–16:30", "08:30-16:30"), ("Invalid", "17-09")],
                    "09-17", SetWorkingHours),
                Knobs.Actions(
                    ("Mon–Fri", () => SetWorkingDays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                        DayOfWeek.Thursday, DayOfWeek.Friday)),
                    ("Sun–Thu", () => SetWorkingDays(DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                        DayOfWeek.Wednesday, DayOfWeek.Thursday)),
                    ("Every day", () => SetWorkingDays(Enum.GetValues<DayOfWeek>()))),
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
                Knobs.Toggle("ShowNonWorkingDaysShading", scheduler.ShowNonWorkingDaysShading,
                    on => scheduler.ShowNonWorkingDaysShading = on),
                Knobs.Toggle("ShowCurrentDayHighlight", scheduler.ShowCurrentDayHighlight,
                    on => scheduler.ShowCurrentDayHighlight = on),
                Knobs.Toggle("ShowNonWorkingHoursShading", scheduler.ShowNonWorkingHoursShading,
                    on => scheduler.ShowNonWorkingHoursShading = on),
                new Segmented<string>("Scheduler colors",
                    [("Light", "light"), ("Dark", "dark"), ("Paper", "paper")],
                    "light", ApplySchedulerColors),
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

    private void SetWorkingDays(params DayOfWeek[] days) => scheduler.WorkingDays = days;

    private void SetWorkingHours(string range)
    {
        var parts = range.Split('-');
        scheduler.WorkingHoursStart = TimeOnly.Parse(parts[0]);
        scheduler.WorkingHoursEnd = TimeOnly.Parse(parts[1]);
    }

    private void ApplySchedulerColors(string theme)
    {
        var dark = theme == "dark";
        var paper = theme == "paper";

        var background = Color.FromArgb(dark ? "#121212" : paper ? "#FBF8F2" : "#FFFFFF");

        scheduler.BackgroundColor = background;
        scheduler.GridBackgroundColor = background;
        scheduler.GridLineColor = Color.FromArgb(dark ? "#3A3A3A" : paper ? "#DDD4C6" : "#E0E0E0");
        scheduler.MinorGridLineColor = Color.FromArgb(dark ? "#292929" : paper ? "#EEE7DC" : "#F0F0F0");
        scheduler.PrimaryTextColor = Color.FromArgb(dark ? "#F2F2F2" : "#212121");
        scheduler.SecondaryTextColor = Color.FromArgb(dark ? "#A8A8A8" : "#6E6E6E");
        scheduler.NonWorkingDaysBackgroundColor = Color.FromArgb(dark ? "#181818" : paper ? "#F3EDE3" : "#FAFAFA");
        scheduler.NonWorkingHoursBackgroundColor = Color.FromArgb(dark ? "#1C1C1C" : paper ? "#F3EDE3" : "#FAFAFA");
        scheduler.CurrentDayBackgroundColor = Color.FromArgb(dark ? "#28213D" : "#F3E8FC");
        scheduler.CurrentDayTextColor = Color.FromArgb(dark ? "#B9C2FF" : "#4458C8");
        scheduler.CurrentTimeIndicatorColor = Color.FromArgb(dark ? "#FF6B52" : "#FD4225");
        scheduler.AdjacentMonthBackgroundColor = Color.FromArgb(dark ? "#161616" : paper ? "#F0E8DC" : "#F5F5F5");
        scheduler.AdjacentMonthTextColor = Color.FromArgb(dark ? "#707070" : "#B0B0B0");
        scheduler.CellSelectionBackgroundColor = Color.FromArgb(dark ? "#34284D" : "#F3E8FC");
        scheduler.CellSelectionBorderColor = Color.FromArgb(dark ? "#745C96" : "#DAB8F4");
        scheduler.CellSelectionTextColor = Color.FromArgb(dark ? "#DBC6F5" : "#6B3FA0");
        scheduler.DragTimeIndicatorBackgroundColor = Color.FromArgb(dark ? "#E8E8E8" : "#212121");
        scheduler.DragTimeIndicatorTextColor = Color.FromArgb(dark ? "#212121" : "#FFFFFF");
        scheduler.BusyIndicatorColor = Color.FromArgb(dark ? "#B9C2FF" : "#4458C8");
    }

    private void UseCustomCellTemplate(bool on) =>
        scheduler.CellSelectionTemplate = on ? new DataTemplate(static () => new CellSelectionBadge()) : null;
}
