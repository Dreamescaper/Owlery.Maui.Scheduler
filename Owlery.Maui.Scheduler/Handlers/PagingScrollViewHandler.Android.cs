#if ANDROID
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Owlery.Maui.Scheduler.Handlers;

/// <summary>
/// Hosts a <see cref="PagingScrollView"/> in a <c>HorizontalScrollView</c> that pages.
/// </summary>
/// <remarks>
/// Android has no paging of its own, so the snap is implemented here — see
/// <see cref="MauiPagingScrollView"/>. This is the reason the control could not simply subclass
/// MAUI's <c>ScrollView</c>: its Android platform view is a <c>MauiScrollView : NestedScrollView</c>
/// that grows an internal <c>MauiHorizontalScrollView</c> child when asked for a horizontal
/// orientation, so paging would have had to be imposed across two nested view classes, neither ours.
/// See DESIGN.md section 19.
/// </remarks>
internal partial class PagingScrollViewHandler : ViewHandler<PagingScrollView, MauiPagingScrollView>
{
    private MauiPagingContentViewGroup? contentHost;

    protected override MauiPagingScrollView CreatePlatformView()
    {
        var scrollView = new MauiPagingScrollView(Context)
        {
            // The pages are a ring buffer, so there is no end to come up against. The glow reports
            // something untrue and the stretch reveals empty surface behind the outermost page.
            OverScrollMode = OverScrollMode.Never,
            HorizontalScrollBarEnabled = false,
            FillViewport = true
        };

        // The content is three pages wide and its background is opaque, so anything of it that
        // escapes the viewport paints over whatever sits beside the pager — the hour gutter, in this
        // control's case. UIScrollView clips unconditionally; here it has to be asked for.
        scrollView.SetClipChildren(true);
        scrollView.SetClipToPadding(true);

        contentHost = new MauiPagingContentViewGroup(Context);
        contentHost.SetClipChildren(false);
        scrollView.AddView(contentHost, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.MatchParent));

        return scrollView;
    }

    protected override void ConnectHandler(MauiPagingScrollView platformView)
    {
        base.ConnectHandler(platformView);

        if (contentHost is not null)
            contentHost.CrossPlatformLayout = VirtualView;

        platformView.ScrollOffsetChanged += OnScrollOffsetChanged;
        platformView.PageSettled += OnPlatformPageSettled;

        VirtualView.ScrollToRequested += OnScrollToRequested;
    }

    protected override void DisconnectHandler(MauiPagingScrollView platformView)
    {
        platformView.ScrollOffsetChanged -= OnScrollOffsetChanged;
        platformView.PageSettled -= OnPlatformPageSettled;

        VirtualView.ScrollToRequested -= OnScrollToRequested;

        if (contentHost is not null)
            contentHost.CrossPlatformLayout = null;

        base.DisconnectHandler(platformView);
    }

    private double? cachedDensity;

    /// <summary>
    /// The display density, fetched once.
    /// </summary>
    /// <remarks>
    /// Read on every scroll frame, and each read used to cross into Java twice — for <c>Resources</c>
    /// and then <c>DisplayMetrics</c> — before dividing. Density cannot change without a
    /// configuration change, which builds a new handler.
    /// </remarks>
    private double Density => cachedDensity ??= Context?.Resources?.DisplayMetrics?.Density ?? 1;

    private void OnScrollOffsetChanged(object? sender, EventArgs e) =>
        VirtualView?.SetScrolledPosition(PlatformView.ScrollX / Density);

    /// <summary>
    /// The animation has stopped, whatever started it.
    /// </summary>
    /// <remarks>
    /// Android has no counterpart to <c>scrollViewDidEndScrollingAnimation</c>, so an animated
    /// request is completed here too. Without that, whoever awaited one waited for ever — which is
    /// how edge-paging mid-drag ended up never resolving a drop target again, since the flag saying
    /// "a slide is in flight" was only cleared once that await returned.
    /// </remarks>
    private void OnPlatformPageSettled(object? sender, EventArgs e)
    {
        VirtualView?.SendScrollFinished();
        VirtualView?.SendPageSettled();
    }

    private void OnScrollToRequested(object? sender, PagingScrollRequestedEventArgs e)
    {
        // ScrollTo is immediate — the offset has changed before this returns, which is what stops a
        // frame being drawn between rotating the pages and recentring on them.
        PlatformView.SetOffset((int)Math.Round(e.ScrollX * Density), e.Animated);

        if (e.Animated)
            return;

        VirtualView?.SetScrolledPosition(PlatformView.ScrollX / Density);
        VirtualView?.SendScrollFinished();
    }

    public static void MapIsScrollEnabled(PagingScrollViewHandler handler, PagingScrollView view) =>
        handler.PlatformView.IsScrollEnabled = view.IsScrollEnabled;

    public static void MapPageWidth(PagingScrollViewHandler handler, PagingScrollView view) =>
        handler.PlatformView.PageWidthPx = (int)Math.Round(view.PageWidth * handler.Density);

    public static void MapContent(PagingScrollViewHandler handler, PagingScrollView view)
    {
        if (handler.MauiContext is null || handler.contentHost is null)
            return;

        handler.contentHost.RemoveAllViews();

        if (view.Content is { } content)
            handler.contentHost.AddView(content.ToPlatform(handler.MauiContext));

        handler.contentHost.RequestLayout();
    }
}

