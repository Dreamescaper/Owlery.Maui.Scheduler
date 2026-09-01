using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class AgendaSurfaceTests
{
    private const double RowHeight = 50;
    private const double MonthHeight = 40;
    private const double WeekHeight = 20;

    private static readonly DateOnly Page = new(2026, 8, 1);

    private static AgendaGeometry Geometry() => new()
    {
        ViewportWidth = 400,
        ViewportHeight = 200,
        FirstDayOfWeek = DayOfWeek.Monday,
        DayGutterWidth = 50,
        EstimatedRowHeight = RowHeight,
        MonthSectionHeight = MonthHeight,
        WeekSectionHeight = WeekHeight,
        OverscanFactor = 0,
        LeadDays = 0,
        TrailDays = 0
    };

    private static DateTime Day(int day) => new(2026, 8, day);

    /// <summary>Appointments on consecutive days, one each, from 3 August (a Monday).</summary>
    private static ISchedulerAppointment[] Daily(int count) =>
        [.. Enumerable.Range(0, count).Select(i => TestAppointment.At(Day(3 + i), "10:00", 1, $"a{i}"))];

    [Test]
    public void A_page_is_a_month_but_the_range_reaches_past_it()
    {
        var geometry = Geometry();
        geometry.LeadDays = 3;
        geometry.TrailDays = 2;
        var surface = new AgendaSurface(geometry);

        Assert.Multiple(() =>
        {
            Assert.That(surface.StartOfPage(new DateOnly(2026, 8, 17)), Is.EqualTo(Page));
            Assert.That(surface.NextPage(Page), Is.EqualTo(new DateOnly(2026, 9, 1)));
            Assert.That(surface.PreviousPage(Page), Is.EqualTo(new DateOnly(2026, 7, 1)));

            // Three days before August and two past the end of it.
            Assert.That(surface.DatesOn(Page)[0], Is.EqualTo(new DateOnly(2026, 7, 29)));
            Assert.That(surface.DatesOn(Page)[^1], Is.EqualTo(new DateOnly(2026, 9, 2)));
            Assert.That(surface.PageContains(Page, new DateOnly(2026, 7, 29)), Is.True);
            Assert.That(surface.PageContains(Page, new DateOnly(2026, 7, 28)), Is.False);
        });
    }

    [Test]
    public void Only_the_rows_near_the_scroll_position_are_returned()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        // 20 days of one appointment each: a table far taller than the 200pt viewport.
        var placements = surface.Layout(Daily(20), Page);

        Assert.That(placements, Has.Count.LessThan(20), "the whole range must not be realized");
        Assert.That(placements, Is.Not.Empty);

        // Everything returned is inside the window it asked for.
        foreach (AgendaPlacement placement in placements.Cast<AgendaPlacement>())
        {
            Assert.That(placement.Row.Bottom, Is.GreaterThan(geometry.WindowTop));
            Assert.That(placement.Row.Top, Is.LessThan(geometry.WindowBottom));
        }
    }

    [Test]
    public void Scrolling_moves_the_window_without_rebuilding_the_table()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        var first = surface.Layout(Daily(20), Page).Cast<AgendaPlacement>().Select(p => p.Row).ToArray();
        var rowsBefore = surface.Rows;

        geometry.VisibleTop = 400;
        var second = surface.Slice().Cast<AgendaPlacement>().Select(p => p.Row).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(surface.Rows, Is.SameAs(rowsBefore), "the table is sliced, not rebuilt");
            Assert.That(second, Is.Not.EqualTo(first).AsCollection);
            Assert.That(second[0].Top, Is.GreaterThanOrEqualTo(400 - RowHeight));
        });
    }

    [Test]
    public void The_day_marker_sits_in_the_gutter_beside_the_days_first_row()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        surface.Layout([
            TestAppointment.At(Day(3), "09:00", 1),
            TestAppointment.At(Day(3), "11:00", 1)
        ], Page);

        var markers = surface.SectionsFor(Page)
            .Where(s => s.Section.Kind is SchedulerAgendaSectionKind.Day)
            .ToArray();

        var firstRow = surface.Rows.First(r => r.Kind is AgendaRowKind.Appointment);

        Assert.Multiple(() =>
        {
            Assert.That(markers, Has.Length.EqualTo(1), "one marker for the day, not one per row");
            Assert.That(markers[0].Bounds.X, Is.EqualTo(0));
            Assert.That(markers[0].Bounds.Width, Is.EqualTo(geometry.DayGutterWidth));
            Assert.That(markers[0].Bounds.Y, Is.EqualTo(firstRow.Top));
            Assert.That(markers[0].Section.AppointmentCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void A_month_heading_reaches_the_edges_and_a_row_does_not()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        var placements = surface.Layout([TestAppointment.At(Day(3), "09:00", 1)], Page);

        var month = surface.SectionsFor(Page).First(s => s.Section.Kind is SchedulerAgendaSectionKind.Month);
        var row = surface.BoundsFor(placements[0]);

        Assert.Multiple(() =>
        {
            Assert.That(month.Bounds.X, Is.EqualTo(0));
            Assert.That(month.Bounds.Width, Is.EqualTo(geometry.ViewportWidth));
            Assert.That(row.X, Is.EqualTo(geometry.DayGutterWidth + AgendaGeometry.RowInset));
        });
    }

    [Test]
    public void A_measured_row_moves_what_is_below_it_and_reports_nothing_when_it_is_on_screen()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        surface.Layout(Daily(10), Page);

        var rows = surface.Rows;
        var target = rows.First(r => r.Kind is AgendaRowKind.Appointment);
        var below = rows[target.Index + 1];
        var heightBefore = geometry.ContentHeight;

        var shift = surface.ApplyMeasuredHeights([(target, RowHeight + 30)]);

        Assert.Multiple(() =>
        {
            Assert.That(shift, Is.Zero, "a row the reader can see needs no compensation");
            Assert.That(target.Measured, Is.True);
            Assert.That(below.Top, Is.EqualTo(target.Bottom));
            Assert.That(geometry.ContentHeight, Is.EqualTo(heightBefore + 30));
        });
    }

    [Test]
    public void A_row_measured_above_the_fold_reports_the_shift_it_caused()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        surface.Layout(Daily(20), Page);

        // Scroll well down, then correct a row that is now entirely above the viewport.
        geometry.VisibleTop = 600;
        var above = surface.Rows.First(r => r.Kind is AgendaRowKind.Appointment);

        var shift = surface.ApplyMeasuredHeights([(above, RowHeight + 25)]);

        Assert.That(shift, Is.EqualTo(25), "content above the reader grew, so the offset must absorb it");
    }

    [Test]
    public void An_empty_range_is_still_a_screenful_tall()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        surface.Layout([], Page);

        // Month headings alone are shorter than the viewport; collapsing would leave nothing to scroll.
        Assert.That(geometry.ContentHeight, Is.EqualTo(geometry.ViewportHeight));
    }

    [Test]
    public void A_touch_resolves_to_the_whole_day_it_lands_on()
    {
        var geometry = Geometry();
        var surface = new AgendaSurface(geometry);

        surface.Layout(Daily(5), Page);

        var row = surface.Rows.First(r => r.Kind is AgendaRowKind.Appointment);
        var slot = surface.SlotAt(new Point(200, row.Top + 1), []);

        Assert.Multiple(() =>
        {
            Assert.That(slot!.Value.Start, Is.EqualTo(row.Date.ToDateTime(TimeOnly.MinValue)));
            Assert.That(slot!.Value.Duration, Is.EqualTo(TimeSpan.FromDays(1)));
            Assert.That(surface.BoundsFor(slot.Value, Page), Is.EqualTo(Rect.Zero), "nothing is drawn for it");
        });
    }
}
