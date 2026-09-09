namespace Owlery.Maui.Scheduler.Internal;

/// <summary>Whether a scroll offset can be applied yet, given what the content currently measures.</summary>
/// <remarks>
/// A platform scroll view silently clamps an offset its content is too narrow to reach, and the
/// clamped value is not recoverable afterwards — so the offset has to be held back until the content
/// has caught up rather than written and corrected. Arithmetic rather than view manipulation, so it
/// lives here and is testable without a platform.
/// </remarks>
internal static class PagingOffset
{
    /// <summary>Whether content <paramref name="contentWidth"/> wide can sit at <paramref name="offsetX"/>.</summary>
    public static bool Fits(double offsetX, double viewportWidth, double contentWidth) =>
        viewportWidth > 0 && contentWidth >= offsetX + viewportWidth;
}
