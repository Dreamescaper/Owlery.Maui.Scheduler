#if IOS || MACCATALYST || ANDROID
using Microsoft.Maui.Handlers;

namespace Owlery.Maui.Scheduler.Handlers;

internal partial class PagingScrollViewHandler
{
    public static IPropertyMapper<PagingScrollView, PagingScrollViewHandler> Mapper =
        new PropertyMapper<PagingScrollView, PagingScrollViewHandler>(ViewMapper)
        {
            [nameof(PagingScrollView.Content)] = MapContent,
            [nameof(PagingScrollView.IsScrollEnabled)] = MapIsScrollEnabled,
            [nameof(PagingScrollView.PageWidth)] = MapPageWidth
        };

    public PagingScrollViewHandler()
        : base(Mapper)
    {
    }

    public PagingScrollViewHandler(IPropertyMapper? mapper)
        : base(mapper ?? Mapper)
    {
    }

    /// <summary>
    /// Measures the pager by its content, which is what a <c>ContentView</c> normally does for itself.
    /// </summary>
    /// <remarks>
    /// Taking over the handler cost this. <c>ContentViewHandler</c> measures the cross-platform
    /// content; a bare <see cref="ViewHandler{TVirtualView,TPlatformView}"/> asks the platform view
    /// instead, and a scroll view answers with its current bounds — so the pager reported "as tall as
    /// I already am". It could still grow, because the hour gutter's own height pulls the row up and
    /// the pager fills it, but nothing ever asked it to shrink: a month replacing a timeline, or a
    /// smaller <c>HourHeight</c>, left the vertical scroll view scrolling over empty space above and
    /// below. Both children had already shrunk; only this had not.
    /// <para>
    /// The width is the constraint rather than what the content measured. The content is deliberately
    /// three pages wide and the pager is one page wide — that is the whole point of it — so reporting
    /// the content's width would make the pager ask for the full surface and stop being a viewport.
    /// </para>
    /// </remarks>
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        if (VirtualView is not IContentView content)
            return base.GetDesiredSize(widthConstraint, heightConstraint);

        var measured = content.CrossPlatformMeasure(widthConstraint, heightConstraint);
        var width = double.IsInfinity(widthConstraint) ? measured.Width : widthConstraint;

        return new Size(width, measured.Height);
    }
}
#endif
