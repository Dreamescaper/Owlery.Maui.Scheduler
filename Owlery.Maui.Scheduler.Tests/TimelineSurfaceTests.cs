using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class TimelineSurfaceTests
{
    private static readonly DateOnly Day = new(2026, 8, 24);
    private const int StartHour = 8;
    private const int EndHour = 18;
    private const double HourHeight = 50;

    private static IReadOnlyList<Rect> BoundsFor(params ISchedulerAppointment[] appointments)
    {
        var geometry = new SchedulerGeometry
        {
            ViewportWidth = 700,
            ViewportHeight = 500,
            VisibleDays = 7,
            HourHeight = HourHeight,
            StartHour = StartHour,
            EndHour = EndHour
        };

        var surface = new TimelineSurface(geometry, () => 30);
        var placements = AppointmentLayoutEngine.Layout(
            appointments, Day, 7, StartHour, EndHour, TimeZoneInfo.Local);

        return
        [
            .. placements
                .Cast<PositionedAppointment>()
                .OrderBy(placement => placement.StartMinutes)
                .Select(surface.BoundsFor)
        ];
    }

    [Test]
    public void The_hour_line_between_two_touching_appointments_is_not_covered()
    {
        var bounds = BoundsFor(
            TestAppointment.At(Day.ToDateTime(TimeOnly.MinValue), "09:00", 1, "first"),
            TestAppointment.At(Day.ToDateTime(TimeOnly.MinValue), "10:00", 1, "second"));

        var line = (10 - StartHour) * HourHeight;

        Assert.Multiple(() =>
        {
            Assert.That(
                bounds[0].Bottom,
                Is.EqualTo(line - TimelineSurface.AppointmentGap),
                "the earlier box pulls back above the line");
            Assert.That(
                bounds[1].Top,
                Is.EqualTo(line + TimelineSurface.AppointmentGap),
                "the later box starts below it");
        });
    }

    [Test]
    public void Every_appointment_is_inset_from_all_four_edges()
    {
        var bounds = BoundsFor(
            TestAppointment.At(Day.ToDateTime(TimeOnly.MinValue), "09:00", 1)).Single();

        var gap = TimelineSurface.AppointmentGap;
        var dayWidth = 700.0 / 7;

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Left, Is.EqualTo(gap));
            Assert.That(bounds.Right, Is.EqualTo(dayWidth - gap));
            Assert.That(bounds.Top, Is.EqualTo((9 - StartHour) * HourHeight + gap));
            Assert.That(bounds.Bottom, Is.EqualTo((10 - StartHour) * HourHeight - gap));
        });
    }
}
