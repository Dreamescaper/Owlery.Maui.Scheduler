using Microsoft.Maui.Controls;
using Owlery.Maui.Scheduler.Internal;
using Microsoft.Maui.Graphics;
using MC = Microsoft.Maui.Controls;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>An appointment view the tests can pick out of the surface and track by identity.</summary>
internal sealed class TestAppointmentView : ContentView;

/// <summary>
/// A <see cref="SchedulerWeekView"/> hosted in a headless MAUI app, arranged to a known size, with
/// scrolling and touch wired up so tests can drive it the way a person would.
/// </summary>
internal sealed class SchedulerHarness
{
    public const double ViewWidth = 400;
    public const double ViewHeight = 700;
    public const double GutterWidth = 52;

    /// <summary>Width of one week page — what the control calls the viewport.</summary>
    public const double PageWidth = ViewWidth - GutterWidth;

    public SchedulerWeekView Scheduler { get; }

    public TestDispatcher Dispatcher { get; }

    public List<SchedulerVisibleDatesChangedEventArgs> VisibleDatesReports { get; } = [];

    public List<SchedulerCellTappedEventArgs> CellTaps { get; } = [];

    public List<SchedulerAppointmentTappedEventArgs> AppointmentTaps { get; } = [];

    public List<SchedulerAppointmentDragStartingEventArgs> DragStarts { get; } = [];

    public List<SchedulerAppointmentDroppedEventArgs> Drops { get; } = [];

    /// <summary>Every scroll the control asked the pager for, in order.</summary>
    public List<(double ScrollX, bool Animated)> PagerScrolls { get; } = [];

    private readonly ScrollView pagerScroll;
    private readonly IGraphicsView surfaceView;
    private readonly TimeGutterDrawable gutterDrawable;
    private readonly Layout surface;

    public SchedulerHarness(DateTime displayDate, IEnumerable<ISchedulerAppointment>? items = null)
    {
        var (application, dispatcher) = TestApplication.Create();
        Dispatcher = dispatcher;

        Scheduler = new SchedulerWeekView
        {
            AppointmentTemplate = new DataTemplate(() => new TestAppointmentView()),
            TimeGutterWidth = GutterWidth,
            DisplayDate = displayDate,
            ItemsSource = items
        };

        Scheduler.VisibleDatesChanged += (_, e) => VisibleDatesReports.Add(e);
        Scheduler.CellTapped += (_, e) => CellTaps.Add(e);
        Scheduler.AppointmentTapped += (_, e) => AppointmentTaps.Add(e);
        Scheduler.AppointmentDragStarting += (_, e) => DragStarts.Add(e);
        Scheduler.AppointmentDropped += (_, e) => Drops.Add(e);

        application.Windows[0].Page = new ContentPage { Content = Scheduler };

        foreach (var scrollView in Descendants(Scheduler).OfType<ScrollView>())
            ShimScrolling(scrollView, PagerScrolls);

        // Laying the control out is what gives it a viewport width and builds the three weeks.
        ((IView)Scheduler).Measure(ViewWidth, ViewHeight);
        ((IView)Scheduler).Arrange(new Rect(0, 0, ViewWidth, ViewHeight));

        // The horizontal pager is the inner of the two scroll views; the grid is the only
        // GraphicsView inside it.
        pagerScroll = Descendants(Scheduler).OfType<ScrollView>()
            .First(scrollView => scrollView.Orientation == ScrollOrientation.Horizontal);
        surface = (Layout)pagerScroll.Content;
        surfaceView = Descendants(surface).OfType<GraphicsView>().First();

        gutterDrawable = Descendants(Scheduler)
            .OfType<GraphicsView>()
            .Select(view => view.Drawable)
            .OfType<TimeGutterDrawable>()
            .First();
    }

    /// <summary>The appointment views currently showing, in the order the surface holds them.</summary>
    public IReadOnlyList<TestAppointmentView> VisibleAppointments =>
        [.. surface.OfType<TestAppointmentView>().Where(view => view.IsVisible)];

    /// <summary>Every appointment view ever created, including pooled ones lying hidden.</summary>
    public IReadOnlyList<TestAppointmentView> AllAppointmentViews => [.. surface.OfType<TestAppointmentView>()];

    public static double DayWidth => PageWidth / 7;

    /// <summary>A point on the scrolling surface, addressed the way a person would think about it.</summary>
    public Point PointAt(int slotIndex, int dayIndex, TimeSpan time, double acrossDay = 0.5)
    {
        var x = slotIndex * PageWidth + (dayIndex + acrossDay) * DayWidth;
        var y = (time.TotalMinutes - Scheduler.StartHour * 60) / 60 * Scheduler.HourHeight;
        return new Point(x, y);
    }

