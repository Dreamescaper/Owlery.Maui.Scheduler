namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Decides whether a dragged appointment is being held against the top or bottom of the timeline.
/// </summary>
/// <remarks>
/// The vertical counterpart of <see cref="EdgePagingDetector"/>, and measured differently: pages are
/// a fixed width so the horizontal zones can be taken from the surface, but how much of the day is on
/// screen depends on the viewport, so these are measured against it.
/// </remarks>
internal static class EdgeScrollDetector
{
    /// <summary>How close to an edge counts as holding against it.</summary>
    public const double ZoneDp = 48;

    /// <summary>-1 towards earlier hours, +1 towards later ones, 0 for anywhere else.</summary>
    /// <param name="screenY">Where the finger is within the visible timeline, not within the day.</param>
    /// <param name="viewportHeight">How much of the day is on screen.</param>
    public static int DirectionFor(double screenY, double viewportHeight)
    {
        // Too little of the day on screen for two zones to be meaningful: they would meet in the
        // middle and every position would count as an edge.
        if (viewportHeight <= ZoneDp * 2)
            return 0;

        if (screenY <= ZoneDp)
            return -1;

        return screenY >= viewportHeight - ZoneDp ? 1 : 0;
    }
}
