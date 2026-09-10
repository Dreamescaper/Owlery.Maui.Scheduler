using System.Reflection;
using Microsoft.Maui.Controls;
using Owlery.Maui.Scheduler.Internal;
using Microsoft.Maui.Graphics;
using MC = Microsoft.Maui.Controls;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>An appointment view the tests can pick out of the surface and track by identity.</summary>
internal sealed class TestAppointmentView : ContentView
{
    /// <summary>What this view measures, so the agenda's row heights are exercised rather than zero.</summary>
    public const double MeasuredHeight = 40;

    public TestAppointmentView() => Content = new BoxView { HeightRequest = MeasuredHeight };

    public int BindingChanges { get; private set; }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        BindingChanges++;
    }
}

/// <summary>
/// A <see cref="SchedulerView"/> hosted in a headless MAUI app, arranged to a known size, with
/// scrolling and touch wired up so tests can drive it the way a person would.
/// </summary>
internal sealed class SchedulerHarness
{
    public const double ViewWidth = 400;
    public const double ViewHeight = 700;
    public const double GutterWidth = 52;

    /// <summary>Width of one week page — what the control calls the viewport.</summary>
    public const double PageWidth = ViewWidth - GutterWidth;

    public SchedulerView Scheduler { get; }

    public TestDispatcher Dispatcher { get; }

    public List<SchedulerVisibleDatesChangedEventArgs> VisibleDatesReports { get; } = [];

    public List<SchedulerCellTappedEventArgs> CellTaps { get; } = [];

    public List<SchedulerAppointmentTappedEventArgs> AppointmentTaps { get; } = [];

    public List<SchedulerAppointmentDragStartingEventArgs> DragStarts { get; } = [];

    public List<SchedulerAppointmentDroppedEventArgs> Drops { get; } = [];

    public List<SchedulerAppointmentDropTargetChangedEventArgs> DropTargetChanges { get; } = [];

    public List<SchedulerHeaderTappedEventArgs> HeaderTaps { get; } = [];

    public List<SchedulerTimeGutterTappedEventArgs> GutterTaps { get; } = [];

    /// <summary>Every scroll the control asked the pager for, in order.</summary>
    public List<(double ScrollX, bool Animated)> PagerScrolls { get; } = [];

    private readonly ContentPage page;
    private readonly PagingScrollView pagerScroll;
    private readonly ScrollView timelineScroll;
    private readonly List<Action> pendingScrollCompletions = [];
    private readonly GraphicsView surfaceGraphicsView;
    private IGraphicsView surfaceView => surfaceGraphicsView;
    private readonly Layout surface;
    private readonly Border dragIndicator;
    /// <summary>The day-column strip, or null on a surface that has none.</summary>
    private readonly AbsoluteLayout? headerStrip;
    private readonly IGraphicsView gutterInput;

    private readonly int visibleDays;
    private readonly SchedulerViewMode viewMode;

