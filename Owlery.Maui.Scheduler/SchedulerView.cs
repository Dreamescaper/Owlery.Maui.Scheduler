using System.Collections.Specialized;
using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>
/// A Google-Calendar-style timeline built from plain .NET MAUI primitives, showing a week, three days
/// or a single day depending on <see cref="VisibleDays"/>.
/// </summary>
/// <remarks>
/// Three pages are rendered at all times and rotated as a ring buffer, so swiping is infinite
/// while only one page is ever rebuilt. See DESIGN.md for the reasoning behind each decision.
/// </remarks>
public partial class SchedulerView : ContentView
{
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
    private readonly ISchedulerSurface timelineSurface;
    private readonly ISchedulerSurface monthSurface;
    private ISchedulerSurface pageSurface;
    private readonly SchedulerGridDrawable gridDrawable;
    private readonly MonthGridDrawable monthDrawable;

    private readonly AppointmentViewPool pool;
    private readonly CellSelectionOverlay cellSelection;
    private readonly PageSlot[] slots = new PageSlot[SchedulerGeometry.SlotCount];
    private readonly Dictionary<View, ISchedulerAppointment> appointmentsByView = [];
    private readonly Dictionary<View, PageSlot> slotsByView = [];

    private readonly Grid root;
    private readonly Grid headerClip;
    private readonly AbsoluteLayout headerSurface;
    private readonly Label headerCorner;
    private readonly ScrollView verticalScroll;
    private readonly PagingScrollView pagerScroll;
    private readonly AbsoluteLayout surface;
    private readonly GraphicsView gridView;
    private readonly TimeGutter gutter;
    private readonly ActivityIndicator busyIndicator;
    private readonly AbsoluteLayout dragOverlay;
    private View? dragOverlayView;

    private IDispatcherTimer? longPressTimer;
    private IDispatcherTimer? currentTimeTimer;

    private bool recentring;
    private bool suppressDisplayDateSync;
    private bool initialised;
    private double allocatedWidth;
    private double allocatedHeight;

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

    public SchedulerView()
    {
        gridDrawable = new SchedulerGridDrawable(geometry);

        // Read through a delegate rather than copied in: SnapMinutes has no property-changed handler,
        // so a snapshot taken here would go stale the moment the host changed it.
        timelineSurface = new TimelineSurface(geometry, () => SnapMinutes);
        monthSurface = new MonthSurface(monthGeometry);
        pageSurface = timelineSurface;

        monthDrawable = new MonthGridDrawable(monthGeometry, slots);

        gutter = new TimeGutter(geometry);

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
        cellSelection = new CellSelectionOverlay(surface, SelectionZIndex);

        pagerScroll = new PagingScrollView { Content = surface };
        pagerScroll.Scrolled += OnPagerScrolled;
        pagerScroll.PageSettled += OnPageSettled;

        // Clipped on purpose. MAUI leaves ClipChildren off on its Android layout views so shadows can
        // spill, which also means the pager is drawn without being clipped to its own bounds — and the
        // pager's content is three pages wide with an opaque background, so the page parked to the
        // left of the viewport painted straight over the hour gutter beside it.
        var bodyGrid = new Grid
        {
            IsClippedToBounds = true,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            ]
        };
        bodyGrid.Add(gutter.View, 0);
        bodyGrid.Add(pagerScroll, 1);

        verticalScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = bodyGrid
        };

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
        headerClip = new Grid { IsClippedToBounds = true };
        headerClip.Add(headerSurface);

        var headerGrid = new Grid
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

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }


    /// <summary>The geometry of whichever surface is showing. Shared measurements only.</summary>
    private PageGeometry ActiveGeometry => ViewMode is SchedulerViewMode.Month ? monthGeometry : geometry;

    /// <summary>
    /// The template the pool is filled from. A month falls back to the timeline's when the host has
    /// not supplied one, which renders badly but renders.
    /// </summary>
    private DataTemplate? ActiveTemplate => ViewMode is SchedulerViewMode.Month
        ? MonthAppointmentTemplate ?? AppointmentTemplate
        : AppointmentTemplate;

    private bool DraggingEnabled => AllowDragAndDrop && ViewMode is SchedulerViewMode.Timeline;

    /// <summary>
    /// Scrolls the timeline so that <paramref name="time"/> is near the top of the viewport.
    /// Does nothing in a month, which shows every day whole and does not scroll.
    /// </summary>
    public void ScrollToTime(TimeSpan time)
    {
        if (ViewMode is SchedulerViewMode.Month)
            return;

        var y = geometry.YFromMinutes(time.TotalMinutes);
        _ = verticalScroll.ScrollToAsync(0, Math.Max(0, y), false);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        ConfigurePlatformScrolling();

        currentTimeTimer = Dispatcher.CreateTimer();
        currentTimeTimer.Interval = TimeSpan.FromMinutes(1);
        currentTimeTimer.Tick += OnCurrentTimeTick;
        currentTimeTimer.Start();

        // Open on the current time rather than at StartHour, like most calendars do.
        var now = NowInZone();
        if (ViewMode is SchedulerViewMode.Timeline && now.TimeOfDay.TotalMinutes > geometry.WindowStartMinutes)
            ScrollToTime(now.TimeOfDay - TimeSpan.FromHours(1));
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        currentTimeTimer?.Stop();
        currentTimeTimer = null;
        longPressTimer?.Stop();
        longPressTimer = null;
    }

    private void OnCurrentTimeTick(object? sender, EventArgs e)
    {
        var previousDate = ActiveGeometry.Now.Date;
        ActiveGeometry.Now = NowInZone();
        gridView.Invalidate();

        // The drawable re-evaluates "today" every time it repaints, but the day headers are real
        // labels that are only rewritten when a slot is rebuilt. Left alone they would keep marking
        // yesterday until the next swipe. A month has no such labels — its day numbers are painted.
        if (previousDate == ActiveGeometry.Now.Date || ViewMode is SchedulerViewMode.Month)
            return;

        for (var i = 0; i < slots.Length; i++)
            UpdateSlotHeader(slots[i], i);
    }

    private DateTime NowInZone() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
            return;

        allocatedWidth = width;
        allocatedHeight = height;

        var viewport = Math.Max(0, width - ActiveGutterWidth);
        var unchanged = Math.Abs(viewport - ActiveGeometry.ViewportWidth) < 0.5 && initialised;

        ActiveGeometry.ViewportWidth = viewport;
        ActiveGeometry.ViewportHeight = Math.Max(0, height - HeaderHeight);

        if (unchanged)
            return;

        ApplyGeometry();
    }

    private static void OnGeometryChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerView)bindable).ApplyGeometry();

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
        // A month is not made of a number of days the host chose. The new count is picked up by
        // ApplyTimelineChrome whenever the timeline comes back.
        if (ViewMode is SchedulerViewMode.Month)
            return;

        var previousDayWidth = geometry.ViewportWidth / Math.Clamp(oldDays, 1, 7);
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

        // Puts the day that was already on screen back at the left edge of the viewport, so the new
        // days grow in from the side they belong on — Monday and Tuesday from the left, Saturday and
        // Sunday from the right — instead of the whole page sliding sideways first. Pages are
        // spaced by what they currently measure, so the centre page starts one page-span in, and the
        // shift has to undo both that and the columns preceding the anchored day.
        var shiftedDays = previousPageStart.DayNumber - slots[1].PageStart.DayNumber;
        var startOffset = geometry.ViewportWidth
            - ((geometry.VisibleDays + shiftedDays) * previousDayWidth);

        this.AbortAnimation(DayCountAnimationName);

        new Animation(
            progress =>
            {
                geometry.DayWidthOverride = previousDayWidth + ((newDayWidth - previousDayWidth) * progress);
                geometry.AnimationOffsetX = startOffset * (1 - progress);
                ApplyDayWidth();
            },
            0,
            1,
            Easing.CubicInOut)
            .Commit(this, DayCountAnimationName, length: DayCountAnimationMs, finished: (_, _) =>
            {
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


    /// <summary>A month reaches the left edge; only the timeline sets its hours aside a gutter.</summary>
    private double ActiveGutterWidth => ViewMode is SchedulerViewMode.Month ? 0 : TimeGutterWidth;

    private void ApplyGeometry()
    {
        var active = ActiveGeometry;

        active.FirstDayOfWeek = FirstDayOfWeek;
        active.Now = NowInZone();
        active.ViewportHeight = Math.Max(0, allocatedHeight - HeaderHeight);

        gridView.BackgroundColor = GridBackgroundColor;

        if (ViewMode is SchedulerViewMode.Month)
            ApplyMonthChrome();
        else
            ApplyTimelineChrome();

        if (active.ViewportWidth <= 0)
            return;

        pagerScroll.PageWidth = active.ViewportWidth;

        surface.WidthRequest = active.SurfaceWidth;
        surface.HeightRequest = active.ContentHeight;
        AbsoluteLayout.SetLayoutBounds(gridView, new Rect(0, 0, active.SurfaceWidth, active.ContentHeight));

        headerSurface.WidthRequest = active.SurfaceWidth;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].DayNameLabels.Length != HeaderColumns)
                BuildSlotHeader(slots[i]);

            AbsoluteLayout.SetLayoutBounds(slots[i].Header, new Rect(0, 0, active.ViewportWidth, HeaderHeight));
        }

        RebuildAll(pageSurface.StartOfPage(DateOnly.FromDateTime(DisplayDate)));
        initialised = true;
    }

    private void ApplyTimelineChrome()
    {
        geometry.HourHeight = HourHeight;
        geometry.VisibleDays = Math.Clamp(VisibleDays, 1, 7);
        geometry.StartHour = StartHour;
        geometry.EndHour = EndHour;

        gridView.Drawable = gridDrawable;

        gutter.Update(TimeGutterWidth, TimeFormat, GridBackgroundColor);

        headerCorner.WidthRequest = TimeGutterWidth;
        headerCorner.Text = TimeZoneAbbreviation();

        headerClip.HeightRequest = HeaderHeight;
        headerSurface.HeightRequest = HeaderHeight;
    }

    private void ApplyMonthChrome()
    {
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

    /// <summary>
    /// Swaps the surface, and everything that belongs to the one being left behind.
    /// </summary>
    /// <remarks>
    /// The two modes do not share a template or a geometry, so every placed view goes back to the
    /// pool and the pool itself is emptied — a chip and an appointment box are different templates,
    /// and a spare built from one is no use to the other.
    /// </remarks>
    private void ChangeViewMode(SchedulerViewMode mode)
    {
        CancelDragCandidate();

        foreach (var slot in slots)
            ReleaseSlot(slot);

        pool.Clear();

        if (dragOverlayView is not null)
        {
            dragOverlay.Remove(dragOverlayView);
            dragOverlayView = null;
        }

        pageSurface = mode is SchedulerViewMode.Month ? monthSurface : timelineSurface;
        pool.Template = ActiveTemplate;
        cellSelection.Reset();

        // The gutter appears or disappears with the mode, so the viewport is a different width now.
        // Nothing re-allocates the control's size, so this is the only place that would notice.
        ActiveGeometry.ViewportWidth = Math.Max(0, allocatedWidth - ActiveGutterWidth);

        ApplyGeometry();
    }

    private string TimeZoneAbbreviation()
    {
        var offset = TimeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return offset.Minutes == 0
            ? $"GMT{sign}{Math.Abs(offset.Hours)}"
            : $"GMT{sign}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):00}";
    }
}