/// <summary>Lays the content out through the cross-platform view, at whatever width it asks for.</summary>
internal class MauiPagingContentViewGroup(Context context) : ViewGroup(context)
{
    private double? cachedDensity;

    public IContentView? CrossPlatformLayout { get; set; }

    /// <summary>Fetched once; see the note on the handler's copy.</summary>
    private double Density => cachedDensity ??= Context?.Resources?.DisplayMetrics?.Density ?? 1;

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        if (CrossPlatformLayout is null)
        {
            SetMeasuredDimension(0, 0);
            return;
        }

        var height = MeasureSpec.GetSize(heightMeasureSpec) / Density;

        // Unconstrained across: the content is deliberately wider than the viewport, and that width
        // is what there is to page through.
        var measured = CrossPlatformLayout.CrossPlatformMeasure(double.PositiveInfinity, height);

        SetMeasuredDimension(
            (int)Math.Round(measured.Width * Density),
            (int)Math.Round(Math.Max(measured.Height, height) * Density));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        CrossPlatformLayout?.CrossPlatformArrange(
            new Rect(0, 0, (right - left) / Density, (bottom - top) / Density));
    }
}

/// <summary>
/// A <c>HorizontalScrollView</c> that commits to whole pages on release.
/// </summary>
/// <remarks>
/// The approach is the one React Native's <c>ReactHorizontalScrollView</c> takes, because Android
/// gives you nothing here. A fling is not allowed to run its course; instead the distance it *would*
/// have travelled is predicted with a throwaway <see cref="OverScroller"/>, that landing point is
/// rounded to the nearest page, and the view is smooth-scrolled there instead. A release with no
/// fling snaps to whichever page is nearest.
/// <para>
/// There is also no "scrolling has stopped" callback on Android, so rest is detected by watching the
/// offset go quiet across animation frames. That is the one piece genuinely worth copying rather
/// than inventing.
/// </para>
/// </remarks>
internal class MauiPagingScrollView : HorizontalScrollView
{
    private const int SnapDurationMs = 250;

    private readonly OverScroller predictor;
    private readonly OverScroller animator;
    private readonly int minimumFlingVelocity;

    private bool animating;
    private bool flungOnRelease;
    private int? deferredOffsetX;

    public MauiPagingScrollView(Context context) : base(context)
    {
        predictor = new OverScroller(context);
        animator = new OverScroller(context);
        minimumFlingVelocity = ViewConfiguration.Get(context)?.ScaledMinimumFlingVelocity ?? 0;
    }

    protected MauiPagingScrollView(nint handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
        predictor = new OverScroller(Context!);
        animator = new OverScroller(Context!);
        minimumFlingVelocity = ViewConfiguration.Get(Context!)?.ScaledMinimumFlingVelocity ?? 0;
    }

    /// <summary>Width of one page in pixels. Snapping lands on multiples of it.</summary>
    public int PageWidthPx { get; set; }

    /// <summary>
    /// Whether the user can scroll.
    /// </summary>
    /// <remarks>
    /// Enforced in both <see cref="OnInterceptTouchEvent"/> and <see cref="OnTouchEvent"/>. Guarding
    /// only the latter is what makes MAUI's own scroll view unusable for this: it declines to scroll
    /// but still lets the ancestor steal the gesture, so the drag underneath is cancelled and nothing
    /// takes its place.
    /// </remarks>
    public bool IsScrollEnabled { get; set; } = true;

    /// <summary>
    /// Moves the offset, holding the request back until there is something to scroll.
    /// </summary>
    /// <remarks>
    /// The first recentre is asked for before the view has been laid out, and an offset set then is
    /// clamped away against a scroll range that is still zero. Same trap as on iOS, same answer:
    /// notice there is no frame yet and re-apply once there is.
    /// </remarks>
    public void SetOffset(int x, bool animated)
    {
        // Whatever snap was running no longer describes where the content should be.
        animator.ForceFinished(true);
        animating = false;

        if (Width <= 0 || MaximumScrollX < x)
        {
            deferredOffsetX = x;
            return;
        }

        if (animated)
        {
            animating = true;
            animator.StartScroll(ScrollX, 0, x - ScrollX, 0, SnapDurationMs);
            PostInvalidateOnAnimation();
        }
        else
        {
            ScrollTo(x, 0);
        }

        deferredOffsetX = null;
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        base.OnLayout(changed, left, top, right, bottom);

        if (deferredOffsetX is not { } pending || MaximumScrollX < pending)
            return;

        ScrollTo(pending, 0);
        deferredOffsetX = null;
    }

