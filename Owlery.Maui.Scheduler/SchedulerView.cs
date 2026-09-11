using System.Collections.Specialized;
using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>
/// A scheduler built from plain .NET MAUI primitives, with timeline, month and agenda surfaces.
/// </summary>
/// <remarks>
/// Timeline and month views rotate three pages as a ring buffer; the agenda is one virtualized
/// vertical list. See docs/design/ for the reasoning behind each decision.
/// </remarks>
public partial class SchedulerView : ContentView
{
    private static readonly IReadOnlyCollection<DayOfWeek> DefaultWorkingDays = Array.AsReadOnly(
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ]);

    /// <summary>The slot the pager rests on, and the only one an agenda uses.</summary>
    private const int CentreSlot = 1;

    /// <summary>
    /// How many times a populate pass may re-run itself after rows corrected their height.
    /// </summary>
    /// <remarks>
    /// A correction can shorten rows and so reveal more of them, which measure in turn. Two extra
    /// passes settle every realistic case; a cap is what stops an unlucky template oscillating
    /// between two heights and hanging the frame rather than merely looking wrong.
    /// </remarks>
    private const int MaxAgendaMeasurePasses = 2;

    private const int LongPressDelayMs = 350;
    private const double DragMovementToleranceDp = 12;
    private const double TapMovementToleranceDp = 8;
    private const int EdgePagingDwellMs = 600;
    private const double EdgeScrollStepDp = 14;
    private const int EdgeScrollIntervalMs = 60;
    private const int DayCountAnimationMs = 220;
    private const string DayCountAnimationName = "SchedulerDayCount";
    private const double GhostOpacity = 0.5;
    private const double LiftedOpacity = 0.85;

    /// <summary>How often a vertical offset the platform would not take is asked for again.</summary>
    private const int TimelineScrollRetryIntervalMs = 50;

    /// <summary>How many times it is asked for again before the control lets it go.</summary>
    private const int TimelineScrollRetries = 6;

    // AbsoluteLayout paints by ZIndex first, so the stacking order is stated explicitly rather than
    // being an accident of the order children happen to be added in.
    /// <summary>Height of one hour label, and of the drag-time chip that may cover it.</summary>
    private const double GutterLabelHeight = 15;

    /// <summary>
    /// Space between an hour label and the grid.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose: a twelve-hour locale renders "10:00 AM" where a twenty-four-hour one needs
    /// only "10:00", and the default <see cref="TimeGutterWidth"/> has little to spare. A host whose
    /// users read twelve-hour time may want to widen the gutter.
    /// </remarks>
    private const double GutterLabelInset = 4;
    private const double DragIndicatorHeight = 18;

    private const int GridZIndex = 0;
    private const int SelectionZIndex = 1;
    private const int AppointmentZIndex = 2;
    private const int DraggedAppointmentZIndex = 100;

    private readonly SchedulerGeometry geometry = new();
    private readonly MonthGeometry monthGeometry = new();
    private readonly AgendaGeometry agendaGeometry = new();
    private readonly TimelineSurface timelineSurface;
    private readonly ISchedulerSurface monthSurface;
    private readonly AgendaSurface agendaSurface;
    private ISchedulerSurface pageSurface;
    private readonly SchedulerGridDrawable gridDrawable;
    private readonly MonthGridDrawable monthDrawable;
    private readonly AgendaGridDrawable agendaDrawable;

    private readonly AppointmentViewPool pool;

    /// <summary>
    /// Headings and day markers, pooled apart from the appointments they sit among.
    /// </summary>
    /// <remarks>
    /// A second pool rather than a second template on the first: a spare built as a month banner is
    /// no use to a row and vice versa, so one pool would mostly hand back the wrong shape.
    /// </remarks>
    private readonly AppointmentViewPool sectionPool;
    private readonly CellSelectionOverlay cellSelection;
    private readonly PageSlot[] slots = new PageSlot[SchedulerGeometry.SlotCount];
    private readonly Dictionary<View, ISchedulerAppointment> appointmentsByView = [];

    /// <summary>Scratch for <c>PopulateSlot</c>, reused because its passes cannot overlap.</summary>
    private readonly Dictionary<object, View> reusableByKey = [];
    private readonly List<View> reusableArrangedViews = [];

    /// <summary>Views this page kept that no position claimed, ready to be rebound where they stand.</summary>
    private readonly List<View> reusableSurplus = [];
    private readonly Dictionary<(DateTime Date, SchedulerAgendaSectionKind Kind), View> reusableSections = [];
    private readonly Dictionary<View, PageSlot> slotsByView = [];

    private readonly Grid root;
    private readonly Grid headerGrid;
    private readonly Grid headerClip;
    private readonly AbsoluteLayout headerSurface;
    private readonly Label headerCorner;
    private readonly ScrollView verticalScroll;
    private readonly PagingScrollView pagerScroll;
    private readonly AbsoluteLayout surface;

    /// <summary>
    /// The vertical scroll view's content, and the only thing above it that a transform moves.
    /// </summary>
    /// <remarks>
    /// Not <see cref="surface"/>: that is the content view of <c>PagingScrollView</c>, whose handler
    /// owns its frame, so a translation written there is silently discarded. Verified on the
    /// simulator — a forced offset on the surface moved nothing, the same offset here moved the list.
    /// </remarks>
    private readonly Grid bodyGrid;
    private readonly GraphicsView gridView;
    private readonly TimeGutter gutter;
    private readonly ActivityIndicator busyIndicator;
    private readonly AbsoluteLayout dragOverlay;
    private View? dragOverlayView;

    private IDispatcherTimer? longPressTimer;
    private IDispatcherTimer? currentTimeTimer;

    private bool recentring;
    private bool suppressDisplayDateSync;
    private bool agendaNavigationInProgress;
    private double? pendingAgendaScrollTarget;

    /// <summary>The timeline offset asked for and not yet seen applied, or null when none is owed.</summary>
    private double? pendingTimelineScrollTarget;

    /// <summary>Re-asks for an offset the platform clamped away, for a bounded number of frames.</summary>
    /// <remarks>
    /// A surface swapped in is taller than the one the platform still holds a content size for — a
    /// month's is exactly one viewport — and an offset written against the old size is clamped
    /// rather than queued. Nothing reports that: the platform stops short and says nothing further,
    /// so the request is repeated until an offset it reports is the one asked for. The budget is
    /// short and finite deliberately. Where the offset is genuinely out of reach, giving up leaves
    /// the reader where the platform put them, which is better than a surface that keeps pulling
    /// itself back for the rest of the session.
    /// </remarks>
    private IDispatcherTimer? timelineScrollRetryTimer;

    private int timelineScrollRetriesLeft;
    private VisibleDatesReportKey? lastVisibleDatesReport;
    private bool initialised;
    private bool repopulateQueued;
    private bool constructed;

    /// <summary>The day-count change currently easing into place, or null while none is.</summary>
    private DayCountTransition? dayCountTransition;

    /// <summary>Whether the day count has changed in this tick and the date may still be following it.</summary>
    private bool dayCountChangePending;

    /// <summary>Which appointment each view's accessibility description was built from.</summary>
    /// <remarks>
    /// Compared by instance, which the contract makes sufficient: an appointment is read rather than
    /// observed, so a changed one reaches the control as a new instance. What an instance comparison
    /// cannot see is that a description is resolved into <see cref="TimeZone"/> and formatted with
    /// <see cref="TimeFormat"/> — so <see cref="OnGeometryChanged"/>, which both of those arrive
    /// through, clears this.
    /// <para>
    /// It held the resolved values instead, so that a zone change was visible in the comparison. That
    /// made every realization pass resolve two times and read a subject for every row in the window
    /// in order to discover that nothing had changed, which measured as half the pass — and the pass
    /// runs on the agenda's scroll path.
    /// </para>
    /// </remarks>
    private readonly Dictionary<View, ISchedulerAppointment> describedByView = [];

    /// <summary>Whatever <see cref="ItemsSource"/> we currently hold a subscription to, if any.</summary>
    private INotifyCollectionChanged? subscribedItems;
    private bool unloaded;

    /// <summary>How deep the current populate pass has re-run itself after a height correction.</summary>
    private int agendaMeasurePasses;
    private double lastAgendaObservedTop;
    private double allocatedWidth;
    private double allocatedHeight;

    /// <summary>
    /// Defers backward range growth until the vertical scroll is quiet.
    /// </summary>
    /// <remarks>
    /// Growing backward prepends content above the viewport, which changes the anchor and so the
    /// offset — and <c>ScrollToAsync</c> writes the offset by cancelling the platform's in-flight
    /// fling, which is precisely the interruption the reader feels as scrolling up and getting
    /// stopped. Forward growth is different: it adds content below, the offset does not move, and the
    /// fling is untouched. So backward growth waits for the scroll to go quiet (it naturally does so
    /// at the loaded top) and completes then.
    /// </remarks>
    private IDispatcherTimer? agendaBackwardGrowTimer;
    private bool backwardGrowPending;

    /// <summary>
    /// Bounds how long the agenda treats its scroll events as its own after asking for an offset.
    /// </summary>
    /// <remarks>
    /// <c>ScrollToAsync</c> is applied by the handler on a later pass, and iOS reports every offset it
    /// passes through on the way. Those are the platform settling, not the reader scrolling, and
    /// nothing in the event distinguishes them — so recognising only the exact offset that was asked
    /// for leaves each intermediate one looking like a gesture. Entering an agenda from a scrolled
    /// timeline settles <em>upward</em>, which reads as a fling into the top and grows the range
    /// backwards before the reader has touched anything.
    /// <para>
    /// The window is timed rather than held open until the offset arrives, because it may never:
    /// the platform clamps the request against a content size it has not resized yet, stops short,
    /// and reports nothing further. An open-ended window would leave the agenda ignoring the reader
    /// for the rest of the session, which is a far worse failure than the one it fixes.
    /// </para>
    /// </remarks>
    private IDispatcherTimer? agendaNavigationSettleTimer;

    /// <summary>
    /// The offset a compensating shift asked for, while the platform still owes it.
    /// </summary>
    /// <remarks>
    /// Rows are reflowed and the surface regrown synchronously; the offset that pays for them is
    /// applied by the handler on a later pass. Between the two the reader is looking at content that
    /// has moved without the scroll having moved with it — a month of appointments arriving above the
    /// viewport is five thousand units of that, which reads as the list jumping and snapping back.
    /// While this is set, the difference between it and the offset the platform reports is held in
    /// <see cref="bodyGrid"/>'s translation, so the content stays exactly where the reader left it.
    /// The transform and the scroll are both applied to the platform view, so they compose in one
    /// frame rather than fighting across two.
    /// </remarks>
    private double? agendaCompensationTarget;

    /// <summary>
    /// Whether a month has been added ahead of the reader that the host has not answered yet.
    /// </summary>
    /// <remarks>
    /// Growing forward is really a request: it widens the range and asks the host, through
    /// <c>VisibleDatesChanged</c>, to fetch it. The answer arrives on a later tick, so without a gate
    /// every frame of a fling into the end asks again — and a month the host has nothing for adds
    /// only its own heading, so the position keeps asking and the range runs away, rebuilding the
    /// whole row table each time. One request at a time. Leaving the end clears it, so coming back
    /// grows again even for a host that had nothing to send.
    /// </remarks>
    private bool forwardGrowAwaitingData;

    /// <summary>The width rows were last measured at, which is the only thing that invalidates one.</summary>
    private double lastAgendaRowWidth = double.NaN;

    private View? dragView;
    private View? pressedView;
    private View? floatingView;
    private ISchedulerAppointment? floatingAppointment;
    private IDispatcherTimer? edgePagingTimer;
    private int edgePagingDirection;
    private IDispatcherTimer? edgeScrollTimer;
    private int edgeScrollDirection;
    private bool pagingDuringDrag;
    private Point lastDragPoint;
    private bool dragArmed;
    private Rect dragOriginalBounds;
    private Point dragGrabOffset;
    private Point interactionStart;
    private bool interactionMoved;
    private DateTime dragDropStart;

    /// <summary>The boundary the drag last came to rest on, or null before the first is resolved.</summary>
    private DateTime? lastDropTarget;

    public SchedulerView()
    {
        gridDrawable = new SchedulerGridDrawable(geometry);

        // Read through a delegate rather than copied in: the snap has no property-changed handler,
        // so a snapshot taken here would go stale the moment the host changed it.
        timelineSurface = new TimelineSurface(geometry, () => SlotMinutes);
        monthSurface = new MonthSurface(monthGeometry);
        agendaSurface = new AgendaSurface(agendaGeometry);
        pageSurface = timelineSurface;

        monthDrawable = new MonthGridDrawable(monthGeometry, slots);
        agendaDrawable = new AgendaGridDrawable(agendaGeometry, agendaSurface);

        gutter = new TimeGutter(geometry);
        gutter.Tapped += OnGutterTapped;

        gridView = new GraphicsView { Drawable = gridDrawable, ZIndex = GridZIndex };
        gridView.StartInteraction += OnSurfaceStartInteraction;
        gridView.DragInteraction += OnSurfaceDragInteraction;
        gridView.EndInteraction += OnSurfaceEndInteraction;
        gridView.CancelInteraction += OnSurfaceCancelInteraction;

        surface = new AbsoluteLayout();
        surface.Add(gridView);


        // Appointments never handle their own input: every touch on the surface is resolved by
        // OnSurfaceStartInteraction, which hit-tests them arithmetically.
        pool = new AppointmentViewPool(surface) { ViewCreated = view => view.InputTransparent = true };

        // Headings are chrome, not content: they never take a touch, and hit-testing must not find
        // them among the appointments it walks.
        sectionPool = new AppointmentViewPool(surface) { ViewCreated = view => view.InputTransparent = true };
        cellSelection = new CellSelectionOverlay(
            surface,
            SelectionZIndex,
            CellSelectionBackgroundColor,
            CellSelectionBorderColor,
            CellSelectionTextColor);

        pagerScroll = new PagingScrollView { Content = surface };
        pagerScroll.Scrolled += OnPagerScrolled;
        pagerScroll.PageSettled += OnPageSettled;

        // Clipped on purpose. MAUI leaves ClipChildren off on its Android layout views so shadows can
        // spill, which also means the pager is drawn without being clipped to its own bounds — and the
        // pager's content is three pages wide with an opaque background, so the page parked to the
        // left of the viewport painted straight over the hour gutter beside it.
        bodyGrid = new Grid
        {
            IsClippedToBounds = true,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            ]
        };
        // Both explicitly Top, and they must agree. Whenever the hours are shorter than the screen
        // the scroll view leaves the row taller than the content, and the two platforms distribute
        // that slack differently: Android stretches the pager and centres the gutter, iOS centres the
        // pager. Either way the hour labels drifted away from the lines they name — by half the slack
        // in one direction or the other. Pinning both to the top removes the disagreement instead of
        // matching one platform's guess.
        gutter.View.VerticalOptions = LayoutOptions.Start;
        pagerScroll.VerticalOptions = LayoutOptions.Start;

        bodyGrid.Add(gutter.View, 0);
        bodyGrid.Add(pagerScroll, 1);

        verticalScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = bodyGrid
        };

        verticalScroll.Scrolled += OnVerticalScrolled;
        // The body's arrange is the only moment its size — the content's — becomes knowable.
        verticalScroll.SizeChanged += OnBodySizeChanged;

        headerCorner = new Label
        {
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.End
        };

        // Start alignment matters: the strip is three viewports wide inside a one-viewport cell, and
        // Fill would arrange it to the cell instead of its requested width.
        headerSurface = new AbsoluteLayout
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        // The strip is the one piece of chrome that resolves a tap by arithmetic without a drawing
        // surface underneath it. Its labels are real views, so a recognizer per column would work —
        // but there are three pages of them, rebuilt whenever the day count changes, and the column
        // boundaries are already known from the geometry.
        var headerTap = new TapGestureRecognizer();
        headerTap.Tapped += OnHeaderTapped;
        headerSurface.GestureRecognizers.Add(headerTap);

        headerClip = new Grid { IsClippedToBounds = true };
        headerClip.Add(headerSurface);

        headerGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            ]
        };
        headerGrid.Add(headerCorner, 0);
        headerGrid.Add(headerClip, 1);

        for (var i = 0; i < slots.Length; i++)
            slots[i] = CreateSlot();

        busyIndicator = new ActivityIndicator
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            Margin = new Thickness(0, 12, 0, 0)
        };

        root = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            ]
        };
        // A dragged appointment is drawn here rather than on the scrolling surface. Anything inside
        // the pager moves when the pager moves, so an appointment held still by a finger would have to
        // be pushed back by every scroll delta — a correction that can only be applied after the fact,
        // which is what made edge paging jitter. Out here nothing scrolls, so there is nothing to
        // correct: the view simply stays where it was last put.
        dragOverlay = new AbsoluteLayout { InputTransparent = true };

        root.Add(headerGrid, 0, 0);
        root.Add(verticalScroll, 0, 1);
        root.Add(busyIndicator, 0, 1);
        root.Add(dragOverlay, 0, 0);
        Grid.SetRowSpan(dragOverlay, 2);

        Content = root;

        constructed = true;
        ApplyAppearance();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }


    /// <summary>The geometry of whichever surface is showing. Shared measurements only.</summary>
    private PageGeometry ActiveGeometry => ViewMode switch
    {
        SchedulerViewMode.Month => monthGeometry,
        SchedulerViewMode.Timeline => geometry,
        SchedulerViewMode.Agenda => agendaGeometry,
        _ => throw new NotSupportedException($"{ViewMode} has no geometry."),
    };

    /// <summary>
    /// The template the pool is filled from. A month falls back to the timeline's when the host has
    /// not supplied one, which renders badly but renders.
    /// </summary>
    private DataTemplate? ActiveTemplate => ViewMode switch
    {
        SchedulerViewMode.Month => MonthAppointmentTemplate ?? AppointmentTemplate,
        SchedulerViewMode.Timeline => AppointmentTemplate,
        SchedulerViewMode.Agenda => AgendaAppointmentTemplate ?? AppointmentTemplate,
        _ => throw new NotSupportedException($"{ViewMode} has no appointment template."),
    };

    private bool DraggingEnabled => AllowDragAndDrop && ViewMode is SchedulerViewMode.Timeline;

    /// <summary>
    /// Scrolls the timeline so that <paramref name="time"/> is near the top of the viewport.
    /// Timeline only: no other surface lays its content out against a time axis.
    /// </summary>
    public void ScrollToTime(TimeSpan time)
    {
        if (ViewMode is not SchedulerViewMode.Timeline)
            return;

        // Clamped here rather than left to the platform: hours shorter than the screen put the later
        // ones out of reach, and a request that can never be satisfied would be re-asked for until
        // the retries ran out before settling where the platform had put it anyway.
        var furthest = Math.Max(0, geometry.ContentHeight - geometry.ViewportHeight);

        RequestTimelineScroll(Math.Clamp(geometry.YFromMinutes(time.TotalMinutes), 0, furthest));
    }

    /// <summary>Asks for a vertical offset, and keeps asking while the platform cannot take it.</summary>
    private void RequestTimelineScroll(double target)
    {
        pendingTimelineScrollTarget = target;
        timelineScrollRetriesLeft = TimelineScrollRetries;

        _ = verticalScroll.ScrollToAsync(0, target, false);

        // Where the request was applied outright there is nothing to wait for. The scroll event the
        // platform raises on its way may already have ended this one, which is why the offset is
        // read back rather than the pending target.
        if (Math.Abs(verticalScroll.ScrollY - target) < 0.5)
        {
            EndTimelineScroll();
            return;
        }

        timelineScrollRetryTimer?.Stop();
        timelineScrollRetryTimer?.Start();
    }

    private void OnTimelineScrollRetryTicked(object? sender, EventArgs e)
    {
        if (pendingTimelineScrollTarget is not { } target || ViewMode is not SchedulerViewMode.Timeline)
        {
            EndTimelineScroll();
            return;
        }

        if (Math.Abs(verticalScroll.ScrollY - target) < 0.5 || --timelineScrollRetriesLeft <= 0)
        {
            EndTimelineScroll();
            return;
        }

        _ = verticalScroll.ScrollToAsync(0, target, false);
    }

    /// <summary>Stops owing an offset, whether it arrived or was given up on.</summary>
    private void EndTimelineScroll()
    {
        pendingTimelineScrollTarget = null;
        timelineScrollRetryTimer?.Stop();
    }

    /// <summary>Puts the timeline's opening time at the top of the viewport, as far as it reaches.</summary>
    /// <remarks>
    /// Runs whenever the timeline appears rather than only on load. The three surfaces share one
    /// vertical scroll, and a month's content is exactly one viewport tall, so the offset a reader
    /// left the timeline at does not survive the visit — it is clamped away while the other surface
    /// is showing, and coming back to the top of the day window is not where anybody was.
    /// </remarks>
    private void ScrollToOpeningAnchor()
    {
        if (ViewMode is not SchedulerViewMode.Timeline)
            return;

        if (InitialScrollTime is { } opening)
        {
            ScrollToTime(opening.ToTimeSpan());
            return;
        }

        // Open on the current time rather than at StartHour, like most calendars do.
        var now = NowInZone();

        if (now.TimeOfDay.TotalMinutes > geometry.WindowStartMinutes)
            ScrollToTime(now.TimeOfDay - TimeSpan.FromHours(1));
    }

    /// <summary>Brings a date into view on the active surface.</summary>
    /// <remarks>
    /// On a timeline or month this is the programmatic counterpart to changing
    /// <see cref="DisplayDate"/>. In an agenda it additionally places the first appointment on or
    /// after that date at the top of the viewport; an empty day therefore advances to the next
    /// meaningful row rather than manufacturing an empty one.
    /// </remarks>
    public void ScrollToDate(DateTime date)
    {
        DisplayDate = date;

        if (ViewMode is SchedulerViewMode.Agenda && initialised)
            ScrollAgendaToDate(DateOnly.FromDateTime(date));
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        unloaded = false;
        SyncItemsSubscription();

        // The collection may have been changed while we were detached from it.
        QueueRepopulate();

        ConfigurePlatformScrolling();

        currentTimeTimer = Dispatcher.CreateTimer();
        currentTimeTimer.Interval = TimeSpan.FromMinutes(1);
        currentTimeTimer.Tick += OnCurrentTimeTick;
        currentTimeTimer.Start();

        agendaBackwardGrowTimer = Dispatcher.CreateTimer();
        agendaBackwardGrowTimer.Interval = TimeSpan.FromMilliseconds(200);
        agendaBackwardGrowTimer.Tick += OnAgendaBackwardGrowTicked;

        agendaNavigationSettleTimer = Dispatcher.CreateTimer();
        agendaNavigationSettleTimer.Interval = TimeSpan.FromMilliseconds(250);
        agendaNavigationSettleTimer.Tick += OnAgendaNavigationSettled;

        timelineScrollRetryTimer = Dispatcher.CreateTimer();
        timelineScrollRetryTimer.Interval = TimeSpan.FromMilliseconds(TimelineScrollRetryIntervalMs);
        timelineScrollRetryTimer.IsRepeating = true;
        timelineScrollRetryTimer.Tick += OnTimelineScrollRetryTicked;

        ScrollToOpeningAnchor();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        unloaded = true;
        SyncItemsSubscription();

        currentTimeTimer?.Stop();
        currentTimeTimer = null;
        longPressTimer?.Stop();
        longPressTimer = null;
        agendaBackwardGrowTimer?.Stop();
        agendaNavigationSettleTimer?.Stop();
        EndTimelineScroll();
        timelineScrollRetryTimer = null;
    }

    private void OnCurrentTimeTick(object? sender, EventArgs e)
    {
        var previousDate = ActiveGeometry.Now.Date;
        ActiveGeometry.Now = NowInZone();
        gridView.Invalidate();

        // The drawable re-evaluates "today" every time it repaints, but the day headers are real
        // labels that are only rewritten when a slot is rebuilt. Left alone they would keep marking
        // yesterday until the next swipe. Only the timeline has such labels — a month's day numbers
        // are painted, and a surface without a header strip has none at all.
        if (previousDate == ActiveGeometry.Now.Date || ViewMode is not SchedulerViewMode.Timeline)
            return;

        for (var i = 0; i < slots.Length; i++)
            UpdateSlotHeader(slots[i], i);
    }

    private DateTime NowInZone() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

    /// <summary>Wraps a grid position for reporting, so a host can read it either way it needs to.</summary>
    private SchedulerMoment Moment(DateTime wallClock) => new(wallClock, TimeZone);

    /// <summary>
    /// The width the content was arranged into, which is not the width the control was allocated:
    /// padding and a platform inset both narrow the content without narrowing the control
    /// (<c>docs/design/grid-and-scrolling.md</c> §8).
    /// </summary>
    /// <remarks>
    /// Read off the body, which is what the columns must fit inside. It reports -1 until a platform
    /// has arranged it, so the headless tests fall back to the padding — the half of this the
    /// control can work out for itself.
    /// </remarks>
    private double ContentWidth => verticalScroll.Width > 0
        ? verticalScroll.Width
        : Math.Max(0, allocatedWidth - Padding.HorizontalThickness);

    /// <summary>
    /// How much of a page is on screen. The body is exactly that — the content less the header row —
    /// so it is read rather than derived from the control's height, which the same two things
    /// overstate. See <see cref="ContentWidth"/>.
    /// </summary>
    /// <remarks>The fallback subtracts the nominal header height; the body reflects the measured one.</remarks>
    private double BodyHeight => verticalScroll.Height > 0
        ? verticalScroll.Height
        : Math.Max(0, allocatedHeight - Padding.VerticalThickness - ActiveHeaderHeight);

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
            return;

        allocatedWidth = width;
        allocatedHeight = height;

        UpdateViewport();
    }

    private void OnBodySizeChanged(object? sender, EventArgs e) => UpdateViewport();

    /// <summary>Settles the geometry against the content's size, and applies it if either axis moved.</summary>
    private void UpdateViewport()
    {
        if (allocatedWidth <= 0)
            return;

        var viewport = Math.Max(0, ContentWidth - ActiveGutterWidth);
        var viewportHeight = BodyHeight;

        // Both axes, not just the width. A month is exactly one viewport tall, so its content height
        // is a function of this value — and a height-only reallocation used to update the geometry
        // and then return without applying it, leaving the surface sized for whatever the control was
        // allocated first. It only ever surfaced by a dozen pixels or so, as a month that could still
        // be scrolled a little, because a timeline measures its height in hours and does not care
        // what the viewport is.
        var unchanged = initialised
            && Math.Abs(viewport - ActiveGeometry.ViewportWidth) < 0.5
            && Math.Abs(viewportHeight - ActiveGeometry.ViewportHeight) < 0.5;

        ActiveGeometry.ViewportWidth = viewport;
        ActiveGeometry.ViewportHeight = viewportHeight;

        if (unchanged)
            return;

        ApplyGeometry();
    }

    private static void OnGeometryChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        // TimeZone and TimeFormat arrive here, and both change what a description says without
        // changing the appointment it was built from. Deliberately not in ApplyGeometry: that also
        // runs for every size reallocation, and a resize leaves every description still correct.
        view.describedByView.Clear();
        view.ApplyGeometry();
    }

    private static void OnVisibleDaysChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerView)bindable).ChangeVisibleDays((int)oldValue, (int)newValue);

    /// <summary>
    /// Applies a new day count, growing or shrinking the columns into place rather than cutting.
    /// </summary>
    /// <remarks>
    /// The page is laid out for the new count first, then the column width is animated from the old
    /// one to the new. Everything measures through <see cref="SchedulerGeometry.DayWidth"/>, so the
    /// grid, the appointments and the day headers move together for free.
    /// </remarks>
    private void ChangeVisibleDays(int oldDays, int newDays)
    {
        // Only a timeline is made of a number of days the host chose. The new count is picked up by
        // ApplyTimelineChrome whenever the timeline comes back.
        if (ViewMode is not SchedulerViewMode.Timeline)
            return;

        // A host opening a day writes the date too, and the two land one after the other in some
        // order. Marked before the guards below rather than left to the presence of an animation:
        // the transition is skipped on a control with no handler or not yet loaded, and what a host
        // sees must not depend on that. Cleared on the next tick, so only writes belonging to the
        // same batch are taken as part of the same intent.
        if (!dayCountChangePending)
        {
            dayCountChangePending = true;
            Dispatcher.Dispatch(() => dayCountChangePending = false);
        }

        var previousDayCount = Math.Clamp(oldDays, 1, 7);
        var previousDayWidth = geometry.ViewportWidth / previousDayCount;
        var previousPageStart = slots[1].PageStart;

        ApplyGeometry();

        var newDayWidth = geometry.ViewportWidth / Math.Clamp(newDays, 1, 7);

        // Nothing to animate before there is a platform to animate on. Without a handler there is no
        // animation ticker either, and a transition that starts but never advances would leave the
        // column width pinned at its first frame — which is the old width, so the new day count would
        // be laid out at the old size and stay there.
        if (Handler is null || !IsLoaded || geometry.ViewportWidth <= 0
            || Math.Abs(previousDayWidth - newDayWidth) < 0.5)
        {
            return;
        }

        // Aborting runs the previous transition's finished handler, which clears the field — so the
        // one being started here is put in place afterwards.
        this.AbortAnimation(DayCountAnimationName);

        var transition = new DayCountTransition(
            previousDayWidth, newDayWidth, previousPageStart, previousDayCount);

        transition.AimAt(slots[CentreSlot].PageStart, geometry.VisibleDays);
        dayCountTransition = transition;

        new Animation(
            progress =>
            {
                transition.Progress = progress;
                geometry.DayWidthOverride = transition.DayWidth;
                geometry.AnimationOffsetX = transition.OffsetX;
                ApplyDayWidth();
            },
            0,
            1,
            Easing.CubicInOut)
            .Commit(this, DayCountAnimationName, length: DayCountAnimationMs, finished: (_, _) =>
            {
                dayCountTransition = null;
                geometry.DayWidthOverride = null;
                geometry.AnimationOffsetX = 0;
                ApplyDayWidth();
            });
    }

    /// <summary>Re-places what is already laid out at the current column width, without re-laying it out.</summary>
    private void ApplyDayWidth()
    {
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];

            for (var v = 0; v < slot.Views.Count && v < slot.Positions.Count; v++)
                PositionAppointmentView(slot.Views[v], slot.Positions[v], i);

            AbsoluteLayout.SetLayoutBounds(slot.Header, new Rect(0, 0, geometry.PageSpan, HeaderHeight));
            slot.Header.TranslationX = i * geometry.PageSpan + geometry.AnimationOffsetX;
        }

        // The selected-cell affordance is anchored to a column like everything else, so it widens and
        // travels with the day it marks rather than sitting still while the grid moves under it.
        UpdateSelectionView();

        gridView.Invalidate();
    }


    /// <summary>
    /// The height the day-header strip takes, which is not a given: a surface may have no strip.
    /// </summary>
    private double ActiveHeaderHeight => ViewMode is SchedulerViewMode.Agenda ? 0 : HeaderHeight;

    /// <summary>Only the timeline sets its hours aside a gutter; every other surface reaches the edge.</summary>
    private double ActiveGutterWidth => ViewMode is SchedulerViewMode.Timeline ? TimeGutterWidth : 0;

    private void ApplyGeometry()
    {
        var active = ActiveGeometry;

        active.FirstDayOfWeek = FirstDayOfWeek;
        active.TimeZone = TimeZone;
        active.Now = NowInZone();
        active.ViewportHeight = BodyHeight;

        switch (ViewMode)
        {
            case SchedulerViewMode.Month: ApplyMonthChrome(); break;
            case SchedulerViewMode.Timeline: ApplyTimelineChrome(); break;
            case SchedulerViewMode.Agenda: ApplyAgendaChrome(); break;
            default: throw new NotSupportedException($"{ViewMode} has no chrome.");
        }

        // The row table is derived from geometry, so any application makes it stale. Its measurements
        // are not: they are cached by appointment key, and only the width a row was measured at can
        // make one wrong. Clearing them for every application — an hour height, a size reallocation —
        // would have every pass re-measure the screen, which is the cost the cache exists to avoid.
        // Read after the chrome, which is what settles the gutter the column is inset past.
        var rowWidth = agendaGeometry.RowWidth;

        agendaSurface.Invalidate(
            clearMeasurements: ViewMode is SchedulerViewMode.Agenda
                && !(Math.Abs(rowWidth - lastAgendaRowWidth) < 0.5));

        if (ViewMode is SchedulerViewMode.Agenda)
            lastAgendaRowWidth = rowWidth;

        if (active.ViewportWidth <= 0)
            return;

        pagerScroll.PageWidth = active.ViewportWidth;

        surface.WidthRequest = active.SurfaceWidth;
        surface.HeightRequest = active.ContentHeight;
        AbsoluteLayout.SetLayoutBounds(gridView, new Rect(0, 0, active.SurfaceWidth, active.ContentHeight));

        headerSurface.WidthRequest = active.SurfaceWidth;

        for (var i = 0; i < slots.Length; i++)
        {
            if (!SlotHeaderMatchesMode(slots[i]))
                BuildSlotHeader(slots[i]);

            AbsoluteLayout.SetLayoutBounds(slots[i].Header, new Rect(0, 0, active.ViewportWidth, HeaderHeight));
        }

        var firstLayout = !initialised;

        // For the agenda, re-derive the page from DisplayDate only once, on first layout. After that
        // the page is wherever navigation put it; re-anchoring it to DisplayDate here would reset the
        // loaded range under a live scroll — DisplayDate tracks the viewport as the reader scrolls, so
        // every size change would collapse the range the reader just grew, shifting headings. The
        // other surfaces derive their page from DisplayDate on every application.
        var page = ViewMode is SchedulerViewMode.Agenda && initialised
            ? slots[1].PageStart
            : pageSurface.StartOfPage(DateOnly.FromDateTime(DisplayDate));
        RebuildAll(page);
        initialised = true;

        if (firstLayout && ViewMode is SchedulerViewMode.Agenda)
            ScrollAgendaToDate(DateOnly.FromDateTime(DisplayDate));
    }

    private void ApplyTimelineChrome()
    {
        headerGrid.IsVisible = true;
        pagerScroll.IsScrollEnabled = true;

        geometry.HourHeight = HourHeight;
        geometry.VisibleDays = Math.Clamp(VisibleDays, 1, 7);
        geometry.StartHour = StartHour;
        geometry.EndHour = EndHour;

        gridView.Drawable = gridDrawable;

        gutter.Update(TimeGutterWidth, TimeFormat, GridBackgroundColor, SecondaryTextColor);

        headerCorner.WidthRequest = TimeGutterWidth;
        headerCorner.Text = TimeZoneAbbreviation();

        headerClip.HeightRequest = HeaderHeight;
        headerSurface.HeightRequest = HeaderHeight;
    }

    /// <summary>
    /// Moves the agenda's window of realized rows to follow the scroll.
    /// </summary>
    /// <remarks>
    /// Not routed through <c>QueueRepopulate</c>. Data-driven rebuilds are coalesced onto the next
    /// tick because a burst of them should cost one pass; this is gesture-driven, and a pass arriving
    /// a tick late is a screen of blank rows during a fling.
    /// <para>
    /// The gate is distance rather than time. A pass costs what is on screen, so running it every
    /// frame is waste — but running it only every half-overscan means the rows are realized well
    /// before they are reached.
    /// </para>
    /// </remarks>
    internal void OnVerticalScrolled(object? sender, ScrolledEventArgs e)
    {
        // The offset asked for has arrived, so there is nothing left to re-ask for. Read before the
        // agenda's gate: the timeline scrolls too, and this is the only report either of them gets.
        if (pendingTimelineScrollTarget is { } requestedTop && Math.Abs(e.ScrollY - requestedTop) < 0.5)
            EndTimelineScroll();

        if (ViewMode is not SchedulerViewMode.Agenda || !initialised)
            return;

        var direction = e.ScrollY - lastAgendaObservedTop;
        lastAgendaObservedTop = e.ScrollY;
        var reachedRequestedOffset = pendingAgendaScrollTarget is { } requested
            && Math.Abs(e.ScrollY - requested) < 0.5;
        var programmatic = agendaNavigationInProgress || reachedRequestedOffset;

        if (reachedRequestedOffset)
            EndAgendaNavigation();

        // The platform has moved, so what it still owes a compensating shift has changed with it.
        UpdateAgendaCompensation(e.ScrollY);

        // DisplayDate is public viewport state, not realization state. Keep it current even when
        // this scroll delta is too small to justify rebuilding the overscanned view window.
        if (!programmatic)
            SyncAgendaDisplayDate(e.ScrollY);

        // Where the viewport is, separately from whether this event may act on it: the position is
        // what arms and disarms both gates, and it does so whoever moved the scroll.
        var nearEnd = e.ScrollY + agendaGeometry.ViewportHeight + agendaGeometry.Overscan
            >= agendaGeometry.ContentHeight - agendaGeometry.Overscan;
        var nearStart = e.ScrollY <= agendaGeometry.Overscan;

        if (!nearEnd)
            forwardGrowAwaitingData = false;

        var growForward = nearEnd && !programmatic && direction > 0 && !forwardGrowAwaitingData;
        var growBackward = nearStart && !programmatic && direction < 0;

        // Only a viewport that has actually left the top cancels a deferred backward growth. A fling
        // into the top settles by springing back, which arrives as a positive delta at an offset still
        // within the overscan — the platform settling, not the reader moving away — and cancelling on
        // it would drop the growth the fling had just asked for.
        if (!nearStart)
        {
            backwardGrowPending = false;
            agendaBackwardGrowTimer?.Stop();
        }

        if (!growForward && !growBackward
            && Math.Abs(e.ScrollY - agendaGeometry.VisibleTop) < Math.Max(1, agendaGeometry.Overscan / 2))
        {
            return;
        }

        agendaGeometry.VisibleTop = e.ScrollY;

        if (growForward)
        {
            forwardGrowAwaitingData = true;
            agendaSurface.GrowForward(slots[CentreSlot].PageStart);

            PopulateSlot(slots[CentreSlot], CentreSlot);
            RaiseVisibleDatesChanged();
            gridView.Invalidate();
            return;
        }

        if (growBackward)
        {
            // Deferred: growing backward changes the offset, and writing it mid-fling is what stops
            // the reader. Wait for the scroll to go quiet, then grow.
            backwardGrowPending = true;
            agendaBackwardGrowTimer?.Stop();
            agendaBackwardGrowTimer?.Start();
        }

        // Realize the current window over the table as it stands (backward growth has not rebuilt it
        // yet), so the scroll keeps rows under the reader without stalling on the deferred rebuild.
        PopulateSlot(slots[CentreSlot], CentreSlot);
        gridView.Invalidate();
    }

    /// <summary>Completes a deferred backward growth once the vertical scroll has been quiet.</summary>
    private void OnAgendaBackwardGrowTicked(object? sender, EventArgs e)
    {
        agendaBackwardGrowTimer?.Stop();

        if (!backwardGrowPending)
            return;

        backwardGrowPending = false;

        // The reader may have moved away from the top while the timer was running, or left the mode.
        if (ViewMode is not SchedulerViewMode.Agenda || !initialised)
            return;

        if (agendaGeometry.VisibleTop > agendaGeometry.Overscan)
            return;

        agendaSurface.GrowBackward(slots[CentreSlot].PageStart);

        PopulateSlot(slots[CentreSlot], CentreSlot);
        RaiseVisibleDatesChanged();
        gridView.Invalidate();
    }

    /// <summary>Publishes the date at the agenda's viewport anchor without navigating back to it.</summary>
    private void SyncAgendaDisplayDate(double visibleTop)
    {
        var date = agendaSurface.DateAt(visibleTop);

        if (date is null || DateOnly.FromDateTime(DisplayDate) == date)
            return;

        suppressDisplayDateSync = true;

        try
        {
            DisplayDate = date.Value.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            suppressDisplayDateSync = false;
        }
    }

    /// <summary>
    /// Writes the surface's height through, once the rows have said what it is.
    /// </summary>
    /// <remarks>
    /// ApplyGeometry sets this before laying anything out, which is all a timeline or a month needs —
    /// their heights are formulas. An agenda's is not known until its rows exist, and changes again
    /// as they measure, so it is written again here. Guarded: an unchanged write still costs a
    /// layout pass, and this runs on the scroll path.
    /// </remarks>
    private void ApplyContentExtent()
    {
        var height = ActiveGeometry.ContentHeight;

        if (Math.Abs(surface.HeightRequest - height) < 0.5)
            return;

        surface.HeightRequest = height;
        AbsoluteLayout.SetLayoutBounds(gridView, new Rect(0, 0, ActiveGeometry.SurfaceWidth, height));
    }

    /// <summary>
    /// Absorbs a change in the height of content above the viewport into the scroll offset.
    /// </summary>
    /// <remarks>
    /// Without this, a row above the fold measuring taller than its estimate pushes everything the
    /// reader is looking at downwards. The same operation is what growing the range backwards will
    /// need, which is why it is a method rather than two lines inline.
    /// <para>
    /// The write goes through <c>ScrollToAsync</c>, which MAUI applies on a later pass — so this
    /// corrects a visible jump rather than preventing one outright. It is deliberately not attempted
    /// mid-gesture; settling at rest reads better than fighting the platform for the same frame.
    /// </para>
    /// </remarks>
    private void ShiftContentAbove(double delta)
    {
        if (Math.Abs(delta) < 0.5)
            return;

        // Geometry is the source of truth during a correction. A programmatic navigation updates it
        // before the platform has necessarily applied the requested offset, while an ordinary
        // gesture keeps it equal to ScrollY through OnVerticalScrolled.
        var target = Math.Max(0, agendaGeometry.VisibleTop + delta);
        agendaGeometry.VisibleTop = target;
        lastAgendaObservedTop = target;
        pendingAgendaScrollTarget = target;

        // Hold the content where it is until the platform has caught up, and give the settle timer
        // the job of letting go if it never does — a translation left behind would be a permanent
        // visual offset, which is worse than the jump it prevents.
        agendaCompensationTarget = target;
        UpdateAgendaCompensation(verticalScroll.ScrollY);

        agendaNavigationSettleTimer?.Stop();
        agendaNavigationSettleTimer?.Start();

        _ = verticalScroll.ScrollToAsync(0, target, false);
    }

    /// <summary>
    /// Translates the surface by whatever the platform still owes a compensating shift.
    /// </summary>
    /// <remarks>
    /// Set from the scroll event, so the translation is released in the same frame the platform
    /// reports the offset it was standing in for.
    /// </remarks>
    private void UpdateAgendaCompensation(double scrollY)
    {
        var owed = agendaCompensationTarget is { } target ? target - scrollY : 0;

        if (Math.Abs(owed) < 0.5)
        {
            agendaCompensationTarget = null;
            owed = 0;
        }

        if (Math.Abs(bodyGrid.TranslationY + owed) >= 0.5)
            bodyGrid.TranslationY = -owed;
    }

    /// <summary>Drops any held translation, for a deliberate move or a change of surface.</summary>
    private void ReleaseAgendaCompensation()
    {
        agendaCompensationTarget = null;

        if (Math.Abs(bodyGrid.TranslationY) >= 0.5)
            bodyGrid.TranslationY = 0;
    }

    /// <summary>Places the agenda's first meaningful row on or after a date at the top.</summary>
    private void ScrollAgendaToDate(DateOnly date)
    {
        agendaNavigationInProgress = true;

        // A navigation moves the reader on purpose; there is nothing to hold still for it.
        ReleaseAgendaCompensation();

        // ItemsSource can change synchronously while VisibleDatesChanged is being delivered. Refresh
        // a dirty table before resolving the offset so navigation uses the data the host just sent.
        PopulateSlot(slots[CentreSlot], CentreSlot);

        var maximum = Math.Max(0, agendaGeometry.ContentHeight - agendaGeometry.ViewportHeight);
        var target = Math.Clamp(agendaSurface.OffsetFor(date), 0, maximum);

        agendaGeometry.VisibleTop = target;
        lastAgendaObservedTop = target;
        pendingAgendaScrollTarget = target;
        _ = verticalScroll.ScrollToAsync(0, target, false);

        PopulateSlot(slots[CentreSlot], CentreSlot);

        // The offset is not the platform's yet. Where it applied the request outright there is
        // nothing left to wait for; otherwise stay in navigation until one of the offsets it reports
        // is the one asked for, or the settle window closes.
        if (Math.Abs(verticalScroll.ScrollY - target) < 0.5)
        {
            EndAgendaNavigation();
        }
        else
        {
            agendaNavigationSettleTimer?.Stop();
            agendaNavigationSettleTimer?.Start();
        }

        gridView.Invalidate();
    }

    /// <summary>Hands scroll events back to the reader, the requested offset having arrived or not.</summary>
    private void EndAgendaNavigation()
    {
        agendaNavigationInProgress = false;
        pendingAgendaScrollTarget = null;
        agendaNavigationSettleTimer?.Stop();
    }

    /// <summary>Closes the settle window when the platform never reported the offset asked for.</summary>
    private void OnAgendaNavigationSettled(object? sender, EventArgs e)
    {
        EndAgendaNavigation();
        ReleaseAgendaCompensation();

        // Wherever it actually stopped is where the reader is now. Left at the offset that was asked
        // for, the next event's direction would be measured against somewhere the list never went —
        // which is the same misreading, one gesture later.
        lastAgendaObservedTop = verticalScroll.ScrollY;
    }

    private DataTemplate ActiveSectionTemplate =>
        AgendaSectionTemplate ?? new DataTemplate(() => new AgendaSectionView());

    /// <summary>
    /// An agenda has no strip of day columns and no gutter of hours, and does not page sideways.
    /// </summary>
    /// <remarks>
    /// The header grid is collapsed whole rather than zeroed piecemeal: an empty label in an
    /// auto-sized row is not reliably nothing on both platforms. The pager is left in place but told
    /// not to scroll, since the surface is now exactly one page wide and there is nowhere to go.
    /// </remarks>
    private void ApplyAgendaChrome()
    {
        agendaGeometry.EstimatedRowHeight = AgendaEstimatedRowHeight;
        agendaGeometry.DayGutterWidth = AgendaDayGutterWidth;

        gridView.Drawable = agendaDrawable;

        gutter.Hide();
        headerCorner.WidthRequest = 0;
        headerCorner.Text = string.Empty;
        headerGrid.IsVisible = false;

        pagerScroll.IsScrollEnabled = false;
    }

    private void ApplyMonthChrome()
    {
        headerGrid.IsVisible = true;
        pagerScroll.IsScrollEnabled = true;

        monthDrawable.OverflowFormat = MonthOverflowFormat;

        gridView.Drawable = monthDrawable;

        // Collapsed rather than merely blank: both live in Auto-sized cells, so a width of zero is
        // what actually gives the month the full width of the control.
        gutter.Hide();
        headerCorner.WidthRequest = 0;
        headerCorner.Text = string.Empty;

        headerClip.HeightRequest = HeaderHeight;
        headerSurface.HeightRequest = HeaderHeight;
    }

    /// <summary>Applies paint-only state without touching appointment layout or binding.</summary>
    private void ApplyAppearance()
    {
        if (!constructed)
            return;

        gridView.BackgroundColor = GridBackgroundColor;
        headerCorner.TextColor = SecondaryTextColor;

        gridDrawable.GridLineColor = GridLineColor;
        gridDrawable.MinorGridLineColor = MinorGridLineColor;
        gridDrawable.MinorGridLineMinutes = MinorGridLineMinutes;
        gridDrawable.NonWorkingDaysBackgroundColor = NonWorkingDaysBackgroundColor;
        gridDrawable.NonWorkingHoursBackgroundColor = NonWorkingHoursBackgroundColor;
        gridDrawable.CurrentDayBackgroundColor = CurrentDayBackgroundColor;
        gridDrawable.CurrentTimeIndicatorColor = CurrentTimeIndicatorColor;
        gridDrawable.ShowNonWorkingDaysShading = ShowNonWorkingDaysShading;
        gridDrawable.ShowCurrentDayHighlight = ShowCurrentDayHighlight;
        gridDrawable.ShowNonWorkingHoursShading = ShowNonWorkingHoursShading;
        gridDrawable.WorkingDays = WorkingDays;
        gridDrawable.WorkingHoursStart = WorkingHoursStart;
        gridDrawable.WorkingHoursEnd = WorkingHoursEnd;

        monthDrawable.GridLineColor = GridLineColor;
        monthDrawable.NonWorkingDaysBackgroundColor = NonWorkingDaysBackgroundColor;
        monthDrawable.AdjacentMonthBackgroundColor = AdjacentMonthBackgroundColor;
        monthDrawable.CurrentDayBackgroundColor = CurrentDayBackgroundColor;
        monthDrawable.DayNumberColor = PrimaryTextColor;
        monthDrawable.AdjacentMonthDayNumberColor = AdjacentMonthTextColor;
        monthDrawable.CurrentDayTextColor = CurrentDayTextColor;
        monthDrawable.OverflowTextColor = SecondaryTextColor;
        monthDrawable.ShowNonWorkingDaysShading = ShowNonWorkingDaysShading;
        monthDrawable.ShowCurrentDayHighlight = ShowCurrentDayHighlight;
        monthDrawable.WorkingDays = WorkingDays;
        monthDrawable.OverflowFormat = MonthOverflowFormat;

        gutter.UpdateAppearance(
            GridBackgroundColor,
            SecondaryTextColor,
            DragTimeIndicatorBackgroundColor,
            DragTimeIndicatorTextColor);

        agendaDrawable.CurrentDayBackgroundColor = CurrentDayBackgroundColor;
        agendaDrawable.ShowCurrentDayHighlight = ShowCurrentDayHighlight;

        foreach (var slot in slots)
        {
            foreach (var view in slot.SectionViews)
            {
                if (view is AgendaSectionView section)
                    section.UpdateAppearance(PrimaryTextColor, SecondaryTextColor);
            }
        }

        cellSelection.UpdateAppearance(
            CellSelectionBackgroundColor,
            CellSelectionBorderColor,
            CellSelectionTextColor);

        busyIndicator.Color = BusyIndicatorColor;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].DayNameLabels.Length > 0)
                UpdateSlotHeader(slots[i], i);
        }

        gridView.Invalidate();
    }

    /// <summary>
    /// Swaps the surface, and everything that belongs to the one being left behind.
    /// </summary>
    /// <remarks>
    /// The modes do not share a template or a geometry, so every placed view goes back to the pool
    /// and the pool itself is emptied — a chip, an appointment box and an agenda row are different
    /// templates, and a spare built from one is no use to another.
    /// </remarks>
    private void ChangeViewMode(SchedulerViewMode mode)
    {
        CancelDragCandidate();

        foreach (var slot in slots)
            ReleaseSlot(slot);

        pool.Clear();

        foreach (var slot in slots)
            ReleaseSectionViews(slot);

        sectionPool.Clear();
        sectionPool.Template = ActiveSectionTemplate;
        agendaSurface.Invalidate(clearMeasurements: true);
        lastAgendaRowWidth = double.NaN;
        forwardGrowAwaitingData = false;
        backwardGrowPending = false;
        agendaBackwardGrowTimer?.Stop();
        EndAgendaNavigation();
        ReleaseAgendaCompensation();
        EndTimelineScroll();

        if (dragOverlayView is not null)
        {
            dragOverlay.Remove(dragOverlayView);
            dragOverlayView = null;
        }

        pageSurface = mode switch
        {
            SchedulerViewMode.Month => monthSurface,
            SchedulerViewMode.Timeline => timelineSurface,
            SchedulerViewMode.Agenda => agendaSurface,
            _ => throw new NotSupportedException($"{mode} has no surface."),
        };
        pool.Template = ActiveTemplate;
        cellSelection.Reset();

        // The gutter appears or disappears with the mode, so the viewport is a different width now.
        // Nothing re-allocates the control's size, so this is the only place that would notice.
        ActiveGeometry.ViewportWidth = Math.Max(0, ContentWidth - ActiveGutterWidth);

        ApplyGeometry();

        if (mode is SchedulerViewMode.Agenda && initialised)
            ScrollAgendaToDate(DateOnly.FromDateTime(DisplayDate));
        else if (mode is SchedulerViewMode.Timeline && initialised)
            ScrollToOpeningAnchor();
    }

    private string TimeZoneAbbreviation()
    {
        var offset = TimeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return offset.Minutes == 0
            ? $"GMT{sign}{Math.Abs(offset.Hours)}"
            : $"GMT{sign}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):00}";
    }

    private readonly record struct VisibleDatesReportKey(
        SchedulerViewMode Mode,
        DateOnly VisibleFirst,
        DateOnly VisibleLast,
        int VisibleCount,
        DateOnly PrefetchFirst,
        DateOnly PrefetchLast);
}