    public SchedulerHarness(
        DateTime displayDate,
        IEnumerable<ISchedulerAppointment>? items = null,
        int visibleDays = 7,
        SchedulerViewMode viewMode = SchedulerViewMode.Timeline,
        Action<SchedulerView>? configure = null)
    {
        this.visibleDays = visibleDays;
        this.viewMode = viewMode;

        var (application, dispatcher) = TestApplication.Create();
        Dispatcher = dispatcher;

        Scheduler = new SchedulerView
        {
            AppointmentTemplate = new DataTemplate(() => new TestAppointmentView()),
            MonthAppointmentTemplate = new DataTemplate(() => new TestAppointmentView()),
            TimeGutterWidth = GutterWidth,
            VisibleDays = visibleDays,
            ViewMode = viewMode,
            DisplayDate = displayDate,
            ItemsSource = items
        };

        configure?.Invoke(Scheduler);

        Scheduler.VisibleDatesChanged += (_, e) => VisibleDatesReports.Add(e);
        Scheduler.CellTapped += (_, e) => CellTaps.Add(e);
        Scheduler.AppointmentTapped += (_, e) => AppointmentTaps.Add(e);
        Scheduler.AppointmentDragStarting += (_, e) => DragStarts.Add(e);
        Scheduler.AppointmentDropped += (_, e) => Drops.Add(e);
        Scheduler.HeaderTapped += (_, e) => HeaderTaps.Add(e);
        Scheduler.TimeGutterTapped += (_, e) => GutterTaps.Add(e);
        Scheduler.AppointmentDropTargetChanged += (_, e) => DropTargetChanges.Add(e);

        application.Windows[0].Page = page = new ContentPage { Content = Scheduler };

        foreach (var scrollView in Descendants(Scheduler).OfType<ScrollView>())
            ShimScrolling(scrollView);

        foreach (var pager in Descendants(Scheduler).OfType<PagingScrollView>())
            ShimPaging(pager);

        // Laying the control out is what gives it a viewport width and builds the three weeks.
        ((IView)Scheduler).Measure(ViewWidth, ViewHeight);
        ((IView)Scheduler).Arrange(new Rect(0, 0, ViewWidth, ViewHeight));

        // The grid is the only GraphicsView inside the pager.
        pagerScroll = Descendants(Scheduler).OfType<PagingScrollView>().First();
        timelineScroll = Descendants(Scheduler).OfType<ScrollView>()
            .First(scrollView => scrollView.Orientation == ScrollOrientation.Vertical);
        surface = (Layout)pagerScroll.Content;
        surfaceGraphicsView = Descendants(surface).OfType<GraphicsView>().First();

        dragIndicator = Descendants(Scheduler)
            .OfType<Border>()
            .First(border => border.AutomationId == TimeGutter.IndicatorAutomationId);

        // Both are found through something already identified rather than by size: the strip and the
        // scrolling surface are the same width, so a width test would pick whichever came first.
        gutterInput = Descendants((Element)dragIndicator.Parent).OfType<GraphicsView>().First();
        // An agenda has no day-column strip at all, so there is no grid of columns to find it by.
        headerStrip = Descendants(Scheduler)
            .OfType<Grid>()
            .FirstOrDefault(grid => grid.Parent is AbsoluteLayout && grid.ColumnDefinitions.Count > 0)
            ?.Parent as AbsoluteLayout;
    }

    /// <summary>
    /// Appointment views that are drawn but belong to no page. Always empty.
    /// </summary>
    /// <remarks>
    /// The shape of a phantom: it reads as an appointment, refuses to be pressed — hit-testing walks
    /// the pages, so the press falls through to the cell underneath and behaves like an empty one —
    /// and stays on the column it was last left at while every other week scrolls past, because only
    /// a page's views are translated when the weeks rotate. Asserted after gestures rather than
    /// reasoned about: the state is reachable from several directions and unrecoverable once reached,
    /// so the cheap thing is to check for it rather than to argue that it cannot happen.
    /// <para>
    /// Everything under the control, not only what is on the scrolling surface. The follower a drag
    /// carries lives on the overlay instead, in the control's own coordinates and taking no input, so
    /// one left behind is a phantom of exactly the same kind — and looking only at the surface is how
    /// it would be missed.
    /// </para>
    /// </remarks>
    public IReadOnlyList<TestAppointmentView> OrphanedAppointments
    {
        get
        {
            var placed = Scheduler.PlacedViews.ToHashSet();

            return
            [
                .. Descendants(Scheduler)
                    .OfType<TestAppointmentView>()
                    .Where(view => view.IsVisible && !placed.Contains(view))
            ];
        }
    }

    /// <summary>The appointment views currently showing, in the order the surface holds them.</summary>
    public IReadOnlyList<TestAppointmentView> VisibleAppointments =>
        [.. surface.OfType<TestAppointmentView>().Where(view => view.IsVisible)];

    /// <summary>The "+" affordance marking the selected cell, if one is showing.</summary>
    public View? CellSelectionAffordance => surface
        .OfType<View>()
        .FirstOrDefault(view => view is Border && view.IsVisible);

    /// <summary>How far apart the three rendered pages sit. A month has no gutter to give up.</summary>
    /// <remarks>An agenda does not page at all, so its pages sit on top of each other.</remarks>
    public double PageStride => viewMode switch
    {
        SchedulerViewMode.Month => ViewWidth,
        SchedulerViewMode.Agenda => 0,
        _ => PageWidth
    };

