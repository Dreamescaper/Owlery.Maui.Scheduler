using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class SchedulerAgendaViewTests
{
    /// <summary>Monday, 3 August 2026.</summary>
    private static readonly DateTime Monday = new(2026, 8, 3);

    private static SchedulerHarness Agenda(params ISchedulerAppointment[] items) =>
        new(Monday, items, viewMode: SchedulerViewMode.Agenda);

    /// <summary>One appointment a day, starting on the Monday.</summary>
    private static ISchedulerAppointment[] Daily(int days) =>
        [.. Enumerable.Range(0, days).Select(i => TestAppointment.At(Monday.AddDays(i), "10:00", 1, $"a{i}"))];

    /// <summary>
    /// Many appointments a day, over a fortnight — enough rows to dwarf the window.
    /// </summary>
    /// <remarks>
    /// Dense rather than long on purpose. The agenda covers a month either side of its page, so
    /// spreading the same count over more days would push most of it outside the range and prove
    /// nothing about what the window realizes.
    /// </remarks>
    private static ISchedulerAppointment[] Dense(int days = 14, int perDay = 10) =>
    [
        .. from day in Enumerable.Range(0, days)
           from n in Enumerable.Range(0, perDay)
           select TestAppointment.At(Monday.AddDays(day), $"{8 + n:00}:00", 1, $"d{day}n{n}")
    ];

    [Test]
    public void Rows_are_stacked_down_a_single_column()
    {
        var harness = Agenda(Daily(3));

        var bounds = harness.VisibleAppointments.Select(harness.BoundsOf).OrderBy(b => b.Y).ToArray();

        Assert.That(bounds, Has.Length.EqualTo(3));
        Assert.Multiple(() =>
        {
            // One column: every row starts at the same x, past the day gutter.
            Assert.That(bounds.Select(b => b.X).Distinct().Count(), Is.EqualTo(1));
            Assert.That(bounds[0].X, Is.GreaterThan(0), "rows are inset past the gutter");

            // And they do not overlap.
            Assert.That(bounds[1].Y, Is.GreaterThanOrEqualTo(bounds[0].Y + bounds[0].Height));
            Assert.That(bounds[2].Y, Is.GreaterThanOrEqualTo(bounds[1].Y + bounds[1].Height));
        });
    }

    [Test]
    public void Known_row_height_bypasses_the_template_measurement_result()
    {
        var appointment = Daily(1);
        var calls = 0;
        var harness = new SchedulerHarness(
            Monday,
            appointment,
            viewMode: SchedulerViewMode.Agenda,
            configure: scheduler => scheduler.AgendaRowHeight = item =>
            {
                calls++;
                Assert.That(item, Is.SameAs(appointment[0]));
                return 73;
            });

        Assert.Multiple(() =>
        {
            Assert.That(harness.BoundsOf(harness.VisibleAppointments.Single()).Height, Is.EqualTo(73));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Changing_known_row_height_reflows_the_existing_agenda()
    {
        var harness = Agenda(Daily(1));

        harness.Scheduler.AgendaRowHeight = _ => 86;

        Assert.That(harness.BoundsOf(harness.VisibleAppointments.Single()).Height, Is.EqualTo(86));
    }

    [Test]
    public void A_day_marker_sits_in_the_gutter_and_a_month_heading_spans_the_width()
    {
        var harness = Agenda(Daily(2));

        var markers = harness.AgendaSections
            .Where(v => ((SchedulerAgendaSection)v.BindingContext).Kind is SchedulerAgendaSectionKind.Day)
            .Select(harness.BoundsOf)
            .ToArray();

        var month = harness.AgendaSections
            .Where(v => ((SchedulerAgendaSection)v.BindingContext).Kind is SchedulerAgendaSectionKind.Month)
            .Select(harness.BoundsOf)
            .First();

        Assert.Multiple(() =>
        {
            Assert.That(markers, Has.Length.EqualTo(2), "one marker per day, not per appointment");
            Assert.That(markers.Select(m => m.X), Is.All.Zero);
            // The month heading lines up with the rows beneath it, past the gutter the day marks occupy.
            Assert.That(month.X, Is.EqualTo(harness.Scheduler.AgendaDayGutterWidth + AgendaGeometry.RowInset));
            Assert.That(month.Width, Is.GreaterThan(markers[0].Width), "a month heading is not confined to the gutter");
        });
    }

    [Test]
    public void Every_month_in_range_is_announced_and_empty_days_are_not()
    {
        // One appointment, in August. September's heading is still there; the 60 other days are not.
        var harness = Agenda(TestAppointment.At(Monday, "10:00", 1));

        Assert.Multiple(() =>
        {
            Assert.That(harness.SectionsOfKind(SchedulerAgendaSectionKind.Month), Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(harness.SectionsOfKind(SchedulerAgendaSectionKind.Day), Has.Count.EqualTo(1));
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void The_built_in_month_heading_uses_the_configured_empty_text()
    {
        var harness = Agenda();
        var month = harness.AgendaSections.First(view =>
            view.BindingContext is SchedulerAgendaSection { Kind: SchedulerAgendaSectionKind.Month });

        Assert.That(harness.TextWithin(month), Does.Contain("No appointments"));

        harness.Scheduler.AgendaEmptyText = "Nothing planned";

        Assert.That(harness.TextWithin(month), Does.Contain("Nothing planned"));
    }

    [Test]
    public void A_heading_repaints_when_the_appearance_colors_change()
    {
        // UpdateAppearance runs for every heading in the window on every realization pass, and the
        // agenda realizes on its scroll path — so it skips the repaint when the colours have not
        // moved. This is the direction that guard must not swallow: a colour that has.
        var harness = Agenda(Daily(1));
        var month = harness.AgendaSections.First(view =>
            view.BindingContext is SchedulerAgendaSection { Kind: SchedulerAgendaSectionKind.Month });
        var repainted = Color.FromArgb("#123456");

        harness.Scheduler.PrimaryTextColor = repainted;

        Assert.That(harness.LabelColorsWithin(month), Does.Contain(repainted));
    }

    [Test]
    public void Headings_are_described_for_a_screen_reader()
    {
        var harness = Agenda(Daily(1));

        Assert.That(
            harness.AgendaSections.Select(SemanticProperties.GetDescription),
            Is.All.Not.Null.And.All.Not.Empty);
    }

    [Test]
    public void The_agenda_does_not_page_sideways()
    {
        var harness = Agenda(Daily(5));
        var before = harness.Scheduler.DisplayDate;

        harness.SettlePager();

        Assert.Multiple(() =>
        {
            // A one-page surface reports page 0, which without the guard retreats a whole month.
            Assert.That(harness.Scheduler.DisplayDate, Is.EqualTo(before));
            Assert.That(harness.PageStride, Is.Zero);
        });
    }

    [Test]
    public void The_visible_dates_report_describes_the_loaded_vertical_range()
    {
        var harness = Agenda(Daily(5));
        var report = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(report.VisibleDates, Has.Count.GreaterThan(31));
            Assert.That(report.PrefetchFrom.WallClock.Date, Is.EqualTo(report.VisibleDates[0].WallClock.Date));
            Assert.That(report.PrefetchTo.WallClock.Date, Is.EqualTo(report.VisibleDates[^1].WallClock.Date));
        });
    }

    [Test]
    public void Only_the_rows_near_the_scroll_position_exist_as_views()
    {
        var harness = Agenda(Dense());

        // 140 rows, all inside the range. A viewport of 700 plus half a screen of overscan either
        // side cannot honestly hold more than about 50 of them at the minimum row height.
        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Is.Not.Empty);
            Assert.That(harness.VisibleAppointments, Has.Count.LessThan(140 / 2));
        });
    }

    [Test]
    public void Scrolling_realizes_the_rows_that_come_into_view()
    {
        var harness = Agenda(Dense());

        var atTop = harness.VisibleAppointments.Select(v => v.BindingContext).ToArray();

        // Half way down whatever the rows actually came to, rather than a guessed offset.
        harness.ScrollVerticallyTo(harness.SurfaceRequestedHeight / 2);

        var lower = harness.VisibleAppointments.Select(v => v.BindingContext).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(lower, Is.Not.Empty, "scrolling must not leave the list blank");
            Assert.That(lower, Is.Not.EqualTo(atTop).AsCollection, "different rows are showing");
        });
    }

    [Test]
    public void Scrolling_does_not_rebind_views_that_stay_in_the_window()
    {
        var harness = Agenda(Dense());
        var before = harness.VisibleAppointments.ToDictionary(
            view => view,
            view => (Appointment: (ISchedulerAppointment)view.BindingContext, view.BindingChanges));

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 300);

        var survivors = harness.VisibleAppointments.Where(before.ContainsKey).ToArray();

        Assert.That(survivors, Is.Not.Empty, "the two overscanned windows should overlap");

        foreach (var view in survivors)
        {
            Assert.Multiple(() =>
            {
                Assert.That(view.BindingContext, Is.SameAs(before[view].Appointment));
                Assert.That(view.BindingChanges, Is.EqualTo(before[view].BindingChanges));
            });
        }
    }

    [Test]
    public void Scrolling_updates_display_date_without_snapping_the_viewport()
    {
        // Use a known height so this test isolates DisplayDate navigation from the independent
        // correction applied when newly-realized rows measure differently from their estimate.
        var harness = new SchedulerHarness(
            Monday,
            Daily(3),
            viewMode: SchedulerViewMode.Agenda,
            configure: scheduler => scheduler.AgendaRowHeight = _ => TestAppointmentView.MeasuredHeight);
        var targetDate = Monday.AddDays(1).Date;
        var target = harness.VisibleAppointments.First(view =>
            ((ISchedulerAppointment)view.BindingContext).Start.Date == targetDate);
        var requested = harness.BoundsOf(target).Y + 5;

        Assert.That(requested - harness.AgendaScrollY, Is.LessThan(SchedulerHarness.ViewHeight / 2),
            "the movement must stay below the realization throttle to exercise viewport-only synchronization");

        harness.ScrollVerticallyTo(requested);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(targetDate));
            Assert.That(harness.AgendaScrollY, Is.EqualTo(requested).Within(0.5),
                "the internal two-way update must not navigate back to the start of the date");
        });
    }

    [Test]
    public void Scroll_to_the_current_agenda_date_still_repositions_it()
    {
        var items = Enumerable.Range(0, 30)
            .Select(i => (ISchedulerAppointment)new TestAppointment(
                Monday.AddMinutes(i), Monday.AddMinutes(i + 1), $"a{i}"))
            .ToArray();
        var harness = Agenda(items);
        var firstTop = harness.VisibleAppointments.Min(view => harness.BoundsOf(view).Y);

        harness.ScrollVerticallyTo(firstTop + 400);
        Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(Monday.Date));

        harness.Scheduler.ScrollToDate(Monday);

        Assert.That(harness.AgendaScrollY, Is.EqualTo(firstTop).Within(0.5));
    }

    [Test]
    public void Changing_display_date_to_the_previous_month_reports_one_range()
    {
        var previous = Monday.AddDays(-7);
        var harness = Agenda(
            TestAppointment.At(previous, "10:00", 1, "previous"),
            TestAppointment.At(Monday, "10:00", 1, "current"));
        var reportsBefore = harness.VisibleDatesReports.Count;

        harness.Scheduler.DisplayDate = previous;

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleDatesReports, Has.Count.EqualTo(reportsBefore + 1));
            Assert.That(harness.Scheduler.DisplayDate.Date, Is.EqualTo(previous.Date));
        });
    }

    [Test]
    public void Data_supplied_from_the_initial_range_report_is_used_for_initial_navigation()
    {
        var appointments = Enumerable.Range(0, 30)
            .Select(i => (ISchedulerAppointment)new TestAppointment(
                Monday.AddMinutes(i), Monday.AddMinutes(i + 1), $"loaded-{i}"))
            .ToArray();
        var harness = new SchedulerHarness(
            Monday,
            [],
            viewMode: SchedulerViewMode.Agenda,
            configure: scheduler => scheduler.VisibleDatesChanged += (_, _) =>
                scheduler.ItemsSource = appointments);

        var visible = harness.VisibleAppointments.First(view =>
            ReferenceEquals(view.BindingContext, appointments[0]));

        Assert.Multiple(() =>
        {
            Assert.That(visible.BindingContext, Is.SameAs(appointments[0]));
            Assert.That(harness.BoundsOf(visible).Y, Is.EqualTo(harness.AgendaScrollY).Within(0.5));
        });
    }

    [Test]
    public void Measuring_unvisited_rows_above_the_window_compensates_the_scroll_offset()
    {
        var harness = Agenda(Dense());

        // Make the estimate deliberately much taller than the deterministic 40-unit test view.
        harness.Scheduler.AgendaEstimatedRowHeight = 120;
        var requested = harness.SurfaceRequestedHeight / 2;

        harness.ScrollVerticallyTo(requested);

        Assert.That(
            harness.AgendaScrollY,
            Is.LessThan(requested),
            "shorter rows in the overscan above the fold should pull the offset up by the same delta");
    }

    [Test]
    public void Reaching_the_bottom_extends_the_loaded_range_forward()
    {
        var harness = Agenda(Dense());
        var before = harness.VisibleDatesReports.Last();

        harness.ScrollVerticallyTo(harness.SurfaceRequestedHeight);

        var after = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(after.PrefetchFrom.WallClock, Is.EqualTo(before.PrefetchFrom.WallClock));
            Assert.That(after.PrefetchTo.WallClock, Is.GreaterThan(before.PrefetchTo.WallClock));
            Assert.That(after.VisibleDates.Count, Is.GreaterThan(before.VisibleDates.Count));
        });
    }

    [Test]
    public void Reaching_the_top_extends_backward_without_moving_the_old_content()
    {
        var harness = Agenda(Dense());
        var before = harness.VisibleDatesReports.Last();

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);
        harness.FireAgendaBackwardGrowTimer();

        var after = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(after.PrefetchFrom.WallClock, Is.LessThan(before.PrefetchFrom.WallClock));
            Assert.That(after.PrefetchTo.WallClock, Is.EqualTo(before.PrefetchTo.WallClock));

            // Asking for the month lays nothing out, so the reader is exactly where they were. The
            // offset moves when the host answers, which is what the sibling test covers.
            Assert.That(harness.AgendaScrollY, Is.Zero,
                "asking for an earlier month does not move the reader");
        });
    }

    [Test]
    public void Data_arriving_in_a_new_prefix_keeps_the_visible_anchor_still()
    {
        var original = Dense();
        var harness = Agenda(original);

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);
        harness.FireAgendaBackwardGrowTimer();
        var before = harness.AgendaScrollY;

        harness.Scheduler.ItemsSource = original.Append(
            TestAppointment.At(new DateTime(2026, 6, 26), "10:00", 1, "earlier"));

        Assert.That(harness.AgendaScrollY, Is.GreaterThan(before),
            "new rows above the visible anchor add the same amount to the offset");
    }

    [Test]
    public void Switching_to_the_agenda_and_back_leaves_nothing_stranded()
    {
        var items = Daily(5);
        var harness = new SchedulerHarness(Monday, items);

        var onTimeline = harness.VisibleAppointments.Count;

        harness.Scheduler.ViewMode = SchedulerViewMode.Agenda;
        harness.Scheduler.ViewMode = SchedulerViewMode.Timeline;

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Has.Count.EqualTo(onTimeline));
            Assert.That(harness.AgendaSections, Is.Empty, "headings do not survive the mode they belong to");
        });
    }

    [Test]
    public void A_day_count_change_is_ignored_while_the_agenda_is_showing()
    {
        var harness = Agenda(Daily(3));
        var before = harness.VisibleAppointments.Select(harness.BoundsOf).ToArray();

        harness.Scheduler.VisibleDays = 1;

        Assert.That(
            harness.VisibleAppointments.Select(harness.BoundsOf),
            Is.EqualTo(before).AsCollection,
            "an agenda is not made of a number of day columns");
    }

    [Test]
    public void A_tap_on_a_row_selects_the_whole_day()
    {
        var harness = Agenda(Daily(3));

        var first = harness.VisibleAppointments.OrderBy(v => harness.BoundsOf(v).Y).First();
        var bounds = harness.BoundsOf(first);

        harness.Tap(bounds.X + 5, bounds.Y + bounds.Height / 2);

        Assert.That(harness.AppointmentTaps, Has.Count.EqualTo(1));
    }

    [Test]
    public void Scroll_to_date_positions_that_days_first_appointment()
    {
        var harness = Agenda(Dense());
        var target = Monday.AddDays(8);

        harness.Scheduler.ScrollToDate(target);

        var first = harness.VisibleAppointments
            .OrderBy(view => harness.BoundsOf(view).Y)
            .First(view => ((ISchedulerAppointment)view.BindingContext).Start.Date >= target.Date);

        Assert.Multiple(() =>
        {
            Assert.That(((ISchedulerAppointment)first.BindingContext).Start.Date, Is.EqualTo(target.Date));
            Assert.That(harness.BoundsOf(first).Y, Is.EqualTo(harness.AgendaScrollY).Within(0.5));
        });
    }

    /// <summary>
    /// Growing forward is a request to the host, and one request is asked at a time.
    /// </summary>
    /// <remarks>
    /// A month the host has nothing for adds only its own heading, so the position that asked keeps
    /// asking. Ungated, every frame of a fling into the end would add another month and rebuild the
    /// whole row table for it.
    /// </remarks>
    [Test]
    public void A_fling_into_the_end_asks_the_host_for_one_month_not_a_month_per_frame()
    {
        var harness = Agenda(Dense());
        var before = harness.VisibleDatesReports.Last();
        var reportsBefore = harness.VisibleDatesReports.Count;

        // Ten events, each a little further down than the last, none of them answered with new items.
        for (var i = 0; i < 10; i++)
            harness.ScrollVerticallyTo(harness.SurfaceRequestedHeight + i);

        var after = harness.VisibleDatesReports.Last();

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleDatesReports, Has.Count.EqualTo(reportsBefore + 1));
            Assert.That(after.PrefetchTo.WallClock, Is.GreaterThan(before.PrefetchTo.WallClock));
            Assert.That((after.PrefetchTo.WallClock - before.PrefetchTo.WallClock).TotalDays, Is.LessThanOrEqualTo(31));
        });
    }

    /// <summary>And the gate opens again once the host has answered — the direction that can break.</summary>
    [Test]
    public void The_end_asks_again_once_the_host_has_sent_what_it_asked_for()
    {
        var items = Dense();
        var harness = Agenda(items);

        harness.ScrollVerticallyTo(harness.SurfaceRequestedHeight);
        var afterFirst = harness.VisibleDatesReports.Last();

        // The host answers, which is what makes the next stretch of list worth asking about.
        harness.Scheduler.ItemsSource = items.ToList();
        harness.ScrollVerticallyTo(harness.SurfaceRequestedHeight + 1);

        Assert.That(harness.VisibleDatesReports.Last().PrefetchTo.WallClock, Is.GreaterThan(afterFirst.PrefetchTo.WallClock));
    }

    /// <summary>
    /// A fling into the top settles by springing back, and the spring is not the reader leaving.
    /// </summary>
    [Test]
    public void A_spring_back_at_the_top_does_not_cancel_the_growth_the_fling_asked_for()
    {
        var harness = Agenda(Dense());
        var before = harness.VisibleDatesReports.Last();

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);

        // The platform settling: a positive delta, still well inside the overscan.
        harness.ScrollVerticallyTo(4);
        harness.FireAgendaBackwardGrowTimer();

        Assert.That(harness.VisibleDatesReports.Last().PrefetchFrom.WallClock, Is.LessThan(before.PrefetchFrom.WallClock));
    }

    /// <summary>Leaving the top really does cancel it, which is the half that must keep working.</summary>
    [Test]
    public void Leaving_the_top_cancels_a_deferred_backward_growth()
    {
        var harness = Agenda(Dense());

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);

        var before = harness.VisibleDatesReports.Last();

        harness.ScrollVerticallyTo(2000);
        harness.FireAgendaBackwardGrowTimer();

        Assert.That(harness.VisibleDatesReports.Last().PrefetchFrom.WallClock, Is.EqualTo(before.PrefetchFrom.WallClock));
    }

    /// <summary>
    /// Headings belong to the rows they introduce, and go when there are no rows left.
    /// </summary>
    /// <remarks>
    /// The surface answers <c>SectionsFor</c> from its last slice, so a populate pass that has nothing
    /// to lay out has to say so rather than leave the previous window standing.
    /// </remarks>
    [Test]
    public void Clearing_the_items_takes_the_headings_with_the_rows()
    {
        var harness = Agenda(Daily(3));

        Assert.That(harness.AgendaSections, Is.Not.Empty, "there is something to clear");

        harness.Scheduler.ItemsSource = null;

        Assert.Multiple(() =>
        {
            Assert.That(harness.VisibleAppointments, Is.Empty);
            Assert.That(harness.AgendaSections, Is.Empty, "no headings over rows that are gone");
        });
    }

    /// <summary>
    /// A geometry change that cannot alter a row's size keeps what the rows already measured.
    /// </summary>
    /// <remarks>
    /// Measurements are cached by appointment key and only the width a row was measured at can make
    /// one wrong. Throwing them away puts every row above the viewport back at the estimate, and the
    /// content above the reader changing height is exactly what moves the offset under them.
    /// </remarks>
    [Test]
    public void An_unrelated_geometry_change_does_not_re_measure_the_rows_above_the_viewport()
    {
        var harness = Agenda(Dense());

        harness.ScrollVerticallyTo(1500);
        var before = harness.AgendaScrollY;

        // Timeline geometry: it reaches ApplyGeometry, and cannot change what an agenda row measures.
        harness.Scheduler.HourHeight += 10;

        Assert.That(harness.AgendaScrollY, Is.EqualTo(before).Within(0.5));
    }

    /// <summary>
    /// Builds an agenda whose entry navigation the platform has not applied, as iOS leaves it.
    /// </summary>
    private static SchedulerHarness AgendaEnteredWithScrollPending()
    {
        var harness = new SchedulerHarness(Monday, Dense(), viewMode: SchedulerViewMode.Timeline)
        {
            DeferVerticalScrollRequests = true
        };

        harness.Scheduler.ViewMode = SchedulerViewMode.Agenda;

        return harness;
    }

    /// <summary>
    /// Offsets the platform passes through on its way to a requested one are not the reader.
    /// </summary>
    /// <remarks>
    /// Taken from the iOS simulator: entering an agenda from a timeline scrolled part-way down
    /// requested an offset thousands of units into the list, and the platform reported two settling
    /// offsets near the top instead — the second above the first. Read as a gesture, that is a fling
    /// into the top, and the range grew backwards before the reader had touched anything.
    /// </remarks>
    [Test]
    public void The_platform_settling_after_a_navigation_is_not_read_as_a_fling_to_the_top()
    {
        var harness = AgendaEnteredWithScrollPending();
        var before = harness.VisibleDatesReports.Last();

        Assert.That(harness.AgendaScrollY, Is.Not.EqualTo(harness.LastVerticalScrollRequest).Within(0.5),
            "the platform has not applied the offset entry asked for");

        harness.ScrollVerticallyTo(135);
        harness.ScrollVerticallyTo(83);
        harness.FireAgendaBackwardGrowTimer();

        Assert.That(harness.VisibleDatesReports.Last().PrefetchFrom.WallClock, Is.EqualTo(before.PrefetchFrom.WallClock),
            "the platform settling toward a requested offset is not a reader at the top");
    }

    /// <summary>
    /// And once the window closes the reader has their scrolling back — the half that can break.
    /// </summary>
    [Test]
    public void A_navigation_the_platform_never_applied_gives_scrolling_back_when_it_settles()
    {
        var harness = AgendaEnteredWithScrollPending();

        // The platform stops short of the offset it was asked for and reports nothing further.
        harness.ScrollVerticallyTo(120);
        harness.FireAgendaNavigationSettleTimer();

        var before = harness.VisibleDatesReports.Last();

        // Now the reader really does scroll to the top.
        harness.ScrollVerticallyTo(0);
        harness.FireAgendaBackwardGrowTimer();

        Assert.That(harness.VisibleDatesReports.Last().PrefetchFrom.WallClock, Is.LessThan(before.PrefetchFrom.WallClock),
            "after the settle window the agenda follows the reader again");
    }

    /// <summary>A month of appointments in June, which the host supplies once asked for it.</summary>
    /// <summary>
    /// An agenda whose rows state their own height, so nothing is measured or corrected.
    /// </summary>
    /// <remarks>
    /// Isolates compensation from the estimate-to-measured settling that otherwise moves rows for
    /// its own reasons — the two are separate mechanisms and a test that spans both proves neither.
    /// </remarks>
    private static SchedulerHarness AgendaWithKnownRowHeights(params ISchedulerAppointment[] items) =>
        new(Monday, items, viewMode: SchedulerViewMode.Agenda,
            configure: scheduler => scheduler.AgendaRowHeight = _ => 40);

    private static ISchedulerAppointment[] Earlier(int days = 20, int perDay = 5) =>
    [
        .. from day in Enumerable.Range(0, days)
           from n in Enumerable.Range(0, perDay)
           select TestAppointment.At(new DateTime(2026, 6, 1).AddDays(day), $"{8 + n:00}:00", 1, $"e{day}n{n}")
    ];

    /// <summary>
    /// Content arriving above the viewport does not move what the reader is looking at.
    /// </summary>
    /// <remarks>
    /// The rows below an insertion are reflowed and the surface regrown synchronously, while the
    /// offset that pays for them is applied by the handler on a later pass. A month of appointments
    /// is thousands of units of that, and between the two writes the reader was shown the inserted
    /// month and then snapped back — the jump-and-reset this test exists to catch.
    /// </remarks>
    [Test]
    public void A_month_arriving_above_the_viewport_leaves_the_visible_rows_where_they_are()
    {
        var items = Dense();
        var harness = AgendaWithKnownRowHeights(items);

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);

        // From here the platform stops applying requested offsets, which is the state it is in
        // between the reflow and the frame its handler gets to.
        harness.DeferVerticalScrollRequests = true;

        var anchor = harness.VisibleAppointments.OrderBy(view => harness.BoundsOf(view).Y).First();
        var anchorKey = ((ISchedulerAppointment)anchor.BindingContext).Key;
        var before = harness.VisualTopOf(anchor);

        harness.FireAgendaBackwardGrowTimer();

        // The host answers the widened range with a month that belongs above the reader.
        harness.Scheduler.ItemsSource = (ISchedulerAppointment[])[.. Earlier(), .. items];

        var after = harness.VisibleAppointments
            .Single(view => Equals(((ISchedulerAppointment)view.BindingContext).Key, anchorKey));

        Assert.That(harness.VisualTopOf(after), Is.EqualTo(before).Within(1),
            "the row the reader was looking at did not move");
    }

    /// <summary>And the hold is let go once the platform reports the offset — the half that sticks.</summary>
    [Test]
    public void The_hold_is_released_when_the_platform_applies_the_offset()
    {
        var items = Dense();
        var harness = AgendaWithKnownRowHeights(items);

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);
        harness.DeferVerticalScrollRequests = true;

        harness.FireAgendaBackwardGrowTimer();
        harness.Scheduler.ItemsSource = (ISchedulerAppointment[])[.. Earlier(), .. items];

        Assert.That(harness.SurfaceTranslationY, Is.Not.EqualTo(0).Within(0.5),
            "the content is being held while the platform owes the offset");

        // The handler gets its frame and reports the offset that was asked for.
        harness.DeferVerticalScrollRequests = false;
        harness.ScrollVerticallyTo(harness.LastVerticalScrollRequest);

        Assert.That(harness.SurfaceTranslationY, Is.EqualTo(0).Within(0.5),
            "a hold left behind would be a permanent visual offset");
    }

    /// <summary>
    /// Asking for an earlier month does not make a week straddling the old edge sprout a heading.
    /// </summary>
    /// <remarks>
    /// Caught frame by frame on the simulator, and the whole of the reported blink. Sunday 1 March
    /// 2026 is the tail of a week beginning 23 February. While the range starts at 1 March that week
    /// is not wholly loaded and gets no heading (AGD-4). Widening the range to February before the
    /// host has answered made it eligible for one — and its only populated day was still 1 March, so
    /// the heading appeared under the March banner, pushed every row down by its own height, and
    /// jumped back out of view a frame later when February's appointments arrived and carried it up
    /// into February where it belonged.
    /// </remarks>
    [Test]
    public void Asking_for_an_earlier_month_does_not_add_a_heading_for_a_week_it_has_not_loaded()
    {
        // The range reaches one whole month before the page's month, so an April page starts on
        // 1 March — putting the edge mid-week, since Sunday 1 March closes a week begun 23 February.
        var march = new DateTime(2026, 3, 1);
        var items = (ISchedulerAppointment[])
        [
            .. from day in Enumerable.Range(0, 20)
               from n in Enumerable.Range(0, 4)
               select TestAppointment.At(march.AddDays(day), $"{9 + n:00}:00", 1, $"m{day}n{n}")
        ];

        var harness = new SchedulerHarness(
            new DateTime(2026, 4, 15), items, viewMode: SchedulerViewMode.Agenda);

        var straddling = DateOnly.FromDateTime(march).AddDays(-6).ToDateTime(TimeOnly.MinValue);

        Assert.That(harness.SectionsOfKind(SchedulerAgendaSectionKind.Week).Select(s => s.Date),
            Does.Not.Contain(straddling),
            "the week is not wholly loaded, so it has no heading to begin with");

        harness.ScrollVerticallyTo(harness.AgendaScrollY + 500);
        harness.ScrollVerticallyTo(0);
        harness.FireAgendaBackwardGrowTimer();

        Assert.That(harness.SectionsOfKind(SchedulerAgendaSectionKind.Week).Select(s => s.Date),
            Does.Not.Contain(straddling),
            "asking for February must not announce a week whose February days are not loaded");
    }
}
