using System.Collections.Specialized;
using System.Globalization;
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
    private const double AppointmentGap = 1;
    private const double MinimumAppointmentHeight = 18;
    private const int SnapDetectionDelayMs = 90;
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
    private const int GridZIndex = 0;
    private const int SelectionZIndex = 1;
    private const int AppointmentZIndex = 2;
    private const int DraggedAppointmentZIndex = 100;

    private readonly SchedulerGeometry geometry = new();
    private readonly SchedulerGridDrawable gridDrawable;
    private readonly TimeGutterDrawable gutterDrawable;
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
    private readonly ScrollView pagerScroll;
    private readonly AbsoluteLayout surface;
    private readonly GraphicsView gridView;
    private readonly GraphicsView gutterView;
    private readonly ActivityIndicator busyIndicator;
    private readonly AbsoluteLayout dragOverlay;
    private View? dragOverlayView;

    private IDispatcherTimer? snapTimer;
    private IDispatcherTimer? longPressTimer;
    private IDispatcherTimer? currentTimeTimer;

    private double lastScrollX;
    private bool recentring;
    private bool snapping;
    private bool suppressDisplayDateSync;
    private bool initialised;
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
        gutterDrawable = new TimeGutterDrawable(geometry);

        gutterView = new GraphicsView { Drawable = gutterDrawable, InputTransparent = true };

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
        cellSelection = new CellSelectionOverlay(surface, geometry, SelectionZIndex);

        pagerScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = surface
        };
        pagerScroll.Scrolled += OnPagerScrolled;

        var bodyGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            ]
        };
        bodyGrid.Add(gutterView, 0);
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


    /// <summary>Scrolls the timeline so that <paramref name="time"/> is near the top of the viewport.</summary>
    public void ScrollToTime(TimeSpan time)
    {
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
        if (now.TimeOfDay.TotalMinutes > geometry.WindowStartMinutes)
            ScrollToTime(now.TimeOfDay - TimeSpan.FromHours(1));
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        currentTimeTimer?.Stop();
        currentTimeTimer = null;
        snapTimer?.Stop();
        snapTimer = null;
        longPressTimer?.Stop();
        longPressTimer = null;
    }

    private void OnCurrentTimeTick(object? sender, EventArgs e)
    {
        var previousDate = geometry.Now.Date;
        geometry.Now = NowInZone();
        gridView.Invalidate();

        // The drawable re-evaluates "today" every time it repaints, but the day headers are real
        // labels that are only rewritten when a slot is rebuilt. Left alone they would keep marking
        // yesterday until the next swipe.
        if (previousDate == geometry.Now.Date)
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

        allocatedHeight = height;

        var viewport = Math.Max(0, width - TimeGutterWidth);
        var unchanged = Math.Abs(viewport - geometry.ViewportWidth) < 0.5 && initialised;

        geometry.ViewportWidth = viewport;
        geometry.ViewportHeight = Math.Max(0, height - HeaderHeight);

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


    private void ApplyGeometry()
    {
        geometry.HourHeight = HourHeight;
        geometry.VisibleDays = Math.Clamp(VisibleDays, 1, 7);
        geometry.StartHour = StartHour;
        geometry.EndHour = EndHour;
        geometry.FirstDayOfWeek = FirstDayOfWeek;
        geometry.Now = NowInZone();

        geometry.ViewportHeight = Math.Max(0, allocatedHeight - HeaderHeight);

        gutterDrawable.HourFormat = TimeFormat;
        gutterDrawable.Width = TimeGutterWidth;
        gridView.BackgroundColor = GridBackgroundColor;

        if (geometry.ViewportWidth <= 0)
            return;

        surface.WidthRequest = geometry.SurfaceWidth;
        surface.HeightRequest = geometry.ContentHeight;
        AbsoluteLayout.SetLayoutBounds(gridView, new Rect(0, 0, geometry.SurfaceWidth, geometry.ContentHeight));

        gutterView.WidthRequest = TimeGutterWidth;
        gutterView.HeightRequest = geometry.ContentHeight;

        headerCorner.WidthRequest = TimeGutterWidth;
        headerClip.HeightRequest = HeaderHeight;
        headerSurface.WidthRequest = geometry.SurfaceWidth;
        headerSurface.HeightRequest = HeaderHeight;
        headerCorner.Text = TimeZoneAbbreviation();

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].DayNameLabels.Length != geometry.VisibleDays)
                BuildSlotHeader(slots[i]);

            AbsoluteLayout.SetLayoutBounds(slots[i].Header, new Rect(0, 0, geometry.ViewportWidth, HeaderHeight));
        }

        RebuildAll(StartOfPage(DateOnly.FromDateTime(DisplayDate)));
        initialised = true;
    }

    private string TimeZoneAbbreviation()
    {
        var offset = TimeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return offset.Minutes == 0
            ? $"GMT{sign}{Math.Abs(offset.Hours)}"
            : $"GMT{sign}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):00}";
    }

    /// <summary>
    /// The first day of the page containing <paramref name="date"/>.
    /// </summary>
    /// <remarks>
    /// A full week snaps to <see cref="FirstDayOfWeek"/>, because a week that started on an arbitrary
    /// day would not be one. Shorter pages start on the day asked for, which is what makes "today"
    /// the leading column in a day or three-day view.
    /// </remarks>
    private DateOnly StartOfPage(DateOnly date)
    {
        if (geometry.VisibleDays < 7)
            return date;

        var diff = ((int)date.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        return date.AddDays(-diff);
    }
}
