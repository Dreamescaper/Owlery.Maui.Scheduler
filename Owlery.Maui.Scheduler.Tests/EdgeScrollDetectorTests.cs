using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class EdgeScrollDetectorTests
{
    private const double Viewport = 600;

    [TestCase(0, -1)]
    [TestCase(20, -1)]
    [TestCase(48, -1)]
    [TestCase(300, 0)]
    [TestCase(Viewport - 48, 1)]
    [TestCase(Viewport, 1)]
    public void Zones_sit_at_the_top_and_bottom_of_what_is_on_screen(double screenY, int expected)
    {
        Assert.That(EdgeScrollDetector.DirectionFor(screenY, Viewport), Is.EqualTo(expected));
    }

    [Test]
    public void A_viewport_too_short_for_two_zones_has_none()
    {
        // The bands would otherwise meet in the middle and every position would be an edge.
        Assert.Multiple(() =>
        {
            Assert.That(EdgeScrollDetector.DirectionFor(10, 80), Is.EqualTo(0));
            Assert.That(EdgeScrollDetector.DirectionFor(70, 80), Is.EqualTo(0));
            Assert.That(EdgeScrollDetector.DirectionFor(0, 0), Is.EqualTo(0));
        });
    }
}
