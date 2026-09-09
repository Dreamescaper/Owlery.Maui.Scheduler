#if IOS || MACCATALYST
using CoreGraphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Owlery.Maui.Scheduler.Internal;
using UIKit;

namespace Owlery.Maui.Scheduler.Handlers;

/// <summary>
/// Hosts a <see cref="PagingScrollView"/> in a <c>UIScrollView</c>, which pages natively.
/// </summary>
/// <remarks>
/// Everything set up here used to be reached for through <c>Handler.PlatformView</c> from inside the
/// scheduler. It belongs to the scroller, not to a calendar, which is most of why this control was
/// separated out — see docs/design/pager.md.
/// </remarks>
internal partial class PagingScrollViewHandler : ViewHandler<PagingScrollView, MauiPagingScrollView>
{
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
    /// <para>
    /// This is iOS-only on purpose, and it is not an oversight that Android has no counterpart.
    /// Measuring the cross-platform content from a handler satisfies MAUI's measure cache, and on
    /// Android that starves the pass that actually matters: <c>MauiPagingContentViewGroup</c> measures
    /// and then <em>arranges</em> the same content, so with the measure already considered done the
    /// content was never laid out. Everything inside the pager — the drawing surface and every
    /// appointment in it — came out unsized, and the grid rendered as blank white while the hour
    /// gutter and day headers outside the pager still drew. Constraining the width differently does
    /// not avoid it; making the call at all does. Android needs nothing here because its platform view
    /// measures its child for itself.
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

    protected override MauiPagingScrollView CreatePlatformView() => new()
    {
        // The frame is exactly one page and the content exactly three, so page boundaries already
        // fall on 0 / W / 2W and the platform needs no snap offsets of its own.
        PagingEnabled = true,

        // There is no end of the calendar to bounce against — the pages are a ring buffer — so
        // rubber-banding would pull the outermost page away from the edge and reveal empty surface
        // behind it. It also keeps the offset inside 0..2W, which the ring-buffer arithmetic and the
        // hand-mirrored day headers both assume.
        Bounces = false,

        // A UIScrollView holds back touchesBegan while it decides whether a touch is a scroll. The
        // long press that picks an appointment up has to know the moment the finger lands.
        DelaysContentTouches = false,

        ShowsHorizontalScrollIndicator = false,
        ShowsVerticalScrollIndicator = false
    };

    protected override void ConnectHandler(MauiPagingScrollView platformView)
    {
        base.ConnectHandler(platformView);

        platformView.CrossPlatformLayout = VirtualView;

        platformView.Scrolled += OnScrolled;
        platformView.ScrollAnimationEnded += OnScrollAnimationEnded;
        platformView.DecelerationEnded += OnSettled;
        platformView.DraggingEnded += OnDraggingEnded;

        VirtualView.ScrollToRequested += OnScrollToRequested;
    }

    protected override void DisconnectHandler(MauiPagingScrollView platformView)
    {
        platformView.Scrolled -= OnScrolled;
        platformView.ScrollAnimationEnded -= OnScrollAnimationEnded;
        platformView.DecelerationEnded -= OnSettled;
        platformView.DraggingEnded -= OnDraggingEnded;

        VirtualView.ScrollToRequested -= OnScrollToRequested;

        platformView.CrossPlatformLayout = null;

        base.DisconnectHandler(platformView);
    }

    private void OnScrolled(object? sender, EventArgs e) =>
        VirtualView?.SetScrolledPosition(PlatformView.ContentOffset.X);

    /// <summary>Only an animated request ends this way; the caller of it is awaiting.</summary>
    private void OnScrollAnimationEnded(object? sender, EventArgs e) => VirtualView?.SendScrollFinished();

    private void OnSettled(object? sender, EventArgs e) => VirtualView?.SendPageSettled();