    /// <summary>The height the scrolling surface asks for, which is the agenda's content height.</summary>
    public double SurfaceRequestedHeight => ((VisualElement)surface).HeightRequest;

    /// <summary>The agenda's current vertical offset.</summary>
    public double AgendaScrollY => timelineScroll.ScrollY;

    /// <summary>How far the content is held while the platform catches up with a compensated offset.</summary>
    /// <remarks>
    /// The vertical scroll view's own content, which is the element the control translates — a
    /// transform on the pager's content view is discarded by its handler.
    /// </remarks>
    public double SurfaceTranslationY => ((VisualElement)timelineScroll.Content).TranslationY;

    /// <summary>
    /// Where a view actually appears to the reader, rather than where it sits in the content.
    /// </summary>
    /// <remarks>
    /// Its position in the surface, less the offset the platform has applied, plus whatever the
    /// control is holding the surface by while it waits for the rest. This is the number a glitch
    /// shows up in: content can be reflowed and the scroll compensated and still be visually still.
    /// </remarks>
    public double VisualTopOf(View view) => BoundsOf(view).Y + SurfaceTranslationY - AgendaScrollY;

    /// <summary>The agenda's headings and day markers, whatever kind they are.</summary>
    public IReadOnlyList<View> AgendaSections =>
    [
        .. surface.OfType<View>().Where(view =>
            view.IsVisible && view.BindingContext is SchedulerAgendaSection)
    ];

    /// <summary>The headings of one kind, in the order they appear down the list.</summary>
    public IReadOnlyList<SchedulerAgendaSection> SectionsOfKind(SchedulerAgendaSectionKind kind) =>
    [
        .. AgendaSections
            .Select(view => (SchedulerAgendaSection)view.BindingContext)
            .Where(section => section.Kind == kind)
            .OrderBy(section => section.Date)
    ];

    /// <summary>All label text inside a view, including the built-in agenda section variants.</summary>
    public IReadOnlyList<string> TextWithin(View view) =>
    [
        .. Descendants(view)
            .OfType<Label>()
            .Where(label => label.IsVisible && !string.IsNullOrEmpty(label.Text))
            .Select(label => label.Text)
    ];

    /// <summary>The colours the labels inside a view are painted, for chrome that paints itself.</summary>
    public IReadOnlyList<Color> LabelColorsWithin(View view) =>
    [
        .. Descendants(view)
            .OfType<Label>()
            .Where(label => label.IsVisible)
            .Select(label => label.TextColor)
    ];

    /// <summary>
    /// Scrolls the vertical surface, as a gesture would.
    /// </summary>
    /// <remarks>
    /// <c>SetScrolledPosition</c> is MAUI's handler-to-virtual-view path and raises <c>Scrolled</c>,
    /// just as the platform handler does after a gesture.
    /// </remarks>
    /// <summary>
    /// Takes the control off its page and puts it back, which is what a host navigating away and
    /// back does — and the only way to reach <c>Loaded</c>/<c>Unloaded</c> without a platform.
    /// </summary>
    /// <remarks>
    /// Leaving the visual tree is the real event, not a simulated one: MAUI raises <c>Unloaded</c>
    /// and <c>IsLoaded</c> goes false, exactly as on a device. Nothing else about the control is
    /// reset, so the same harness keeps working across the round trip.
    /// </remarks>
    public void DetachFromWindow() => page.Content = null;

    public void ReattachToWindow() => page.Content = Scheduler;

    public void ScrollVerticallyTo(double y)
    {
        timelineScroll.SetScrolledPosition(0, y);
    }

    /// <summary>The appointments on the page currently on screen, ignoring the two either side.</summary>
    public IReadOnlyList<TestAppointmentView> CentrePageAppointments =>
    [
        .. VisibleAppointments.Where(view =>
        {
            var x = BoundsOf(view).X;
            return x >= PageStride && x < PageStride * 2;
        })
    ];

    /// <summary>The column labels of each rendered page's header, left to right.</summary>
    public IReadOnlyList<IReadOnlyList<Label>> PageHeaders =>
    [
        .. Descendants(Scheduler)
            .OfType<Grid>()
            .Where(grid => grid.Parent is AbsoluteLayout && grid.ColumnDefinitions.Count > 0)
            .OrderBy(grid => grid.TranslationX)
            .Select(grid => (IReadOnlyList<Label>)
            [
                .. Descendants(grid)
                    .OfType<Label>()
                    .Where(label => label.FontSize == 11)
            ])
    ];

