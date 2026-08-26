using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class MonthGeometryTests
{
    private static MonthGeometry Geometry(DayOfWeek firstDay = DayOfWeek.Monday) => new()
    {
        ViewportWidth = 350,
        ViewportHeight = 660,
        FirstDayOfWeek = firstDay
    };

    [Test]
    public void Grid_opens_on_the_first_day_of_the_week_before_the_first_of_the_month()
    {
        // 1 August 2026 is a Saturday, so a Monday-start grid opens on Monday 27 July.
        var start = Geometry().GridStart(new DateOnly(2026, 8, 1));

        Assert.That(start, Is.EqualTo(new DateOnly(2026, 7, 27)));
    }

    [Test]
    public void Grid_start_honours_a_Sunday_week()
    {
        var start = Geometry(DayOfWeek.Sunday).GridStart(new DateOnly(2026, 8, 1));

        Assert.That(start, Is.EqualTo(new DateOnly(2026, 7, 26)));
    }

    [Test]
    public void Grid_opens_on_the_first_itself_when_the_month_starts_the_week()
    {
        // 1 June 2026 is a Monday.
        var start = Geometry().GridStart(new DateOnly(2026, 6, 1));

        Assert.That(start, Is.EqualTo(new DateOnly(2026, 6, 1)));
    }

    [Test]
    public void Any_day_of_the_month_identifies_the_same_grid()
    {
        var geometry = Geometry();

        Assert.That(
            geometry.GridStart(new DateOnly(2026, 8, 19)),
            Is.EqualTo(geometry.GridStart(new DateOnly(2026, 8, 1))));
    }

    [Test]
    public void Cell_index_covers_the_leading_and_trailing_days_of_neighbouring_months()
    {
        var geometry = Geometry();
        var august = new DateOnly(2026, 8, 1);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.CellIndexOf(august, new DateOnly(2026, 7, 27)), Is.EqualTo(0));
            Assert.That(geometry.CellIndexOf(august, new DateOnly(2026, 8, 1)), Is.EqualTo(5));
            Assert.That(geometry.CellIndexOf(august, new DateOnly(2026, 9, 6)), Is.EqualTo(41));
        });
    }

    [Test]
    public void Dates_outside_the_six_rows_are_not_on_the_page()
    {
        var geometry = Geometry();
        var august = new DateOnly(2026, 8, 1);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.CellIndexOf(august, new DateOnly(2026, 7, 26)), Is.EqualTo(-1));
            Assert.That(geometry.CellIndexOf(august, new DateOnly(2026, 9, 7)), Is.EqualTo(-1));
        });
    }

    [Test]
    public void A_short_month_starting_the_week_still_renders_six_rows()
    {
        // February 2027 is 28 days and begins on a Monday, so the last two rows are entirely March.
        var geometry = Geometry();
        var february = new DateOnly(2027, 2, 1);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.GridStart(february), Is.EqualTo(february));
            Assert.That(geometry.CellIndexOf(february, new DateOnly(2027, 3, 14)), Is.EqualTo(41));
        });
    }

    [Test]
    public void Cells_tile_the_viewport_exactly()
    {
        var geometry = Geometry();

        var first = geometry.CellBounds(0);
        var last = geometry.CellBounds(MonthGeometry.CellCount - 1);

        Assert.Multiple(() =>
        {
            Assert.That(first.X, Is.EqualTo(0));
            Assert.That(first.Y, Is.EqualTo(0));
            Assert.That(last.Right, Is.EqualTo(geometry.ViewportWidth).Within(0.001));
            Assert.That(last.Bottom, Is.EqualTo(geometry.ViewportHeight).Within(0.001));
        });
    }

    [Test]
    public void Cell_bounds_walk_left_to_right_then_down()
    {
        var geometry = Geometry();

        Assert.Multiple(() =>
        {
            Assert.That(geometry.CellBounds(1).X, Is.EqualTo(geometry.CellWidth).Within(0.001));
            Assert.That(geometry.CellBounds(1).Y, Is.EqualTo(0));
            Assert.That(geometry.CellBounds(7).X, Is.EqualTo(0));
            Assert.That(geometry.CellBounds(7).Y, Is.EqualTo(geometry.CellHeight).Within(0.001));
        });
    }

    [Test]
    public void Line_capacity_is_what_fits_beneath_the_day_number()
    {
        // 660 / 6 = 110 a cell, less 18 for the day number, over 18 a chip.
        Assert.That(Geometry().LinesPerCell, Is.EqualTo(5));
    }

    [Test]
    public void A_cell_too_short_for_a_chip_has_no_lines()
    {
        var geometry = new MonthGeometry { ViewportWidth = 350, ViewportHeight = 120 };

        Assert.That(geometry.LinesPerCell, Is.EqualTo(0));
    }

    [Test]
    public void A_month_page_is_one_viewport_wide_and_does_not_scroll()
    {
        var geometry = Geometry();

        Assert.Multiple(() =>
        {
            Assert.That(geometry.PageSpan, Is.EqualTo(geometry.ViewportWidth));
            Assert.That(geometry.ContentHeight, Is.EqualTo(geometry.ViewportHeight));
        });
    }
}
