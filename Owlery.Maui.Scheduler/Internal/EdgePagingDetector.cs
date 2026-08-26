namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Decides whether a dragged appointment is being held against an edge, and which way that points.
/// </summary>
/// <remarks>
/// Separated from the control because the zone arithmetic is the kind that drifts unnoticed: the
/// bands are measured from the visible page rather than the surface, and they have to stay narrow
/// enough that dragging onto the first or last column is still possible.
/// </remarks>
internal static class EdgePagingDetector
{
    /// <summary>How close to an edge counts as holding against it.</summary>
    public const double ZoneDp = 32;

    /// <summary>-1 towards the previous period, +1 towards the next, 0 for anywhere else.</summary>
    public static int DirectionFor(SchedulerGeometry geometry, Point point)
    {
        if (geometry.ViewportWidth <= 0)
            return 0;

        // The pager rests on the centre page for the whole of a drag, so the visible page begins one
        // viewport into the surface.
        var xInPage = point.X - geometry.ViewportWidth;

        if (xInPage <= ZoneDp)
            return -1;

        return xInPage >= geometry.ViewportWidth - ZoneDp ? 1 : 0;
    }
}