    /// <summary>The day-number labels in the page headers, which only a timeline has.</summary>
    public IReadOnlyList<Label> HeaderDayNumbers =>
    [
        .. Descendants(Scheduler)
            .OfType<Grid>()
            .Where(grid => grid.Parent is AbsoluteLayout && grid.ColumnDefinitions.Count > 0)
            .SelectMany(grid => Descendants(grid).OfType<Label>())
            .Where(label => label.FontSize == 16)
    ];

    public IDrawable SurfaceDrawable => surfaceGraphicsView.Drawable;

    public Color GridBackground => surfaceGraphicsView.BackgroundColor;

    public Color GutterBackground => ((VisualElement)dragIndicator.Parent).BackgroundColor;

    public Border DragTimeIndicatorView => dragIndicator;

    public ActivityIndicator BusyIndicator => Descendants(Scheduler).OfType<ActivityIndicator>().Single();

    public double MonthCellWidth => ViewWidth / 7;

    public double MonthCellHeight => (ViewHeight - Scheduler.HeaderHeight) / 6;

    /// <summary>The middle of one cell of a month page, addressed by its index in the 42-day grid.</summary>
    public Point MonthCellAt(int slotIndex, int cellIndex) => new(
        slotIndex * ViewWidth + (cellIndex % 7 + 0.5) * MonthCellWidth,
        (cellIndex / 7 + 0.5) * MonthCellHeight);

    /// <summary>Which cell of the centre page a view has been placed in.</summary>
    public int CellOf(View view)
    {
        var bounds = BoundsOf(view);
        var column = (int)((bounds.X - PageStride) / MonthCellWidth);

        return (int)(bounds.Y / MonthCellHeight) * 7 + column;
    }

    /// <summary>Every appointment view ever created, including pooled ones lying hidden.</summary>
    public IReadOnlyList<TestAppointmentView> AllAppointmentViews => [.. surface.OfType<TestAppointmentView>()];

    /// <summary>Column width for a full week, for tests that do not vary the day count.</summary>
    public static double DayWidth => PageWidth / 7;

    /// <summary>Column width for this harness's day count.</summary>
    public double ColumnWidth => PageWidth / visibleDays;

    /// <summary>A point on the scrolling surface, addressed the way a person would think about it.</summary>
    public Point PointAt(int slotIndex, int dayIndex, TimeSpan time, double acrossDay = 0.5)
    {
        var x = slotIndex * PageWidth + (dayIndex + acrossDay) * ColumnWidth;
        var y = (time.TotalMinutes - Scheduler.StartHour * 60) / 60 * Scheduler.HourHeight;
        return new Point(x, y);
    }

    public void Tap(Point point) => Tap(point.X, point.Y);

    /// <summary>The view riding the finger during a drag. It lives on the overlay, not the surface.</summary>
    public TestAppointmentView? DraggedAppointment => Descendants(Scheduler)
        .OfType<TestAppointmentView>()
        .FirstOrDefault(view => view.IsVisible && !ReferenceEquals(view.Parent, surface));

    /// <summary>The faded original left behind in its week at the start of a drag.</summary>
    public TestAppointmentView? GhostAppointment =>
        VisibleAppointments.FirstOrDefault(view => view.Opacity < 1);

    /// <summary>Where the dragged view sits in the control's own coordinates.</summary>
    public Rect DraggedAppointmentBounds =>
        DraggedAppointment is null ? Rect.Zero : AbsoluteLayout.GetLayoutBounds(DraggedAppointment);

    /// <summary>The drag-time chip itself, for tests that care where it sits rather than what it says.</summary>
    public View DragIndicatorView => dragIndicator;

    /// <summary>The time shown in the gutter while an appointment is being dragged.</summary>
    public string? DragTimeIndicator =>
        dragIndicator.IsVisible ? ((Label)dragIndicator.Content!).Text : null;