    public event EventHandler? ScrollOffsetChanged;

    public event EventHandler? PageSettled;

    public override bool OnInterceptTouchEvent(MotionEvent? e) =>
        IsScrollEnabled && base.OnInterceptTouchEvent(e);

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (!IsScrollEnabled || e is null)
            return false;

        var releasing = e.Action is MotionEventActions.Up or MotionEventActions.Cancel;

        // Scoped to this one release rather than to the gesture. A ViewGroup does not intercept
        // ACTION_DOWN — the drawing surface underneath consumes it, and interception only begins once
        // a drag is recognised — so anything reset on DOWN here is never reset at all. A flag that
        // outlived its gesture left every slow release unsnapped after the first fling.
        if (releasing)
            flungOnRelease = false;

        var handled = base.OnTouchEvent(e);

        // A release fast enough to fling has already snapped from Fling, which base.OnTouchEvent
        // calls on the way through. A slower one never gets there, so it snaps here instead —
        // otherwise the pager simply stays wherever the finger left it, between two pages.
        if (releasing && !flungOnRelease)
            SnapTo(PageNearest(ScrollX));

        return handled;
    }

    /// <summary>
    /// Never lets a fling run its course; snaps to where it would have landed instead.
    /// </summary>
    /// <remarks>
    /// Returns <c>void</c> rather than <c>bool</c>, unlike the RecyclerView equivalent, so declining
    /// to handle it means simply not calling the base implementation.
    /// </remarks>
    public override void Fling(int velocityX)
    {
        if (!IsScrollEnabled || PageWidthPx <= 0)
            return;

        // Only ever reached from inside a release, so this cannot outlive one.
        flungOnRelease = true;

        if (Math.Abs(velocityX) < minimumFlingVelocity)
        {
            SnapTo(PageNearest(ScrollX));
            return;
        }

        // Where the fling would have ended had it been allowed to run. Rounding that — rather than the
        // offset at release — is what makes a short flick move a page while a slow drag does not.
        predictor.ForceFinished(true);
        predictor.Fling(ScrollX, 0, velocityX, 0, 0, Math.Max(0, MaximumScrollX), 0, 0);

        SnapTo(PageNearest(predictor.FinalX));
    }

    private int MaximumScrollX =>
        ChildCount == 0 ? 0 : Math.Max(0, GetChildAt(0)!.MeasuredWidth - Width);

    private int PageNearest(int offset)
    {
        if (PageWidthPx <= 0)
            return 0;

        var page = (int)Math.Round((double)offset / PageWidthPx);
        var lastPage = MaximumScrollX / PageWidthPx;

        return Math.Clamp(page, 0, Math.Max(0, lastPage));
    }

    /// <summary>
    /// Animates to a page with a scroller this class owns.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>SmoothScrollTo</c>. That drives the scroller inside
    /// <c>HorizontalScrollView</c>, which cannot be reached to stop it — so the recentre that follows
    /// a settle was overwritten on the scroller's next frame, and the offset snapped back to the page
    /// it had just left. Every swipe then compounded into several. Owning the animation makes
    /// <see cref="SetOffset"/> able to abort it, and makes "has it arrived" an exact answer rather
    /// than something polled for.
    /// </remarks>
    private void SnapTo(int page)
    {
        var target = Math.Clamp(page * PageWidthPx, 0, Math.Max(0, MaximumScrollX));

        animator.ForceFinished(true);

        if (target == ScrollX)
        {
            animating = false;
            PageSettled?.Invoke(this, EventArgs.Empty);
            return;
        }

        animating = true;
        animator.StartScroll(ScrollX, 0, target - ScrollX, 0, SnapDurationMs);
        PostInvalidateOnAnimation();
    }

    public override void ComputeScroll()
    {
        if (!animating)
        {
            base.ComputeScroll();
            return;
        }

        if (animator.ComputeScrollOffset())
        {
            ScrollTo(animator.CurrX, 0);
            PostInvalidateOnAnimation();
            return;
        }

        animating = false;
        PageSettled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnScrollChanged(int l, int t, int oldl, int oldt)
    {
        base.OnScrollChanged(l, t, oldl, oldt);

        ScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
    }

}
#endif
