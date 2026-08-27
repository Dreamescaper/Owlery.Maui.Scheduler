using Microsoft.Maui.Controls;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// The month surface driven through the control, rather than through the arithmetic underneath it.
/// </summary>
/// <remarks>
/// August 2026 throughout. Its grid opens on Monday 27 July, so cell 5 is the 1st, cell 19 the 15th,
/// and cells 0-4 and 36-41 belong to July and September.
/// </remarks>
[TestFixture]
public class SchedulerMonthViewTests
{
    private static readonly DateTime August = new(2026, 8, 15);

    private const int FirstOfAugust = 5;
    private const int MiddleOfAugust = 19;

    private static SchedulerHarness Month(params ISchedulerAppointment[] items) =>
        new(August, items, viewMode: SchedulerViewMode.Month);

    private static DateTime Day(int day, int month = 8) => new(2026, month, day);

    [Test]
    public void A_month_page_shows_the_appointments_of_its_own_days()
    {
        var harness = Month(
            TestAppointment.At(Day(3), "10:00", 1),
            TestAppointment.At(Day(15), "10:00", 1));

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(2));
    }

    [Test]
    public void A_month_page_also_shows_the_neighbouring_days_its_grid_opens_and_closes_on()
    {
        var harness = Month(
            TestAppointment.At(Day(28, month: 7), "10:00", 1),
            TestAppointment.At(Day(1, month: 9), "10:00", 1));

        Assert.That(
            harness.CentrePageAppointments.Select(harness.CellOf).Order(),
            Is.EqualTo(new[] { 1, 36 }));
    }

    [Test]
    public void A_chip_lands_in_the_cell_for_its_day()
    {
        var harness = Month(TestAppointment.At(Day(15), "10:00", 1));

        Assert.That(harness.CellOf(harness.CentrePageAppointments[0]), Is.EqualTo(MiddleOfAugust));
    }

    [Test]
    public void Chips_on_one_day_stack_down_the_cell_rather_than_across_it()
    {
        var harness = Month(
            TestAppointment.At(Day(15), "09:00", 1),
            TestAppointment.At(Day(15), "11:00", 1));

        var bounds = harness.CentrePageAppointments.Select(harness.BoundsOf).OrderBy(b => b.Y).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(bounds[0].X, Is.EqualTo(bounds[1].X).Within(0.001));
            Assert.That(bounds[1].Y - bounds[0].Y,
                Is.EqualTo(MonthGeometry.ChipHeight + MonthGeometry.ChipGap).Within(0.001));
        });
    }

    [Test]
    public void An_appointment_outside_the_timeline_hours_still_appears()
    {
        // StartHour defaults to 08:00, which would have clipped this on a timeline.
        var harness = Month(TestAppointment.At(Day(15), "06:00", 1));

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(1));
    }

    [Test]
    public void A_busy_day_gives_its_last_line_to_the_marker()
    {
        // Five lines fit at this size, so seven appointments show four and the marker stands for three.
        var harness = Month([.. Enumerable.Range(0, 7)
            .Select(i => TestAppointment.At(Day(15), $"{9 + i:00}:00", 1))]);

        Assert.That(harness.CentrePageAppointments, Has.Count.EqualTo(4));
    }

    [Test]
    public void Every_page_carries_its_own_weekday_row()
    {
        // A single fixed row would stay put while the pages slide under it, which reads as the
        // calendar coming apart from its own heading.
        var harness = Month();

        var headers = harness.PageHeaders;

        Assert.That(headers, Has.Count.EqualTo(3));
        Assert.That(
            headers.Select(header => header.Count),
            Is.All.EqualTo(MonthGeometry.Columns),
            "seven columns on each of the three rendered pages");
    }

    [Test]
    public void The_weekday_row_names_the_columns_and_carries_no_day_numbers()
    {
        // The day numbers belong in the cells, where they are painted with the grid.
        var harness = new SchedulerHarness(new DateTime(2026, 8, 15), viewMode: SchedulerViewMode.Month);

        Assert.That(harness.PageHeaders[1].Select(label => label.Text), Is.EqualTo(new[]
        {
            "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"
        }));
    }

    [Test]
    public void Swiping_forward_moves_on_by_one_month()
    {
        var harness = Month();

        harness.SwipeToPage(2);

        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(new DateTime(2026, 9, 1)));
    }

    [Test]
    public void Swiping_back_from_January_lands_in_the_previous_year()
    {
        var harness = new SchedulerHarness(new DateTime(2027, 1, 15), viewMode: SchedulerViewMode.Month);

        harness.SwipeToPage(0);

        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(new DateTime(2026, 12, 1)));
    }

    [Test]
    public void Month_lengths_do_not_accumulate_across_swipes()
    {
        var harness = Month();

        harness.SwipeToPage(2);
        harness.SwipeToPage(2);
        harness.SwipeToPage(2);

        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(new DateTime(2026, 11, 1)));
    }

    [Test]
    public void The_visible_dates_are_the_whole_six_row_grid()
    {
        var harness = Month();

        var report = harness.VisibleDatesReports[^1];

        Assert.Multiple(() =>
        {
            Assert.That(report.VisibleDates, Has.Count.EqualTo(MonthGeometry.CellCount));
            Assert.That(report.VisibleDates[0].Date, Is.EqualTo(new DateTime(2026, 7, 27)));
            Assert.That(report.VisibleDates[^1].Date, Is.EqualTo(new DateTime(2026, 9, 6)));
        });
    }

    [Test]
    public void The_prefetch_range_covers_the_grids_either_side_rather_than_the_months()
    {
        var harness = Month();

        var report = harness.VisibleDatesReports[^1];

        Assert.Multiple(() =>
        {
            Assert.That(report.PrefetchFrom.Date, Is.EqualTo(new DateTime(2026, 6, 29)));
            Assert.That(report.PrefetchTo.Date, Is.EqualTo(new DateTime(2026, 10, 11)));
        });
    }

    [Test]
    public void Tapping_an_empty_cell_selects_the_whole_day()
    {
        var harness = Month();

        harness.Tap(harness.MonthCellAt(1, MiddleOfAugust));

        Assert.That(harness.CellTaps, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(harness.CellTaps[0].Slot.Start, Is.EqualTo(new DateTime(2026, 8, 15)));
            Assert.That(harness.CellTaps[0].Slot.Duration, Is.EqualTo(TimeSpan.FromDays(1)));
        });
    }

    [Test]
    public void Tapping_a_cell_from_the_previous_month_selects_that_day_not_a_clamped_one()
    {
        var harness = Month();

        harness.Tap(harness.MonthCellAt(1, 0));

        Assert.That(harness.CellTaps[0].Slot.Start, Is.EqualTo(new DateTime(2026, 7, 27)));
    }

    [Test]
    public void No_selection_affordance_is_shown_on_a_month()
    {
        // The "+" means "tap again to create something here", and a month cell has no time to create
        // anything at — tapping one opens that day instead.
        var harness = Month();

        harness.Tap(harness.MonthCellAt(1, FirstOfAugust));

        Assert.That(harness.CellSelectionAffordance, Is.Null);
    }

    [Test]
    public void A_tapped_day_is_still_reported_as_selected()
    {
        // Only the affordance is withheld; the selection itself is part of the contract.
        var harness = Month();

        harness.Tap(harness.MonthCellAt(1, FirstOfAugust));

        Assert.That(harness.Scheduler.SelectedSlot?.Start, Is.EqualTo(new DateTime(2026, 8, 1)));
    }

    [Test]
    public void Tapping_a_chip_reports_the_appointment()
    {
        var appointment = TestAppointment.At(Day(15), "10:00", 1, "lesson");
        var harness = Month(appointment);

        var chip = harness.BoundsOf(harness.CentrePageAppointments[0]);
        harness.Tap(chip.Center);

        Assert.That(harness.AppointmentTaps, Has.Count.EqualTo(1));
        Assert.That(harness.AppointmentTaps[0].Appointment.Key, Is.EqualTo(appointment.Key));
    }

    [Test]
    public void Holding_a_chip_does_not_pick_it_up()
    {
        // Dragging is a timeline affordance; a month has no time to drop onto.
        var harness = Month(TestAppointment.At(Day(15), "10:00", 1));

        var chip = harness.BoundsOf(harness.CentrePageAppointments[0]);
        harness.BeginDrag(chip.Center);

        Assert.Multiple(() =>
        {
            Assert.That(harness.DragStarts, Is.Empty);
            Assert.That(harness.DraggedAppointment, Is.Null);
            Assert.That(harness.GhostAppointment, Is.Null);
        });
    }

    [Test]
    public void Switching_from_the_timeline_lays_the_same_appointments_out_as_a_month()
    {
        var harness = new SchedulerHarness(August, [TestAppointment.At(Day(15), "10:00", 1)]);

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;

        // DisplayDate is left alone — it means "some day on the page", and is only rewritten by a
        // swipe — so what is on screen is read from the report instead.
        var report = harness.VisibleDatesReports[^1];

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
            Assert.That(report.VisibleDates, Has.Count.EqualTo(MonthGeometry.CellCount));
            Assert.That(report.VisibleDates[0].Date, Is.EqualTo(new DateTime(2026, 7, 27)));
        });
    }

    [Test]
    public void Switching_back_to_the_timeline_returns_to_the_week_of_the_day_shown()
    {
        var harness = new SchedulerHarness(August, [TestAppointment.At(Day(15), "10:00", 1)]);

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        // 15 August 2026 is a Saturday, so the week it belongs to opens on Monday the 10th.
        var report = harness.VisibleDatesReports[^1];

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
            Assert.That(report.VisibleDates, Has.Count.EqualTo(7));
            Assert.That(report.VisibleDates[0].Date, Is.EqualTo(new DateTime(2026, 8, 10)));
        });
    }

    [Test]
    public void Returning_to_a_full_week_from_a_month_rebuilds_the_headers()
    {
        // The reported crash. A month header and a week header are both seven columns wide, so the
        // week found one that fitted by width, left it alone, and then indexed day-number labels the
        // month had never made. Reaching it needs a header genuinely built for a month first, which
        // is why it only happened when the day count had been something other than seven.
        var harness = new SchedulerHarness(August, visibleDays: 1);

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;
        harness.Scheduler.VisibleDays = 7;

        Assert.DoesNotThrow(() => harness.Scheduler.ViewMode = SchedulerViewMode.Timeline);
        Assert.That(harness.HeaderDayNumbers, Is.Not.Empty, "a timeline names its days and numbers them");
    }

    [Test]
    public void A_month_header_carries_no_day_numbers_even_arriving_from_a_full_week()
    {
        var harness = new SchedulerHarness(August);

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;

        Assert.That(harness.HeaderDayNumbers, Is.Empty);
    }

    [Test]
    public void Switching_mode_does_not_leave_the_old_views_behind()
    {
        var harness = new SchedulerHarness(August, [TestAppointment.At(Day(15), "10:00", 1)]);

        harness.Scheduler.ViewMode = SchedulerViewMode.Month;

        // The pool is emptied on the way across, so nothing built from the timeline's template
        // survives to be rented for a chip.
        Assert.That(harness.AllAppointmentViews, Has.Count.EqualTo(1));
    }
}