    /// <summary>Minute of day the gutter indicator points at, read back from where it was placed.</summary>
    public double? DragTimeIndicatorMinutes
    {
        get
        {
            if (!dragIndicator.IsVisible)
                return null;

            var bounds = AbsoluteLayout.GetLayoutBounds(dragIndicator);
            var centre = bounds.Y + bounds.Height / 2;

            return centre / Scheduler.HourHeight * 60 + Scheduler.StartHour * 60;
        }
    }

    /// <summary>The hour labels down the gutter, top to bottom.</summary>
    public IReadOnlyList<Label> HourLabels =>
    [
        .. Descendants(Scheduler)
            .OfType<Label>()
            .Where(label => label.HorizontalTextAlignment == TextAlignment.End && label.Parent is AbsoluteLayout)
            .OrderBy(label => AbsoluteLayout.GetLayoutBounds(label).Y)
    ];

    /// <summary>What assistive technology would read for a view.</summary>
    public static string? DescriptionOf(View view) => SemanticProperties.GetDescription(view);

    public Rect BoundsOf(View view)
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

    /// <summary>How far down the day the timeline is scrolled.</summary>
    public double TimelineScrollY => timelineScroll.ScrollY;

    /// <summary>How much of the day is on screen.</summary>
    public double TimelineViewportHeight => ViewHeight - Scheduler.HeaderHeight;

