namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Which pages a day-count transition has to re-place on a given frame: the ones on screen, and the
/// ones that were left on screen last time and have to be moved off it.
/// </summary>
/// <remarks>
/// Re-placing a page gives every appointment view on it a new width, and the platform then lays each
/// one out again — measured on the iOS simulator at 40–50ms a frame for a hundred appointments a
/// month, almost all of it in the views. A page off screen is not seen, so it is left where it is
/// until the transition finishes and places everything at rest.
/// <para>
/// Which pages are off screen depends on the direction. Growing a day into a week keeps the
/// neighbouring weeks at least a full page away throughout, so after the first frame only the
/// centre page is placed. Shrinking a week onto a day saves nothing: the new pages are a day wide,
/// so the days either side sit beside it while it widens and leave the screen only on the last
/// frame. That direction is the cheap one anyway, since a day holds a seventh of a week's views.
/// Asking per frame rather than per direction is what keeps the second case correct, and a page
/// that leaves the screen is placed once more so it is not left behind at its last position.
/// </para>
/// </remarks>
internal sealed class TransitionPagePlacement
{
    private readonly bool[] leftOnScreen = new bool[PageGeometry.SlotCount];

    /// <summary>
    /// Whether a page intersects the viewport at the geometry as it stands, with the viewport
    /// beginning at the pager's offset.
    /// </summary>
    public static bool IsOnScreen(PageGeometry geometry, int slotIndex, double scrollX)
    {
        var left = (slotIndex * geometry.PageSpan) + geometry.AnimationOffsetX - scrollX;

        return left < geometry.ViewportWidth && left + geometry.PageSpan > 0;
    }

    /// <summary>
    /// Records that something other than the transition moved a page, which may have left it on
    /// screen at a position the transition did not choose.
    /// </summary>
    public void Moved(int slotIndex) => leftOnScreen[slotIndex] = true;

    /// <summary>Records every page as moved, so the next frame places all of them.</summary>
    public void MovedAll() => Array.Fill(leftOnScreen, true);

    /// <summary>Whether to place a page this frame, given whether it is on screen at this frame's geometry.</summary>
    /// <remarks>Answering yes records where the page is being left, which the next frame asks about.</remarks>
    public bool ShouldPlace(int slotIndex, bool onScreen)
    {
        if (!onScreen && !leftOnScreen[slotIndex])
            return false;

        leftOnScreen[slotIndex] = onScreen;
        return true;
    }
}
