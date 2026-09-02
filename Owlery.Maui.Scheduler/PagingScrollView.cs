namespace Owlery.Maui.Scheduler;

/// <summary>The page scrolling has come to rest on.</summary>
internal sealed class PagingPageSettledEventArgs(int page) : EventArgs
{
    public int Page { get; } = page;
}

/// <summary>A scroll the control has asked for and is waiting on.</summary>
internal sealed class PagingScrollRequestedEventArgs(double scrollX, bool animated) : EventArgs
{
    public double ScrollX { get; } = scrollX;

    public bool Animated { get; } = animated;
}

/// <summary>
/// A horizontal scroller that commits to whole pages, and whose offset can be moved without waiting
/// for a layout pass.
/// </summary>
/// <remarks>
/// MAUI's <see cref="ScrollView"/> cannot do either job. Paging is a platform setting it does not
/// expose, so a week only snapped on iOS; and its offset is applied through the handler on a later
/// pass, which is what let a frame be drawn between rotating the ring buffer and recentring on it —
/// see docs/design/pager.md.
/// <para>
/// Subclassing it was not an option on Android, where the platform view is a
/// <c>MauiScrollView : NestedScrollView</c> that grows an internal <c>MauiHorizontalScrollView</c>
/// child when a horizontal orientation is asked for. Paging would have had to be imposed across two
/// view classes participating in nested scrolling, neither of them ours.
/// </para>
/// <para>
/// The cross-platform half here owns the offset and the request plumbing; the platform half is a
/// <c>UIScrollView</c> on iOS and a <c>HorizontalScrollView</c> on Android. Without a registered
/// handler — which is the case in the headless tests — nothing scrolls on its own and the offset is
/// whatever <see cref="SetScrolledPosition"/> was last told, which is exactly what a test wants.
/// </para>
/// </remarks>
internal class PagingScrollView : ContentView
{
    private TaskCompletionSource<bool>? pendingScroll;
    private double pageWidth;
    private bool isScrollEnabled = true;

    public PagingScrollView()
    {
        IsClippedToBounds = true;
    }

    /// <summary>How far the content is scrolled, in device-independent units.</summary>
    public double ScrollX { get; private set; }

    /// <summary>
    /// The width one page occupies. Snapping lands on multiples of it.
    /// </summary>
    /// <remarks>
    /// Supplied rather than derived from the control's own width: while a change of day count is
    /// animating, a page is deliberately not a viewport wide.
    /// <para>
    /// The handler is told by hand. These are plain properties rather than bindable ones — nothing
    /// binds to them — so without this the mapper would run once at connect time, while the value was
    /// still its default, and never again.
    /// </para>
    /// </remarks>
    public double PageWidth
    {
        get => pageWidth;
        set
        {
            if (pageWidth.Equals(value))
                return;

            pageWidth = value;
            Handler?.UpdateValue(nameof(PageWidth));
        }
    }

    /// <summary>
    /// Whether the user can scroll. Set to <c>false</c> for the duration of a drag.
    /// </summary>
    /// <remarks>
    /// A scroller that refuses to scroll, not a view that refuses to be touched: the drag that turned
    /// this off is still being delivered through the content and must keep arriving.
    /// </remarks>
    public bool IsScrollEnabled
    {
        get => isScrollEnabled;
        set
        {
            if (isScrollEnabled == value)
                return;

            isScrollEnabled = value;
            Handler?.UpdateValue(nameof(IsScrollEnabled));
        }
    }

    /// <summary>
    /// Raised whenever the offset changes, from any cause. Read <see cref="ScrollX"/> for the value.
    /// </summary>
    /// <remarks>
    /// Deliberately carries no arguments. This fires once per frame for the whole of every scroll,
    /// and an event-args object per frame is a few hundred short-lived allocations a second during a
    /// fling — for a value the receiver can read off the sender.
    /// </remarks>
    public event EventHandler? Scrolled;

    /// <summary>
    /// Raised once scrolling has come to rest on a page.
    /// </summary>
    /// <remarks>
    /// The page is chosen here rather than by whoever is listening, because only the platform knows
    /// the release velocity and where a fling would land. That is the whole reason this control
    /// exists: MAUI exposes no paging, so a listener had to infer the decision from the offset going
    /// quiet — which meant waiting for inertia to run out before it could even guess.
    /// </remarks>
    public event EventHandler<PagingPageSettledEventArgs>? PageSettled;

    /// <summary>Raised when a scroll is asked for. The handler, or a test, performs it.</summary>
    public event EventHandler<PagingScrollRequestedEventArgs>? ScrollToRequested;

    /// <summary>
    /// Moves the offset and returns once the platform reports it has arrived.
    /// </summary>
    /// <remarks>
    /// An animated scroll completes when the animation ends. An unanimated one is applied straight to
    /// the platform view, so by the time this returns the offset has already changed — that is what
    /// closes the window described in docs/design/pager.md, and why it must stay a method rather than
    /// becoming a bindable property applied by a mapper on some later pass.
    /// </remarks>
    public Task ScrollToAsync(double x, bool animated)
    {
        // A request still in flight is abandoned rather than left to hang: whoever awaited it is no
        // longer describing where the content should be.
        pendingScroll?.TrySetResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingScroll = completion;

        ScrollToRequested?.Invoke(this, new PagingScrollRequestedEventArgs(x, animated));

        return completion.Task;
    }

    /// <summary>
    /// Moves the offset now, before anything else can be drawn.
    /// </summary>
    /// <remarks>
    /// Deliberately not a <see cref="Task"/>: the caller needs the offset to have changed by the time
    /// this returns, not merely to have been asked for. Both platforms can do that —
    /// <c>setContentOffset:animated:NO</c> and <c>HorizontalScrollView.ScrollTo</c> are each
    /// immediate — and it is what stops a frame being composited between rotating the pages and
    /// recentring on them. See docs/design/pager.md.
    /// </remarks>
    public void ScrollTo(double x) =>
        ScrollToRequested?.Invoke(this, new PagingScrollRequestedEventArgs(x, animated: false));

    /// <summary>Records an offset the platform has moved to, and tells anyone watching.</summary>
    public void SetScrolledPosition(double x)
    {
        ScrollX = x;
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Completes whatever <see cref="ScrollToAsync"/> is waiting on.</summary>
    public void SendScrollFinished()
    {
        var completion = pendingScroll;
        pendingScroll = null;
        completion?.TrySetResult(true);
    }

    /// <summary>Reports that scrolling has come to rest. Called by the handler, or by a test.</summary>
    public void SendPageSettled() => PageSettled?.Invoke(this, new PagingPageSettledEventArgs(PageAt(ScrollX)));

    /// <summary>The page an offset is nearest to.</summary>
    public int PageAt(double x) => PageWidth <= 0 ? 0 : (int)Math.Round(x / PageWidth);
}