    public void Tap(Point point) => Tap(point.X, point.Y);

    /// <summary>The view riding the finger during a drag, identified by being lifted above the rest.</summary>
    public TestAppointmentView? LiftedAppointment =>
        VisibleAppointments.FirstOrDefault(view => view.ZIndex == 100);

    /// <summary>The faded original left behind at the start of a drag.</summary>
    public TestAppointmentView? GhostAppointment =>
        VisibleAppointments.FirstOrDefault(view => view.ZIndex != 100 && view.Opacity < 1);

    /// <summary>The time shown in the gutter while an appointment is being dragged.</summary>
    public string? DragTimeIndicator => gutterDrawable.HighlightText;

    /// <summary>Minute of day the gutter indicator points at.</summary>
    public double? DragTimeIndicatorMinutes => gutterDrawable.HighlightMinutes;

    public Rect BoundsOf(TestAppointmentView view)
    {
        var bounds = AbsoluteLayout.GetLayoutBounds(view);
        return new Rect(bounds.X + view.TranslationX, bounds.Y + view.TranslationY, bounds.Width, bounds.Height);
    }

    /// <summary>Taps a point on the scrolling surface.</summary>
    public void Tap(double x, double y)
    {
        var point = new PointF((float)x, (float)y);
        surfaceView.StartInteraction([point]);
        surfaceView.EndInteraction([point], isInsideBounds: true);
    }

    /// <summary>Presses, holds long enough to pick an appointment up, drags, and releases.</summary>
    public void LongPressDrag(double fromX, double fromY, double toX, double toY)
    {
        surfaceView.StartInteraction([new PointF((float)fromX, (float)fromY)]);
        FireLongPressTimer();
        surfaceView.DragInteraction([new PointF((float)toX, (float)toY)]);
        surfaceView.EndInteraction([new PointF((float)toX, (float)toY)], isInsideBounds: true);
    }

    /// <summary>Presses and holds long enough to pick an appointment up, without releasing.</summary>
    public void BeginDrag(Point point)
    {
        surfaceView.StartInteraction([new PointF((float)point.X, (float)point.Y)]);
        FireLongPressTimer();
    }

    /// <summary>Moves an in-progress drag.</summary>
    public void DragTo(Point point) => surfaceView.DragInteraction([new PointF((float)point.X, (float)point.Y)]);

    /// <summary>Releases an in-progress drag.</summary>
    public void Release(Point point) =>
        surfaceView.EndInteraction([new PointF((float)point.X, (float)point.Y)], isInsideBounds: true);

    /// <summary>Elapses the dwell that pages to the next week while dragging against an edge.</summary>
    public void FireEdgePagingTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(600));

    /// <summary>A point inside the trailing edge zone of the visible week.</summary>
    public Point TrailingEdge(double y) => new(PageWidth * 2 - 8, y);

    /// <summary>A point inside the leading edge zone of the visible week.</summary>
    public Point LeadingEdge(double y) => new(PageWidth + 8, y);

    /// <summary>Presses and immediately moves, which is how a scroll begins rather than a drag.</summary>
    public void PressAndMove(double fromX, double fromY, double toX, double toY)
    {
        surfaceView.StartInteraction([new PointF((float)fromX, (float)fromY)]);
        surfaceView.DragInteraction([new PointF((float)toX, (float)toY)]);
        surfaceView.EndInteraction([new PointF((float)toX, (float)toY)], isInsideBounds: true);
    }

    /// <summary>Scrolls the pager to a page and lets the snap settle, as a completed swipe would.</summary>
    public void SwipeToPage(int page)
    {
        pagerScroll.SetScrolledPosition(page * PageWidth, 0);
        FireSnapTimer();
    }

    /// <summary>Simulates an ancestor scroll view claiming the gesture part-way through.</summary>
    public void CancelInteraction() => surfaceView.CancelInteraction();

    public void FireSnapTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(90));

    public void FireLongPressTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(350));

    /// <summary>
    /// Stands in for the platform scroll view: applies the requested offset and reports the scroll
    /// as finished, so awaited programmatic scrolls complete.
    /// </summary>
    private static void ShimScrolling(ScrollView scrollView, List<(double ScrollX, bool Animated)> log)
    {
        scrollView.ScrollToRequested += (_, e) =>
        {
            if (scrollView.Orientation == ScrollOrientation.Horizontal)
                log.Add((e.ScrollX, e.ShouldAnimate));

            scrollView.SetScrolledPosition(e.ScrollX, e.ScrollY);
            scrollView.SendScrollFinished();
        };
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        foreach (var child in ((IElementController)root).LogicalChildren)
        {
            yield return child;

            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
