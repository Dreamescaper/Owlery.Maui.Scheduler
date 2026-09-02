using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class SchedulerAppearanceTests
{
    private static readonly IReadOnlyCollection<DayOfWeek> Weekdays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ];

    private static readonly Color Grid = Color.FromArgb("#111111");
    private static readonly Color MinorGrid = Color.FromArgb("#222222");
    private static readonly Color NonWorkingDay = Color.FromArgb("#333333");
    private static readonly Color NonWorkingHour = Color.FromArgb("#444444");
    private static readonly Color CurrentDay = Color.FromArgb("#555555");
    private static readonly Color CurrentTime = Color.FromArgb("#666666");

    [Test]
    public void Defaults_preserve_the_existing_day_shading_and_leave_hours_unshaded()
    {
        var harness = new SchedulerHarness(new DateTime(2026, 8, 24));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.ShowNonWorkingDaysShading, Is.True);
            Assert.That(harness.Scheduler.ShowCurrentDayHighlight, Is.True);
            Assert.That(harness.Scheduler.ShowNonWorkingHoursShading, Is.False);
            Assert.That(harness.Scheduler.WorkingDays, Is.EqualTo(Weekdays).AsCollection);
            Assert.That(harness.Scheduler.WorkingHoursStart, Is.EqualTo(new TimeOnly(9, 0)));
            Assert.That(harness.Scheduler.WorkingHoursEnd, Is.EqualTo(new TimeOnly(17, 0)));
        });
    }

    [Test]
    public void A_null_working_day_collection_falls_back_to_the_default_week()
    {
        var harness = new SchedulerHarness(new DateTime(2026, 8, 24));

        harness.Scheduler.WorkingDays = null!;

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.WorkingDays, Is.EqualTo(Weekdays).AsCollection);
            // The crash this guards against surfaced inside Draw, not at the assignment above.
            Assert.DoesNotThrow(() => Draw(harness.SurfaceDrawable));
        });
    }

    [Test]
    public void An_empty_working_day_collection_makes_every_day_non_working()
    {
        var drawable = Timeline();
        drawable.ShowCurrentDayHighlight = false;
        drawable.WorkingDays = [];

        Assert.That(
            Draw(drawable).Fills.Count(fill => fill.Color == NonWorkingDay && fill.Bounds.Height == 500),
            Is.EqualTo(21), "seven days across three pages");
    }

    [Test]
    public void Working_hour_endpoints_are_reachable_from_xaml()
    {
        // MAUI's XAML loader ignores the converter the framework registers for TimeOnly, so without
        // the attribute below these properties cannot be set from markup at all.
        foreach (var name in new[] { nameof(SchedulerView.WorkingHoursStart), nameof(SchedulerView.WorkingHoursEnd) })
        {
            var converter = typeof(SchedulerView).GetProperty(name)!
                .GetCustomAttribute<System.ComponentModel.TypeConverterAttribute>();

            Assert.That(converter?.ConverterTypeName, Does.Contain(nameof(TimeOnlyTypeConverter)), name);
        }

        Assert.That(
            new TimeOnlyTypeConverter().ConvertFrom(null, null, "09:00"),
            Is.EqualTo(new TimeOnly(9, 0)));
    }

    [Test]
    public void Timeline_shades_each_non_working_day_across_all_three_pages()
    {
        var drawable = Timeline();
        drawable.ShowCurrentDayHighlight = false;

        var recording = Draw(drawable);

        Assert.That(
            recording.Fills.Count(fill => fill.Color == NonWorkingDay && fill.Bounds.Height == 500),
            Is.EqualTo(6));
    }

    [Test]
    public void Non_working_day_shading_can_be_disabled_or_reclassified()
    {
        var drawable = Timeline();
        drawable.ShowCurrentDayHighlight = false;
        drawable.ShowNonWorkingDaysShading = false;

        Assert.That(Draw(drawable).Fills, Is.Empty);

        drawable.ShowNonWorkingDaysShading = true;
        drawable.WorkingDays =
        [
            DayOfWeek.Sunday,
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday
        ];

        var recording = Draw(drawable);

        Assert.That(
            recording.Fills.Count(fill => fill.Color == NonWorkingDay && fill.Bounds.Height == 500),
            Is.EqualTo(6), "Friday and Saturday should replace Saturday and Sunday");
    }

    [Test]
    public void Timeline_shades_before_and_after_working_hours_on_working_days_only()
    {
        var drawable = Timeline();
        drawable.ShowCurrentDayHighlight = false;
        drawable.ShowNonWorkingDaysShading = false;
        drawable.ShowNonWorkingHoursShading = true;
        drawable.WorkingHoursStart = new TimeOnly(9, 0);
        drawable.WorkingHoursEnd = new TimeOnly(17, 0);

        var recording = Draw(drawable);
        var centreMonday = recording.Fills
            .Where(fill => fill.Color == NonWorkingHour && fill.Bounds.X == 700)
            .OrderBy(fill => fill.Bounds.Y)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(centreMonday, Has.Length.EqualTo(2));
            Assert.That(centreMonday[0].Bounds, Is.EqualTo(new RectF(700, 0, 100, 50)));
            Assert.That(centreMonday[1].Bounds, Is.EqualTo(new RectF(700, 450, 100, 50)));
            Assert.That(recording.Fills.Count(fill => fill.Color == NonWorkingHour), Is.EqualTo(30));
        });
    }

    [Test]
    public void Working_hour_shading_is_clipped_and_invalid_intervals_draw_nothing()
    {
        var drawable = Timeline(startHour: 12, endHour: 16);
        drawable.ShowCurrentDayHighlight = false;
        drawable.ShowNonWorkingDaysShading = false;
        drawable.ShowNonWorkingHoursShading = true;

        Assert.That(Draw(drawable).Fills, Is.Empty, "the whole visible window is inside 09:00-17:00");

        drawable.WorkingHoursStart = new TimeOnly(17, 0);
        drawable.WorkingHoursEnd = new TimeOnly(9, 0);

        Assert.That(Draw(drawable).Fills, Is.Empty, "overnight intervals are outside this change");
    }

    [Test]
    public void Sub_hour_marks_follow_their_interval_and_stop_at_the_hour()
    {
        // 08:00-18:00 is ten hours, so nine of them have an hour below to subdivide.
        Assert.Multiple(() =>
        {
            Assert.That(MinorLines(30), Has.Count.EqualTo(10), "the half hour");
            Assert.That(MinorLines(15), Has.Count.EqualTo(30), "three quarters an hour");
            Assert.That(MinorLines(20), Has.Count.EqualTo(20), "two thirds an hour");
            Assert.That(MinorLines(60), Is.Empty, "every mark would land on an hour line");
            Assert.That(MinorLines(0), Is.Empty);
            Assert.That(MinorLines(-15), Is.Empty);
        });
    }

    [Test]
    public void An_interval_that_does_not_divide_the_hour_restarts_rather_than_drifting()
    {
        // 57 leaves a three-minute gap before each hour line. Ugly, but it stays hour-aligned all day
        // rather than walking away from the hours it is meant to subdivide.
        var offsets = MinorLines(57)
            .Select(line => Math.Round(line.Start.Y / HourHeight * 60))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(offsets, Has.Length.EqualTo(10), "one per hour that has an hour below it");
            Assert.That(offsets[0], Is.EqualTo(57));
            Assert.That(offsets.Zip(offsets.Skip(1), (a, b) => b - a), Is.All.EqualTo(60));
        });
    }

    [Test]
    public void Removing_the_sub_hour_marks_leaves_the_hour_lines_alone()
    {
        var drawable = Timeline();
        drawable.MinorGridLineMinutes = 60;

        var recording = Draw(drawable);

        Assert.Multiple(() =>
        {
            Assert.That(recording.Lines, Has.None.Matches<LineOperation>(line => line.Color == MinorGrid));
            Assert.That(recording.Lines.Count(line => line.Color == Grid), Is.GreaterThan(0));
        });
    }

    [Test]
    public void Disabling_current_day_highlight_does_not_disable_the_current_time_indicator()
    {
        var drawable = Timeline();
        drawable.ShowCurrentDayHighlight = false;

        var recording = Draw(drawable);

        Assert.Multiple(() =>
        {
            Assert.That(recording.Fills, Has.None.Matches<FillOperation>(fill => fill.Color == CurrentDay));
            Assert.That(recording.Lines, Has.Some.Matches<LineOperation>(line => line.Color == CurrentTime));
        });
    }

    [Test]
    public void Month_uses_working_days_and_current_day_flags_but_ignores_working_hours()
    {
        var geometry = new MonthGeometry
        {
            ViewportWidth = 700,
            ViewportHeight = 600,
            Now = new DateTime(2026, 8, 15)
        };
        var slots = new[]
        {
            Slot(new DateOnly(2026, 7, 1)),
            Slot(new DateOnly(2026, 8, 1)),
            Slot(new DateOnly(2026, 9, 1))
        };
        var drawable = new MonthGridDrawable(geometry, slots)
        {
            GridLineColor = Grid,
            NonWorkingDaysBackgroundColor = NonWorkingDay,
            AdjacentMonthBackgroundColor = Color.FromArgb("#777777"),
            CurrentDayBackgroundColor = CurrentDay,
            DayNumberColor = Color.FromArgb("#888888"),
            AdjacentMonthDayNumberColor = Color.FromArgb("#999999"),
            CurrentDayTextColor = Color.FromArgb("#AAAAAA"),
            OverflowTextColor = Color.FromArgb("#BBBBBB"),
            ShowNonWorkingDaysShading = true,
            ShowCurrentDayHighlight = false,
            WorkingDays =
            [
                DayOfWeek.Sunday,
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday
            ]
        };

        var recording = Draw(drawable);

        Assert.Multiple(() =>
        {
            Assert.That(recording.Fills, Has.None.Matches<FillOperation>(fill => fill.Color == CurrentDay));
            Assert.That(recording.Fills, Has.Some.Matches<FillOperation>(fill =>
                fill.Color == NonWorkingDay && fill.Bounds == new RectF(1100, 200, 100, 100)));
            Assert.That(recording.Fills, Has.None.Matches<FillOperation>(fill => fill.Color == NonWorkingHour));
        });
    }

    [Test]
    public void Appearance_changes_update_chrome_without_recreating_or_rebinding_appointments()
    {
        var appointment = TestAppointment.At(new DateTime(2026, 8, 24), "10:00", 1);
        var harness = new SchedulerHarness(new DateTime(2026, 8, 24), [appointment]);
        var view = harness.VisibleAppointments.Single();
        var bindingChanges = view.BindingChanges;
        var background = Color.FromArgb("#101820");

        harness.Scheduler.GridBackgroundColor = background;
        harness.Scheduler.GridLineColor = Color.FromArgb("#202830");
        harness.Scheduler.PrimaryTextColor = Color.FromArgb("#F0F0F0");
        harness.Scheduler.ShowNonWorkingDaysShading = false;

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments.Single(), Is.SameAs(view));
            Assert.That(view.BindingChanges, Is.EqualTo(bindingChanges));
            Assert.That(harness.GridBackground, Is.EqualTo(background));
            Assert.That(harness.GutterBackground, Is.EqualTo(background));
        });
    }

    [Test]
    public void The_grid_background_defaults_to_an_opaque_white_surface()
    {
        var harness = new SchedulerHarness(new DateTime(2026, 8, 24));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.GridBackgroundColor, Is.EqualTo(Colors.White));
            Assert.That(harness.GridBackground, Is.EqualTo(Colors.White));
            Assert.That(harness.GutterBackground, Is.EqualTo(Colors.White));
        });
    }

    [Test]
    public void Current_day_header_emphasis_follows_its_flag_and_colors()
    {
        var harness = new SchedulerHarness(DateTime.Today);
        var currentColor = Color.FromArgb("#123456");
        harness.Scheduler.CurrentDayTextColor = currentColor;

        Assert.That(harness.HeaderDayNumbers, Has.Some.Matches<Label>(label =>
            label.FontAttributes == FontAttributes.Bold && label.TextColor == currentColor));

        harness.Scheduler.ShowCurrentDayHighlight = false;

        Assert.That(harness.HeaderDayNumbers, Has.None.Matches<Label>(label =>
            label.FontAttributes == FontAttributes.Bold || label.TextColor == currentColor));
    }

    [Test]
    public void Default_selection_and_other_native_chrome_follow_semantic_colors()
    {
        var harness = new SchedulerHarness(new DateTime(2026, 8, 24));
        var selectionBackground = Color.FromArgb("#102030");
        var selectionBorder = Color.FromArgb("#203040");
        var selectionText = Color.FromArgb("#304050");
        var dragBackground = Color.FromArgb("#405060");
        var dragText = Color.FromArgb("#506070");
        var busy = Color.FromArgb("#607080");

        harness.Scheduler.SelectedSlot = new SchedulerTimeSlot(
            new SchedulerMoment(new DateTime(2026, 8, 24, 10, 0, 0), harness.Scheduler.TimeZone),
            TimeSpan.FromMinutes(15));
        harness.Scheduler.CellSelectionBackgroundColor = selectionBackground;
        harness.Scheduler.CellSelectionBorderColor = selectionBorder;
        harness.Scheduler.CellSelectionTextColor = selectionText;
        harness.Scheduler.DragTimeIndicatorBackgroundColor = dragBackground;
        harness.Scheduler.DragTimeIndicatorTextColor = dragText;
        harness.Scheduler.BusyIndicatorColor = busy;

        var selection = (Border)harness.CellSelectionAffordance!;

        Assert.Multiple(() =>
        {
            Assert.That(selection.BackgroundColor, Is.EqualTo(selectionBackground));
            Assert.That(((SolidColorBrush)selection.Stroke!).Color, Is.EqualTo(selectionBorder));
            Assert.That(((Label)selection.Content!).TextColor, Is.EqualTo(selectionText));
            Assert.That(harness.DragTimeIndicatorView.BackgroundColor, Is.EqualTo(dragBackground));
            Assert.That(((Label)harness.DragTimeIndicatorView.Content!).TextColor, Is.EqualTo(dragText));
            Assert.That(harness.BusyIndicator.Color, Is.EqualTo(busy));
        });
    }

    [Test]
    public void A_custom_selection_template_keeps_ownership_of_its_colors()
    {
        var harness = new SchedulerHarness(new DateTime(2026, 8, 24));
        var custom = Color.FromArgb("#ABCDEF");
        harness.Scheduler.CellSelectionTemplate = new DataTemplate(() => new Border { BackgroundColor = custom });
        harness.Scheduler.SelectedSlot = new SchedulerTimeSlot(
            new SchedulerMoment(new DateTime(2026, 8, 24, 10, 0, 0), harness.Scheduler.TimeZone),
            TimeSpan.FromMinutes(15));

        harness.Scheduler.CellSelectionBackgroundColor = Color.FromArgb("#010203");

        Assert.That(harness.CellSelectionAffordance!.BackgroundColor, Is.EqualTo(custom));
    }

    private const double HourHeight = 50;

    private static SchedulerGridDrawable Timeline(int startHour = 8, int endHour = 18)
    {
        var geometry = new SchedulerGeometry
        {
            ViewportWidth = 700,
            ViewportHeight = 500,
            VisibleDays = 7,
            HourHeight = HourHeight,
            StartHour = startHour,
            EndHour = endHour,
            Now = new DateTime(2026, 8, 26, 10, 0, 0)
        };
        geometry.SlotStarts[0] = new DateOnly(2026, 8, 17);
        geometry.SlotStarts[1] = new DateOnly(2026, 8, 24);
        geometry.SlotStarts[2] = new DateOnly(2026, 8, 31);

        return new SchedulerGridDrawable(geometry)
        {
            GridLineColor = Grid,
            MinorGridLineColor = MinorGrid,
            MinorGridLineMinutes = 30,
            NonWorkingDaysBackgroundColor = NonWorkingDay,
            NonWorkingHoursBackgroundColor = NonWorkingHour,
            CurrentDayBackgroundColor = CurrentDay,
            CurrentTimeIndicatorColor = CurrentTime,
            ShowNonWorkingDaysShading = true,
            ShowCurrentDayHighlight = true,
            ShowNonWorkingHoursShading = false,
            WorkingDays = Weekdays,
            WorkingHoursStart = new TimeOnly(9, 0),
            WorkingHoursEnd = new TimeOnly(17, 0)
        };
    }

    /// <summary>The horizontal minor-grid lines a timeline draws over one page's worth of hours.</summary>
    private static IReadOnlyList<LineOperation> MinorLines(int minutes)
    {
        var drawable = Timeline();
        drawable.MinorGridLineMinutes = minutes;

        return [.. Draw(drawable).Lines
            .Where(line => line.Color == MinorGrid)
            .DistinctBy(line => line.Start.Y)];
    }

    private static RecordingCanvas Draw(IDrawable drawable)
    {
        var (canvas, recording) = RecordingCanvas.Create();
        drawable.Draw(canvas, new RectF(0, 0, 2100, 600));
        return recording;
    }

    private static PageSlot Slot(DateOnly pageStart) => new()
    {
        PageStart = pageStart,
        Header = new Grid()
    };
}