    /// <summary>A release with no momentum left never decelerates, so it settles here instead.</summary>
    private void OnDraggingEnded(object? sender, DraggingEventArgs e)
    {
        if (!e.Decelerate)
            VirtualView?.SendPageSettled();
    }

    private void OnScrollToRequested(object? sender, PagingScrollRequestedEventArgs e)
    {
        PlatformView.SetOffset(e.ScrollX, e.Animated);

        // setContentOffset:animated:NO has already moved it by the time this returns, so there is
        // nothing to wait for and no animation callback coming.
        if (!e.Animated)
        {
            VirtualView?.SetScrolledPosition(PlatformView.ContentOffset.X);
            VirtualView?.SendScrollFinished();
        }
    }

    public static void MapIsScrollEnabled(PagingScrollViewHandler handler, PagingScrollView view) =>
        handler.PlatformView.ScrollEnabled = view.IsScrollEnabled;

    /// <summary>Nothing to do: a paging UIScrollView pages by its own bounds, which is the page width.</summary>
    public static void MapPageWidth(PagingScrollViewHandler handler, PagingScrollView view)
    {
    }

    public static void MapContent(PagingScrollViewHandler handler, PagingScrollView view)
    {
        if (handler.MauiContext is null)
            return;

        foreach (var existing in handler.PlatformView.Subviews)
            existing.RemoveFromSuperview();

        if (view.Content is { } content)
            handler.PlatformView.AddSubview(content.ToPlatform(handler.MauiContext));

        handler.PlatformView.SetNeedsLayout();
    }
}

/// <summary>A <c>UIScrollView</c> that lays its content out through the cross-platform view.</summary>
internal class MauiPagingScrollView : UIScrollView
{
    private double? deferredOffsetX;

    public IContentView? CrossPlatformLayout { get; set; }

    /// <summary>
    /// Moves the offset, remembering it if the content is not yet wide enough to allow it.
    /// </summary>
    /// <remarks>
    /// The first recentre is asked for before the view has a frame. UIKit takes the offset without
    /// complaint at that point — reading it straight back reports exactly what was asked for — and
    /// then resets it to zero on the first real layout, so the calendar opened on the page before the
    /// one it should have. Detecting that by reading the offset back therefore does not work; what
    /// identifies it is the content not reaching the offset, so that is what is tested.
    /// <para>
    /// Having a frame is not enough on its own. Leaving an agenda re-pages the surface from one
    /// viewport to three, and the recentre onto the middle page is asked for while the platform view
    /// still measures the agenda's single page — a frame and a content size, both real, neither wide
    /// enough. The offset was written anyway and UIKit clamped it to the old content on the next
    /// layout, leaving the pager at rest between two pages with no request outstanding to put it
    /// right. Android has always tested its scroll range for this; iOS was the one only testing for
    /// zero.
    /// </para>
    /// </remarks>
    public void SetOffset(double x, bool animated)
    {
        if (!PagingOffset.Fits(x, Bounds.Width, ContentSize.Width))
        {
            deferredOffsetX = x;
            return;
        }

        SetContentOffset(new CGPoint(x, ContentOffset.Y), animated);
        deferredOffsetX = null;
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        if (CrossPlatformLayout is null)
            return;

        // Measured unconstrained across, because the content is deliberately wider than the frame —
        // that width is what there is to page through.
        var measured = CrossPlatformLayout.CrossPlatformMeasure(double.PositiveInfinity, Bounds.Height);

        var width = Math.Max(measured.Width, Bounds.Width);
        var height = Math.Max(measured.Height, Bounds.Height);

        CrossPlatformLayout.CrossPlatformArrange(new Rect(0, 0, width, height));

        ContentSize = new CGSize(width, height);

        if (deferredOffsetX is not { } pending || !PagingOffset.Fits(pending, Bounds.Width, width))
            return;

        ContentOffset = new CGPoint(pending, ContentOffset.Y);
        deferredOffsetX = null;
    }
}
#endif
