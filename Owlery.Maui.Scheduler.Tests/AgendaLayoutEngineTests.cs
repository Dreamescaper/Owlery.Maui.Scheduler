using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class AgendaLayoutEngineTests
{
    /// <summary>1 August 2026 is a Saturday, so the range opens mid-week on purpose.</summary>
    private static readonly DateOnly RangeStart = new(2026, 8, 1);

    private static readonly DateOnly RangeEnd = new(2026, 9, 30);

    private const double RowHeight = 64;
    private const double MonthHeight = 40;
    private const double WeekHeight = 24;
    private const double DayGap = 0;

    private static AgendaPageLayout Layout(params ISchedulerAppointment[] appointments)
        => Layout(RangeStart, RangeEnd, appointments);

    private static AgendaPageLayout Layout(DateOnly from, DateOnly to, params ISchedulerAppointment[] appointments)
        => AgendaLayoutEngine.Layout(
            appointments, from, to, DayOfWeek.Monday, RowHeight, MonthHeight, WeekHeight, DayGap, TimeZoneInfo.Local);

    private static AgendaPageLayout GappedLayout(double dayGap, params ISchedulerAppointment[] appointments)
        => AgendaLayoutEngine.Layout(
            appointments, RangeStart, RangeEnd, DayOfWeek.Monday, RowHeight, MonthHeight, WeekHeight, dayGap, TimeZoneInfo.Local);

    private static DateTime Day(int month, int day) => new(2026, month, day);

    private static IEnumerable<AgendaRow> Of(AgendaPageLayout layout, AgendaRowKind kind)
        => layout.Rows.Where(row => row.Kind == kind);

    [Test]
    public void Every_month_in_the_range_gets_a_heading_even_with_nothing_in_it()
    {
        // Two months, one appointment, and it is in the second — so August's heading is there on its
        // own merit and September's is there despite holding everything.
        var layout = Layout(TestAppointment.At(Day(9, 3), "10:00", 1));

        Assert.That(
            Of(layout, AgendaRowKind.MonthSection).Select(row => row.Date),
            Is.EqualTo(new[] { new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1) }).AsCollection);
    }

    [Test]
    public void A_week_straddling_the_range_edge_gets_no_heading()
    {
        // 1 August 2026 is a Saturday, so the first week (Mon 27 Jul – Sun 2 Aug) straddles the range
        // start and must not be announced — its Monday and Tuesday are not loaded. 3 August starts a
        // whole week inside the range and is.
        var layout = Layout(
            TestAppointment.At(Day(8, 1), "10:00", 1),
            TestAppointment.At(Day(8, 3), "10:00", 1));

        var weekDates = Of(layout, AgendaRowKind.WeekSection).Select(row => row.Date).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(weekDates, Does.Not.Contain(new DateOnly(2026, 7, 27)));
            Assert.That(weekDates, Does.Contain(new DateOnly(2026, 8, 3)));
        });
    }

    [Test]
    public void Section_rows_carry_the_number_of_appointments_they_group()
    {
        var layout = Layout(
            TestAppointment.At(Day(8, 3), "10:00", 1),
            TestAppointment.At(Day(8, 4), "10:00", 1),
            TestAppointment.At(Day(9, 2), "10:00", 1));

        Assert.Multiple(() =>
        {
            Assert.That(
                Of(layout, AgendaRowKind.MonthSection).Select(row => row.DayCount),
                Is.EqualTo(new[] { 2, 1 }).AsCollection);
            Assert.That(
                Of(layout, AgendaRowKind.WeekSection).Select(row => row.DayCount),
                Is.EqualTo(new[] { 2, 1 }).AsCollection);
        });
    }

    [Test]
    public void A_week_gets_a_heading_only_when_something_is_in_it()
    {
        var layout = Layout(
            TestAppointment.At(Day(8, 3), "10:00", 1),
            TestAppointment.At(Day(8, 20), "10:00", 1));

        // Weeks of 3 Aug and 17 Aug. The three empty weeks between them contribute nothing.
        Assert.That(
            Of(layout, AgendaRowKind.WeekSection).Select(row => row.Date),
            Is.EqualTo(new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 17) }).AsCollection);
    }

    [Test]
    public void A_week_straddling_a_month_boundary_is_announced_once()
    {
        // 31 August is a Monday, so its week runs into September. The month heading falls inside that
        // week, and must not make the week look like a new one.
        var layout = Layout(
            TestAppointment.At(Day(8, 31), "10:00", 1),
            TestAppointment.At(Day(9, 2), "10:00", 1));

        Assert.Multiple(() =>
        {
            Assert.That(Of(layout, AgendaRowKind.WeekSection).Count(), Is.EqualTo(1));
            Assert.That(Of(layout, AgendaRowKind.WeekSection).Single().Date, Is.EqualTo(new DateOnly(2026, 8, 31)));
            Assert.That(Of(layout, AgendaRowKind.MonthSection).Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public void Days_with_nothing_in_them_take_no_room_at_all()
    {
        var sparse = Layout(TestAppointment.At(Day(8, 15), "10:00", 1));

        // Two month headings, one week heading, one row — and nothing for the other 60 days.
        Assert.That(sparse.Rows, Has.Count.EqualTo(4));
        Assert.That(sparse.ContentHeight, Is.EqualTo(MonthHeight * 2 + WeekHeight + RowHeight));
    }

    [Test]
    public void The_first_row_of_a_day_carries_the_marker_and_the_days_count()
    {
        var layout = Layout(
            TestAppointment.At(Day(8, 10), "09:00", 1),
            TestAppointment.At(Day(8, 10), "11:00", 1),
            TestAppointment.At(Day(8, 11), "09:00", 1));

        var rows = Of(layout, AgendaRowKind.Appointment).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.StartsDay), Is.EqualTo(new[] { true, false, true }).AsCollection);
            Assert.That(rows.Select(row => row.DayCount), Is.EqualTo(new[] { 2, 2, 1 }).AsCollection);
        });
    }

    [Test]
    public void Rows_are_stacked_without_gaps_and_the_total_is_the_last_bottom()
    {
        var layout = Layout(
            TestAppointment.At(Day(8, 10), "09:00", 1),
            TestAppointment.At(Day(8, 10), "11:00", 1));

        var tops = layout.Rows.Select(row => row.Top).ToArray();

        Assert.Multiple(() =>
        {
            // August, week, row, row, then September's heading — which is there because months are
            // never skipped, not because September holds anything.
            Assert.That(tops, Is.EqualTo(new[]
            {
                0,
                MonthHeight,
                MonthHeight + WeekHeight,
                MonthHeight + WeekHeight + RowHeight,
                MonthHeight + WeekHeight + RowHeight * 2
            }).AsCollection);
            Assert.That(layout.ContentHeight, Is.EqualTo(layout.Rows[^1].Bottom));
        });
    }

    [Test]
    public void A_day_is_ordered_by_start_then_longest_first()
    {
        var late = TestAppointment.At(Day(8, 10), "14:00", 1, "late");
        var longOne = TestAppointment.At(Day(8, 10), "09:00", 3, "long");
        var shortOne = TestAppointment.At(Day(8, 10), "09:00", 1, "short");

        var layout = Layout(late, shortOne, longOne);

        Assert.That(
            Of(layout, AgendaRowKind.Appointment).Select(row => row.Appointment!.Subject),
            Is.EqualTo(new[] { "long", "short", "late" }).AsCollection);
    }

    [Test]
    public void Appointments_outside_the_range_are_dropped()
    {
        var layout = Layout(
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12),
            TestAppointment.At(Day(8, 9), "10:00", 1),
            TestAppointment.At(Day(8, 11), "10:00", 1),
            TestAppointment.At(Day(8, 13), "10:00", 1));

        Assert.That(Of(layout, AgendaRowKind.Appointment).Single().Date, Is.EqualTo(new DateOnly(2026, 8, 11)));
    }

    [Test]
    public void Reflow_moves_everything_below_a_corrected_row_and_nothing_above_it()
    {
        var layout = Layout(
            TestAppointment.At(Day(8, 10), "09:00", 1),
            TestAppointment.At(Day(8, 10), "11:00", 1));

        var rows = layout.Rows;
        var untouched = rows[1].Top;

        rows[2].Height += 20;
        var height = AgendaLayoutEngine.Reflow(rows, 2, DayGap);

        Assert.Multiple(() =>
        {
            Assert.That(rows[1].Top, Is.EqualTo(untouched), "rows above the correction do not move");
            Assert.That(rows[3].Top, Is.EqualTo(rows[2].Bottom));
            Assert.That(height, Is.EqualTo(layout.ContentHeight + 20));
        });
    }

    [Test]
    public void A_day_gap_separates_days_but_not_appointments_within_a_day()
    {
        const double gap = 16;
        var layout = GappedLayout(
            gap,
            TestAppointment.At(Day(8, 3), "09:00", 1),
            TestAppointment.At(Day(8, 3), "10:00", 1),
            TestAppointment.At(Day(8, 4), "09:00", 1));

        var rows = Of(layout, AgendaRowKind.Appointment).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rows[1].Top, Is.EqualTo(rows[0].Bottom), "same-day rows stay contiguous");
            Assert.That(rows[2].Top, Is.EqualTo(rows[1].Bottom + gap), "the next day is owed the gap");
        });
    }

    [Test]
    public void A_day_gap_survives_a_reflow()
    {
        const double gap = 16;
        var layout = GappedLayout(
            gap,
            TestAppointment.At(Day(8, 3), "09:00", 1),
            TestAppointment.At(Day(8, 4), "09:00", 1),
            TestAppointment.At(Day(8, 5), "09:00", 1));

        var rows = layout.Rows;
        var appointments = Of(layout, AgendaRowKind.Appointment).ToArray();
        var taller = appointments[1];
        var heightBefore = layout.ContentHeight;

        taller.Height += 10;
        var height = AgendaLayoutEngine.Reflow(rows, taller.Index, gap);

        Assert.Multiple(() =>
        {
            Assert.That(appointments[2].Top, Is.EqualTo(taller.Bottom + gap), "the gap follows the corrected row");
            Assert.That(height, Is.EqualTo(heightBefore + 10));
        });
    }

    [Test]
    public void IndexAt_finds_the_row_covering_a_position()
    {
        var layout = Layout(
            TestAppointment.At(Day(8, 10), "09:00", 1),
            TestAppointment.At(Day(8, 10), "11:00", 1));

        var rows = layout.Rows;

        Assert.Multiple(() =>
        {
            Assert.That(AgendaLayoutEngine.IndexAt(rows, 0), Is.EqualTo(0));
            Assert.That(AgendaLayoutEngine.IndexAt(rows, rows[2].Top + 1), Is.EqualTo(2));
            // A position exactly on a boundary belongs to the row starting there, not the one ending.
            Assert.That(AgendaLayoutEngine.IndexAt(rows, rows[2].Bottom), Is.EqualTo(3));
            Assert.That(AgendaLayoutEngine.IndexAt(rows, layout.ContentHeight + 100), Is.EqualTo(rows.Count));
        });
    }
}
