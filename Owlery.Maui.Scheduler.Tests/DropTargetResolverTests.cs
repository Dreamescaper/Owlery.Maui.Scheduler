using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class DropTargetResolverTests
{
    private static readonly DateOnly Monday = new(2026, 8, 24);
    private const double PageWidth = 350;
    private const double PageOffset = PageWidth;

    private static SchedulerGeometry Geometry(int visibleDays = 7) => new()
    {
        ViewportWidth = PageWidth,
        VisibleDays = visibleDays,
        HourHeight = 50,
        StartHour = 8,
        EndHour = 23
    };

    private static DropTarget Resolve(
        SchedulerGeometry geometry,
        double x,
        double y,
        double appointmentHeight = 50,
        int snapMinutes = 15) =>
        DropTargetResolver.Resolve(
            geometry, Monday, new Point(x, y), Point.Zero, PageOffset, appointmentHeight, snapMinutes);

    [Test]
    public void Resolves_the_column_the_appointment_would_sit_in()
    {
        var geometry = Geometry();
        var target = Resolve(geometry, PageOffset + (2 * geometry.DayWidth), 0);

        Assert.Multiple(() =>
        {
            Assert.That(target.DayIndex, Is.EqualTo(2));
            Assert.That(target.Start.Date, Is.EqualTo(Monday.AddDays(2).ToDateTime(TimeOnly.MinValue)));
        });
    }

    [Test]
    public void Rounds_to_the_nearest_boundary_rather_than_the_containing_one()
    {
        // Cell selection rounds down; a drop rounds to the nearest, so the appointment does not trail
        // behind the finger. 10:53 is nearer 11:00 than 10:45.
        var geometry = Geometry();
        var target = Resolve(geometry, PageOffset, geometry.YFromMinutes((10 * 60) + 53));

        Assert.That(target.Start.TimeOfDay, Is.EqualTo(TimeSpan.FromHours(11)));
    }

    [Test]
    public void Never_starts_before_the_window_opens()
    {
        var geometry = Geometry();
        var target = Resolve(geometry, PageOffset, -500);

        Assert.That(target.SnappedMinutes, Is.EqualTo(geometry.WindowStartMinutes));
    }

    [Test]
    public void Keeps_the_whole_appointment_inside_the_window()
    {
        // A two-hour appointment cannot start at 22:00 in a window that closes at 23:00.
        var geometry = Geometry();
        var target = Resolve(geometry, PageOffset, 100000, appointmentHeight: 100);

        Assert.That(target.Start.TimeOfDay, Is.EqualTo(TimeSpan.FromHours(21)));
    }

    [Test]
    public void The_column_is_bounded_by_the_page_not_by_a_week()
    {
        var geometry = Geometry(visibleDays: 3);
        var target = Resolve(geometry, PageOffset + (10 * geometry.DayWidth), 0);

        Assert.That(target.DayIndex, Is.EqualTo(2));
    }

    [Test]
    public void The_grabbed_point_stays_under_the_finger()
    {
        // Grabbing an appointment half a column in should not shift it half a column when dropped.
        var geometry = Geometry();
        var grab = new Point(geometry.DayWidth / 2, 0);

        var target = DropTargetResolver.Resolve(
            geometry,
            Monday,
            new Point(PageOffset + (3 * geometry.DayWidth) + (geometry.DayWidth / 2), 0),
            grab,
            PageOffset,
            50,
            15);

        Assert.That(target.DayIndex, Is.EqualTo(3));
    }

    [TestCase(0, -1)]
    [TestCase(10, -1)]
    [TestCase(PageWidth / 2, 0)]
    [TestCase(PageWidth - 10, 1)]
    [TestCase(PageWidth, 1)]
    public void Edge_zones_are_measured_from_the_visible_page(double xInPage, int expected)
    {
        var geometry = Geometry();

        Assert.That(
            EdgePagingDetector.DirectionFor(geometry, new Point(PageOffset + xInPage, 0)),
            Is.EqualTo(expected));
    }

    [Test]
    public void No_edge_is_detected_before_the_control_has_a_width()
    {
        Assert.That(EdgePagingDetector.DirectionFor(new SchedulerGeometry(), new Point(0, 0)), Is.EqualTo(0));
    }
}