    /// <summary>Elapses one step of the scroll that runs while dragging against the top or bottom.</summary>
    public void FireEdgeScrollTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(60));

    /// <summary>A point inside the bottom edge zone of the visible timeline.</summary>
    public Point BottomEdge(double x) => new(x, TimelineScrollY + TimelineViewportHeight - 8);

    /// <summary>A point inside the top edge zone of the visible timeline.</summary>
    public Point TopEdge(double x) => new(x, TimelineScrollY + 8);

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

    /// <summary>Scrolls the pager to a page and reports it settled, as a completed swipe would.</summary>
    /// <remarks>
    /// The platform picks the page and says so; there is no longer a timer inferring it from the
    /// offset going quiet, so a test says which page the swipe landed on rather than eliding it.
    /// </remarks>
    public void SwipeToPage(int page)
    {
        pagerScroll.SetScrolledPosition(page * PageStride);
        pagerScroll.SendPageSettled();
    }

    /// <summary>Reports the pager settled wherever it currently sits.</summary>
    public void SettlePager() => pagerScroll.SendPageSettled();

    /// <summary>Simulates an ancestor scroll view claiming the gesture part-way through.</summary>
    public void CancelInteraction() => surfaceView.CancelInteraction();

    /// <summary>When set, programmatic pager scrolls do not report as finished until asked.</summary>
    public bool DeferPagerScrolls { get; set; }

    /// <summary>Lets every held-back pager scroll report as finished, settling the slide.</summary>
    public void CompletePendingScrolls()
    {
        var completions = pendingScrollCompletions.ToArray();
        pendingScrollCompletions.Clear();

        foreach (var complete in completions)
            complete();
    }

    /// <summary>Moves the pager, as a frame of the slide edge paging performs would.</summary>
    public void ScrollPagerTo(double x) => pagerScroll.SetScrolledPosition(x);

    public void FireLongPressTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(350));

    /// <summary>Completes a deferred backward growth, once the agenda's scroll has gone quiet.</summary>
    public void FireAgendaBackwardGrowTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(200));

    /// <summary>Closes the window in which the agenda treats scroll events as its own navigation.</summary>
    public void FireAgendaNavigationSettleTimer() => Dispatcher.FireTimer(TimeSpan.FromMilliseconds(250));

    /// <summary>Where a day's header sits along the three-page strip.</summary>
    public double HeaderXAt(int slotIndex, int dayIndex) =>
        slotIndex * PageStride + (dayIndex + 0.5) * (PageStride / visibleDays);

    /// <summary>Where a time sits down the hour gutter.</summary>
    public double GutterYAt(TimeSpan time) =>
        (time.TotalMinutes - Scheduler.StartHour * 60) / 60 * Scheduler.HourHeight;

    /// <summary>Taps a day header, addressed by the page and column it belongs to.</summary>
    public void TapHeader(int slotIndex, int dayIndex) =>
        SendTap(headerStrip!, new Point(HeaderXAt(slotIndex, dayIndex), Scheduler.HeaderHeight / 2));

    /// <summary>Taps the hour gutter level with a time.</summary>
    public void TapGutter(TimeSpan time) => TapGutterAt(GutterYAt(time));

    /// <summary>Taps the hour gutter at a raw height, for the cases outside the day window.</summary>
    /// <remarks>
    /// Through the drawing surface, like the grid, rather than through a gesture recognizer — see the
    /// note in <c>TimeGutter</c> on why the hours take their input that way.
    /// </remarks>
    public void TapGutterAt(double y)
    {
        var point = new PointF((float)(GutterWidth / 2), (float)y);

        gutterInput.StartInteraction([point]);
        gutterInput.EndInteraction([point], isInsideBounds: true);
    }

    /// <summary>
    /// Stands in for the platform delivering a tap.
    /// </summary>
    /// <remarks>
    /// <c>SendTapped</c> is how the platform raises a tap, and it is internal because app code has no
    /// business calling it — but a harness is not app code, it is the platform. The position has to be
    /// supplied because there is nothing underneath to ask for one: MAUI reads it back through a
    /// callback the gesture platform normally provides.
    /// <para>
    /// Reflection rather than <c>InternalsVisibleTo</c>, which is not ours to grant. If MAUI renames
    /// this, every tap test fails at once with the message below rather than silently passing.
    /// </para>
    /// </remarks>
    private static void SendTap(View view, Point point)
    {
        var send = typeof(TapGestureRecognizer).GetMethod(
            "SendTapped",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException("TapGestureRecognizer.SendTapped is gone; the tap harness needs updating.");

        // The overload gained a position callback when TappedEventArgs.GetPosition arrived; take
        // either shape rather than pinning the test suite to one MAUI version.
        object?[] arguments = send.GetParameters().Length switch
        {
            1 => [view],
            _ => [view, (Func<IElement?, Point?>)(_ => point)]
        };

        foreach (var recognizer in view.GestureRecognizers.OfType<TapGestureRecognizer>())
            send.Invoke(recognizer, arguments);
    }

    /// <summary>
    /// Stands in for the platform scroll view: applies the requested offset and reports the scroll
    /// as finished, so awaited programmatic scrolls complete.
    /// </summary>
    /// <summary>
    /// Makes the vertical scroll view behave the way iOS's does: a requested offset is not applied.
    /// </summary>
    /// <remarks>
    /// The default shim applies <c>ScrollToAsync</c> at once, which no platform does — the handler
    /// applies it on a later pass, and reports the offsets it passes through on the way. With this
    /// set, the request is recorded in <see cref="LastVerticalScrollRequest"/> and the test delivers
    /// those offsets itself with <see cref="ScrollVerticallyTo"/>.
    /// </remarks>
    public bool DeferVerticalScrollRequests { get; set; }

    /// <summary>The offset last asked for, whether or not it was applied.</summary>
    public double LastVerticalScrollRequest { get; private set; }

    private void ShimScrolling(ScrollView scrollView)
    {
        scrollView.ScrollToRequested += (_, e) =>
        {
            if (scrollView.Orientation == ScrollOrientation.Vertical)
                LastVerticalScrollRequest = e.ScrollY;

            if (DeferVerticalScrollRequests && scrollView.Orientation == ScrollOrientation.Vertical)
                return;

            scrollView.SetScrolledPosition(e.ScrollX, e.ScrollY);
            scrollView.SendScrollFinished();
        };
    }

    /// <summary>
    /// Stands in for the pager's platform handler: applies the requested offset and reports the
    /// scroll as finished, so awaited scrolls complete.
    /// </summary>
    private void ShimPaging(PagingScrollView pager)
    {
        pager.ScrollToRequested += (_, e) =>
        {
            PagerScrolls.Add((e.ScrollX, e.Animated));

            pager.SetScrolledPosition(e.ScrollX);

            // Deferring the completion leaves the awaited scroll in flight, which is the only way to
            // observe the control mid-slide: the shim is otherwise instantaneous.
            if (DeferPagerScrolls)
                pendingScrollCompletions.Add(pager.SendScrollFinished);
            else
                pager.SendScrollFinished();
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
