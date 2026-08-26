using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class MonthLayoutEngineTests
{
    /// <summary>Top-left cell of the August 2026 grid on a Monday week.</summary>
    private static readonly DateOnly GridStart = new(2026, 7, 27);

    private const int LinesPerCell = 5;

    private static MonthPageLayout Layout(params ISchedulerAppointment[] appointments)
        => MonthLayoutEngine.Layout(appointments, GridStart, LinesPerCell);

    private static DateTime Cell(int index) => GridStart.AddDays(index).ToDateTime(TimeOnly.MinValue);

    [Test]
    public void Appointment_is_placed_on_the_cell_for_its_day()
    {
        var result = Layout(TestAppointment.At(Cell(5), "10:00", 1));

        Assert.That(result.Placements, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Placements[0].CellIndex, Is.EqualTo(5));
            Assert.That(result.Placements[0].Line, Is.EqualTo(0));
        });
    }

    [Test]
    public void Days_from_the_neighbouring_months_hold_appointments_like_any_other()
    {
        var result = Layout(
            TestAppointment.At(Cell(0), "10:00", 1, "july"),
            TestAppointment.At(Cell(41), "10:00", 1, "september"));

        Assert.That(result.Placements.Select(p => p.CellIndex), Is.EqualTo(new[] { 0, 41 }));
    }

    [Test]
    public void Appointments_outside_the_six_rows_are_dropped()
    {
        var result = Layout(
            TestAppointment.At(GridStart.AddDays(-1).ToDateTime(TimeOnly.MinValue), "10:00", 1),
            TestAppointment.At(GridStart.AddDays(42).ToDateTime(TimeOnly.MinValue), "10:00", 1));

        Assert.That(result.Placements, Is.Empty);
    }

    [Test]
    public void A_day_stacks_its_appointments_in_order_of_starting()
    {
        var result = Layout(
            TestAppointment.At(Cell(5), "14:00", 1, "afternoon"),
            TestAppointment.At(Cell(5), "09:00", 1, "morning"));

        Assert.That(
            result.Placements.OrderBy(p => p.Line).Select(p => p.Appointment.Subject),
            Is.EqualTo(new[] { "morning", "afternoon" }));
    }

    [Test]
    public void Overlapping_appointments_each_take_a_whole_line()
    {
        // Nothing shares a line the way colliding appointments share a column on the timeline:
        // a month cell has no time axis to collide on.
        var result = Layout(
            TestAppointment.At(Cell(5), "09:00", 2, "first"),
            TestAppointment.At(Cell(5), "10:00", 2, "second"));

        Assert.That(result.Placements.Select(p => p.Line), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Appointments_outside_the_timeline_hours_are_still_shown()
    {
        // The timeline clips to StartHour..EndHour; a month has no such window.
        var result = Layout(TestAppointment.At(Cell(5), "06:00", 1));

        Assert.That(result.Placements, Has.Count.EqualTo(1));
    }

    [Test]
    public void A_day_that_fits_exactly_shows_everything_and_overflows_nothing()
    {
        var result = Layout([.. Enumerable.Range(0, LinesPerCell)
            .Select(i => TestAppointment.At(Cell(5), $"{9 + i:00}:00", 1))]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Placements, Has.Count.EqualTo(LinesPerCell));
            Assert.That(result.OverflowByCell[5], Is.Zero);
        });
    }

    [Test]
    public void A_full_day_gives_its_last_line_to_the_marker()
    {
        // Seven into five lines: four are shown and the fifth line reads "+3 more".
        var result = Layout([.. Enumerable.Range(0, 7)
            .Select(i => TestAppointment.At(Cell(5), $"{9 + i:00}:00", 1))]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Placements, Has.Count.EqualTo(4));
            Assert.That(result.Placements.Select(p => p.Line), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(result.OverflowByCell[5], Is.EqualTo(3));
        });
    }

    [Test]
    public void The_earliest_appointments_are_the_ones_kept_when_a_day_overflows()
    {
        var result = MonthLayoutEngine.Layout(
            [
                TestAppointment.At(Cell(5), "17:00", 1, "late"),
                TestAppointment.At(Cell(5), "09:00", 1, "early"),
                TestAppointment.At(Cell(5), "13:00", 1, "middle")
            ],
            GridStart,
            linesPerCell: 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Placements.Select(p => p.Appointment.Subject), Is.EqualTo(new[] { "early" }));
            Assert.That(result.OverflowByCell[5], Is.EqualTo(2));
        });
    }

    [Test]
    public void A_cell_with_no_room_shows_nothing_and_overflows_everything()
    {
        var result = MonthLayoutEngine.Layout(
            [TestAppointment.At(Cell(5), "09:00", 1), TestAppointment.At(Cell(5), "11:00", 1)],
            GridStart,
            linesPerCell: 0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Placements, Is.Empty);
            Assert.That(result.OverflowByCell[5], Is.EqualTo(2));
        });
    }

    [Test]
    public void Overflow_is_counted_per_day_rather_than_per_page()
    {
        var busy = Enumerable.Range(0, 7).Select(i => TestAppointment.At(Cell(5), $"{9 + i:00}:00", 1));
        var quiet = TestAppointment.At(Cell(6), "09:00", 1);

        var result = Layout([.. busy, quiet]);

        Assert.Multiple(() =>
        {
            Assert.That(result.OverflowByCell[5], Is.EqualTo(3));
            Assert.That(result.OverflowByCell[6], Is.Zero);
        });
    }

    [Test]
    public void Appointments_starting_together_keep_the_order_the_host_supplied()
    {
        // PopulateSlot reuses views positionally, so an unchanged reload has to lay out identically.
        var first = TestAppointment.At(Cell(5), "09:00", 1, "first");
        var second = TestAppointment.At(Cell(5), "09:00", 1, "second");

        var forwards = Layout(first, second).Placements.Select(p => p.Appointment.Subject);

        Assert.That(forwards, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void Every_cell_has_an_overflow_entry_even_when_the_page_is_empty()
    {
        var result = Layout();

        Assert.That(result.OverflowByCell, Has.Count.EqualTo(MonthGeometry.CellCount));
        Assert.That(result.OverflowByCell, Is.All.Zero);
    }
}
