using System.Collections.Specialized;
using System.Globalization;
using Microsoft.Maui.Layouts;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>
/// A Google-Calendar-style week view built from plain .NET MAUI primitives.
/// </summary>
/// <remarks>
/// Three week pages are rendered at all times and rotated as a ring buffer, so swiping is infinite
/// while only one page is ever rebuilt. See DESIGN.md for the reasoning behind each decision.
/// </remarks>
public class SchedulerWeekView : ContentView
{
    private const double AppointmentGap = 1;
    private const double MinimumAppointmentHeight = 18;
    private const int SnapDetectionDelayMs = 90;
    private const int LongPressDelayMs = 350;
    private const double DragMovementToleranceDp = 12;
    private const double TapMovementToleranceDp = 8;
    private const double EdgePagingZoneDp = 32;
    private const int EdgePagingDwellMs = 600;
    private const double GhostOpacity = 0.5;
    private const double LiftedOpacity = 0.85;

    // AbsoluteLayout paints by ZIndex first, so the stacking order is stated explicitly rather than
    // being an accident of the order children happen to be added in.
    private const int GridZIndex = 0;
    private const int SelectionZIndex = 1;
    private const int AppointmentZIndex = 2;
    private const int DraggedAppointmentZIndex = 100;

    private readonly SchedulerGeometry geometry = new();
    private readonly WeekGridDrawable gridDrawable;
    private readonly TimeGutterDrawable gutterDrawable;
    private readonly AppointmentViewPool pool;
    private readonly WeekSlot[] slots = new WeekSlot[SchedulerGeometry.SlotCount];
    private readonly Dictionary<View, ISchedulerAppointment> appointmentsByView = [];
    private readonly Dictionary<View, WeekSlot> slotsByView = [];

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

    private View? selectionView;
    private IDispatcherTimer? snapTimer;
    private IDispatcherTimer? longPressTimer;
    private IDispatcherTimer? currentTimeTimer;

    private double lastScrollX;
    private bool recentring;
    private bool snapping;
    private bool suppressDisplayDateSync;
    private bool initialised;

    private View? dragView;
    private View? pressedView;
    private View? floatingView;
    private View? ghostView;
    private ISchedulerAppointment? floatingAppointment;
    private IDispatcherTimer? edgePagingTimer;
    private int edgePagingDirection;
    private bool pagingDuringDrag;
    private double trackedScrollX;
    private Point lastDragPoint;
    private bool dragArmed;
    private Rect dragOriginalBounds;
    private Point dragGrabOffset;
    private Point interactionStart;
    private bool interactionMoved;
    private DateTime dragDropStart;

    public SchedulerWeekView()
    {
        gridDrawable = new WeekGridDrawable(geometry);
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
        root.Add(headerGrid, 0, 0);
        root.Add(verticalScroll, 0, 1);
        root.Add(busyIndicator, 0, 1);

        Content = root;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region Bindable properties

    public static readonly BindableProperty DisplayDateProperty = BindableProperty.Create(
        nameof(DisplayDate), typeof(DateTime), typeof(SchedulerWeekView), DateTime.Today,
        BindingMode.TwoWay, propertyChanged: OnDisplayDateChanged);

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable<ISchedulerAppointment>), typeof(SchedulerWeekView), null,
        propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty AppointmentTemplateProperty = BindableProperty.Create(
        nameof(AppointmentTemplate), typeof(DataTemplate), typeof(SchedulerWeekView), null,
        propertyChanged: OnAppointmentTemplateChanged);

    public static readonly BindableProperty CellSelectionTemplateProperty = BindableProperty.Create(
        nameof(CellSelectionTemplate), typeof(DataTemplate), typeof(SchedulerWeekView), null,
        propertyChanged: OnCellSelectionTemplateChanged);

    public static readonly BindableProperty SelectedSlotProperty = BindableProperty.Create(
        nameof(SelectedSlot), typeof(SchedulerTimeSlot?), typeof(SchedulerWeekView), null,
        BindingMode.TwoWay, propertyChanged: OnSelectedSlotChanged);

    public static readonly BindableProperty StartHourProperty = BindableProperty.Create(
        nameof(StartHour), typeof(int), typeof(SchedulerWeekView), 8, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty EndHourProperty = BindableProperty.Create(
        nameof(EndHour), typeof(int), typeof(SchedulerWeekView), 23, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty HourHeightProperty = BindableProperty.Create(
        nameof(HourHeight), typeof(double), typeof(SchedulerWeekView), 50d, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty FirstDayOfWeekProperty = BindableProperty.Create(
        nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(SchedulerWeekView), DayOfWeek.Monday,
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty TimeZoneProperty = BindableProperty.Create(
        nameof(TimeZone), typeof(TimeZoneInfo), typeof(SchedulerWeekView), TimeZoneInfo.Local,
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty TimeFormatProperty = BindableProperty.Create(
        nameof(TimeFormat), typeof(string), typeof(SchedulerWeekView), "HH:mm",
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty TimeGutterWidthProperty = BindableProperty.Create(
        nameof(TimeGutterWidth), typeof(double), typeof(SchedulerWeekView), 52d, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty HeaderHeightProperty = BindableProperty.Create(
        nameof(HeaderHeight), typeof(double), typeof(SchedulerWeekView), 52d, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty SnapMinutesProperty = BindableProperty.Create(
        nameof(SnapMinutes), typeof(int), typeof(SchedulerWeekView), 15);

    public static readonly BindableProperty AllowDragAndDropProperty = BindableProperty.Create(
        nameof(AllowDragAndDrop), typeof(bool), typeof(SchedulerWeekView), true);

    public static readonly BindableProperty AllowDragAcrossPeriodsProperty = BindableProperty.Create(
        nameof(AllowDragAcrossPeriods), typeof(bool), typeof(SchedulerWeekView), true);

    public static readonly BindableProperty ShowDragTimeIndicatorProperty = BindableProperty.Create(
        nameof(ShowDragTimeIndicator), typeof(bool), typeof(SchedulerWeekView), true);

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(SchedulerWeekView), false, propertyChanged: OnIsBusyChanged);

    public static readonly BindableProperty GridBackgroundColorProperty = BindableProperty.Create(
        nameof(GridBackgroundColor), typeof(Color), typeof(SchedulerWeekView), Colors.White,
        propertyChanged: OnGeometryChanged);

    /// <summary>Any date inside the week to display. Updated by the control after each swipe.</summary>
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty);
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>
    /// Every appointment the host has loaded. The control picks out the ones belonging to each
    /// rendered week, so the host can keep one flat collection covering several weeks.
    /// </summary>
    public IEnumerable<ISchedulerAppointment>? ItemsSource
    {
        get => (IEnumerable<ISchedulerAppointment>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Template for an appointment box. Instances are pooled and rebound, so the template must not
    /// depend on being constructed per appointment.
    /// </summary>
    public DataTemplate? AppointmentTemplate
    {
        get => (DataTemplate?)GetValue(AppointmentTemplateProperty);
        set => SetValue(AppointmentTemplateProperty, value);
    }

    /// <summary>Optional template for the selected-cell affordance. Bound to the selected <see cref="SchedulerTimeSlot"/>.</summary>
    public DataTemplate? CellSelectionTemplate
    {
        get => (DataTemplate?)GetValue(CellSelectionTemplateProperty);
        set => SetValue(CellSelectionTemplateProperty, value);
    }

    public SchedulerTimeSlot? SelectedSlot
    {
        get => (SchedulerTimeSlot?)GetValue(SelectedSlotProperty);
        set => SetValue(SelectedSlotProperty, value);
    }

    public int StartHour
    {
        get => (int)GetValue(StartHourProperty);
        set => SetValue(StartHourProperty, value);
    }

    public int EndHour
    {
        get => (int)GetValue(EndHourProperty);
        set => SetValue(EndHourProperty, value);
    }

    /// <summary>Pixel height of one hour row.</summary>
    public double HourHeight
    {
        get => (double)GetValue(HourHeightProperty);
        set => SetValue(HourHeightProperty, value);
    }

    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>The zone every <see cref="ISchedulerAppointment"/> value is expressed in.</summary>
    public TimeZoneInfo TimeZone
    {
        get => (TimeZoneInfo)GetValue(TimeZoneProperty);
        set => SetValue(TimeZoneProperty, value);
    }

    public string TimeFormat
    {
        get => (string)GetValue(TimeFormatProperty);
        set => SetValue(TimeFormatProperty, value);
    }

    public double TimeGutterWidth
    {
        get => (double)GetValue(TimeGutterWidthProperty);
        set => SetValue(TimeGutterWidthProperty, value);
    }

    public double HeaderHeight
    {
        get => (double)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    /// <summary>Granularity used when selecting a cell and when snapping a dropped appointment.</summary>
    public int SnapMinutes
    {
        get => (int)GetValue(SnapMinutesProperty);
        set => SetValue(SnapMinutesProperty, value);
    }

    public bool AllowDragAndDrop
    {
        get => (bool)GetValue(AllowDragAndDropProperty);
        set => SetValue(AllowDragAndDropProperty, value);
    }

    /// <summary>
    /// Whether the time an appointment would take is shown in the hour gutter while it is dragged.
    /// </summary>
    public bool ShowDragTimeIndicator
    {
        get => (bool)GetValue(ShowDragTimeIndicatorProperty);
        set => SetValue(ShowDragTimeIndicatorProperty, value);
    }

    /// <summary>
    /// Whether holding a dragged appointment against the leading or trailing edge pages to the
    /// adjacent period, allowing it to be moved out of the one it started in.
    /// </summary>
    /// <remarks>
    /// Named for the period rather than the week because the behaviour belongs to paging itself: a
    /// day or three-day surface would page the same way.
    /// </remarks>
    public bool AllowDragAcrossPeriods
    {
        get => (bool)GetValue(AllowDragAcrossPeriodsProperty);
        set => SetValue(AllowDragAcrossPeriodsProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>
    /// Fill behind the grid. It is an opaque colour rather than transparent so the drawing surface
    /// reliably receives the taps that select an empty cell.
    /// </summary>
    public Color GridBackgroundColor
    {
        get => (Color)GetValue(GridBackgroundColorProperty);
        set => SetValue(GridBackgroundColorProperty, value);
    }

    #endregion

    #region Events

    /// <summary>Raised when empty grid space is tapped.</summary>
    public event EventHandler<SchedulerCellTappedEventArgs>? CellTapped;

    public event EventHandler<SchedulerAppointmentTappedEventArgs>? AppointmentTapped;

    /// <summary>Raised before a drag begins; set <c>Cancel</c> to refuse it.</summary>
    public event EventHandler<SchedulerAppointmentDragStartingEventArgs>? AppointmentDragStarting;

    /// <summary>Raised after a drag is released; set <c>Cancel</c> to snap the appointment back.</summary>
    public event EventHandler<SchedulerAppointmentDroppedEventArgs>? AppointmentDropped;

    /// <summary>Raised whenever the centre week changes, including on first layout.</summary>
    public event EventHandler<SchedulerVisibleDatesChangedEventArgs>? VisibleDatesChanged;

    #endregion

    /// <summary>Scrolls the timeline so that <paramref name="time"/> is near the top of the viewport.</summary>
    public void ScrollToTime(TimeSpan time)
    {
        var y = geometry.YFromMinutes(time.TotalMinutes);
        _ = verticalScroll.ScrollToAsync(0, Math.Max(0, y), false);
    }

    #region Lifecycle and geometry

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

        var viewport = Math.Max(0, width - TimeGutterWidth);
        if (Math.Abs(viewport - geometry.ViewportWidth) < 0.5 && initialised)
            return;

        geometry.ViewportWidth = viewport;
        ApplyGeometry();
    }

    private static void OnGeometryChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerWeekView)bindable).ApplyGeometry();

    private void ApplyGeometry()
    {
        geometry.HourHeight = HourHeight;
        geometry.StartHour = StartHour;
        geometry.EndHour = EndHour;
        geometry.FirstDayOfWeek = FirstDayOfWeek;
        geometry.Now = NowInZone();

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
            AbsoluteLayout.SetLayoutBounds(slots[i].Header, new Rect(0, 0, geometry.ViewportWidth, HeaderHeight));

        RebuildAll(StartOfWeek(DateOnly.FromDateTime(DisplayDate)));
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

    private DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        return date.AddDays(-diff);
    }

    #endregion

    #region Slot construction and population

    private WeekSlot CreateSlot()
    {
        var header = new Grid
        {
            ColumnDefinitions = [.. Enumerable.Range(0, 7).Select(_ => new ColumnDefinition(GridLength.Star))]
        };

        var nameLabels = new Label[7];
        var numberLabels = new Label[7];

        for (var day = 0; day < 7; day++)
        {
            var stack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 6) };

            nameLabels[day] = new Label
            {
                FontSize = 11,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Color.FromArgb("#6E6E6E")
            };
            numberLabels[day] = new Label
            {
                FontSize = 16,
                HorizontalTextAlignment = TextAlignment.Center
            };

            stack.Add(nameLabels[day]);
            stack.Add(numberLabels[day]);
            header.Add(stack, day);
        }

        headerSurface.Add(header);

        return new WeekSlot
        {
            Header = header,
            DayNameLabels = nameLabels,
            DayNumberLabels = numberLabels
        };
    }

    private void RebuildAll(DateOnly centreWeek)
    {
        // Same reasoning as in SnapAsync: do not carry a waiting drop across a change of period.
        if (!dragArmed)
            ReleaseFloatingAppointment();

        slots[0].WeekStart = centreWeek.AddDays(-7);
        slots[1].WeekStart = centreWeek;
        slots[2].WeekStart = centreWeek.AddDays(7);

        for (var i = 0; i < slots.Length; i++)
            PopulateSlot(slots[i], i);

        SyncSlotWeeks();
        UpdateSelectionView();
        RaiseVisibleDatesChanged();
        _ = RecentreAsync(animated: false);
    }

    private void SyncSlotWeeks()
    {
        for (var i = 0; i < slots.Length; i++)
            geometry.SlotWeeks[i] = slots[i].WeekStart;

        gridView.Invalidate();
    }

    /// <summary>Rebinds one week from scratch: releases its views, re-lays out, and re-rents.</summary>
    /// <summary>Brings one week's appointment views in line with the data, reusing what is already there.</summary>
    /// <remarks>
    /// This reconciles rather than rebuilds. Releasing every view and renting them back would hide and
    /// re-show each one, and the pool is a stack, so the views would come back in reverse order — each
    /// bound to a *different* appointment than before. A refresh that returned identical data would
    /// still repaint every appointment on screen, which reads as a flash.
    /// <para>
    /// Views are therefore reused positionally. The layout engine orders deterministically (by day,
    /// then start, then duration), so entry <c>i</c> of the new layout is the same appointment as
    /// entry <c>i</c> of the old one whenever nothing has changed: each view is rebound to an
    /// equivalent item in the same place, renders identically, and nothing visibly happens. Only a
    /// genuine surplus or shortfall touches the pool.
    /// </para>
    /// </remarks>
    private void PopulateSlot(WeekSlot slot, int slotIndex)
    {
        UpdateSlotHeader(slot, slotIndex);

        var positions = AppointmentTemplate is null || ItemsSource is null || geometry.ViewportWidth <= 0
            ? []
            : AppointmentLayoutEngine.Layout(LayoutItems(), slot.WeekStart, StartHour, EndHour);

        // Index what this week already has by the identity of what it is showing.
        var available = new Dictionary<object, View>(slot.Views.Count);

        foreach (var view in slot.Views)
        {
            if (appointmentsByView.TryGetValue(view, out var bound))
                available[bound.Key] = view;
        }

        var arranged = new List<View>(positions.Count);

        foreach (var position in positions)
        {
            if (!available.Remove(position.Appointment.Key, out var view))
            {
                view = pool.Rent();

                if (view is null)
                    break;
            }

            arranged.Add(view);
            BindAppointmentView(view, position, slot, slotIndex);
        }

        // Whatever no appointment claimed is genuinely gone from this week.
        foreach (var surplus in available.Values)
        {
            appointmentsByView.Remove(surplus);
            slotsByView.Remove(surplus);
            pool.Return(surplus);
        }

        slot.Views.Clear();
        slot.Views.AddRange(arranged);
    }

    /// <summary>
    /// The appointments the weeks lay out. An appointment being dragged is excluded: it is represented
    /// by a floating view that belongs to no week, which is what lets the weeks rotate underneath it
    /// while it stays under the finger.
    /// </summary>
    private IEnumerable<ISchedulerAppointment> LayoutItems()
    {
        var items = ItemsSource ?? [];

        // Matched by key, not by instance: the collection may have been rebuilt since the drag began.
        return floatingAppointment is null
            ? items
            : items.Where(item => !Equals(item.Key, floatingAppointment.Key));
    }

    /// <summary>
    /// Exchanges an appointment for whichever instance currently represents it.
    /// </summary>
    /// <remarks>
    /// Everything the control hands back to the host goes through here, so a handler that acts on
    /// what it was given is acting on something the host is still displaying. Without it, an
    /// appointment picked up before a reload would be reported afterwards as an orphan, and mutating
    /// it would silently do nothing.
    /// </remarks>
    private ISchedulerAppointment Resolve(ISchedulerAppointment appointment)
    {
        if (ItemsSource is null)
            return appointment;

        foreach (var item in ItemsSource)
        {
            if (Equals(item.Key, appointment.Key))
                return item;
        }

        return appointment;
    }

    private void BindAppointmentView(View view, PositionedAppointment position, WeekSlot slot, int slotIndex)
    {
        view.BindingContext = position.Appointment;
        SetAppointmentSemantics(view, position.Appointment);
        appointmentsByView[view] = position.Appointment;
        slotsByView[view] = slot;

        PositionAppointmentView(view, position, slotIndex);
    }

    private void SetAppointmentSemantics(View view, ISchedulerAppointment appointment)
    {
        var culture = CultureInfo.CurrentUICulture;
        var range = $"{appointment.Start.ToString(TimeFormat, culture)} - {appointment.End.ToString(TimeFormat, culture)}";
        var day = appointment.Start.ToString("D", culture);

        SemanticProperties.SetDescription(view, string.IsNullOrEmpty(appointment.Subject)
            ? $"{day}, {range}"
            : $"{appointment.Subject}, {day}, {range}");
    }

    private void ReleaseSlot(WeekSlot slot)
    {
        foreach (var view in slot.Views)
        {
            appointmentsByView.Remove(view);
            slotsByView.Remove(view);
            pool.Return(view);
        }

        slot.Views.Clear();
    }

    private void UpdateSlotHeader(WeekSlot slot, int slotIndex)
    {
        var culture = CultureInfo.CurrentUICulture;
        var today = DateOnly.FromDateTime(geometry.Now);

        for (var day = 0; day < 7; day++)
        {
            var date = slot.WeekStart.AddDays(day);
            var isToday = date == today;

            slot.DayNameLabels[day].Text = culture.DateTimeFormat
                .GetAbbreviatedDayName(date.DayOfWeek)
                .ToUpper(culture);

            var number = slot.DayNumberLabels[day];
            number.Text = date.Day.ToString(culture);
            number.FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None;
            number.TextColor = isToday ? Color.FromArgb("#4458C8") : Color.FromArgb("#212121");
        }

        slot.Header.TranslationX = slotIndex * geometry.ViewportWidth;
    }

    private void PositionAppointmentView(View view, PositionedAppointment position, int slotIndex)
    {
        var dayWidth = geometry.DayWidth;
        var x = position.DayIndex * dayWidth + position.LeftFraction * dayWidth + AppointmentGap;
        var width = Math.Max(1, position.WidthFraction * dayWidth - AppointmentGap * 2);
        var y = geometry.YFromMinutes(position.StartMinutes);
        var height = Math.Max(MinimumAppointmentHeight, geometry.YFromMinutes(position.EndMinutes) - y);

        // Only write bounds that actually changed: an unchanged write still costs a layout pass.
        var bounds = new Rect(x, y, width, height);

        if (AbsoluteLayout.GetLayoutBounds(view) != bounds)
        {
            AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
            AbsoluteLayout.SetLayoutBounds(view, bounds);
        }

        // The slot offset lives in TranslationX so rotating weeks never triggers a layout pass.
        view.TranslationX = slotIndex * geometry.ViewportWidth;
        view.TranslationY = 0;
        view.ZIndex = AppointmentZIndex;
    }

    /// <summary>Moves an untouched week to a new physical position — the cheap half of a rotation.</summary>
    private void ShiftSlot(WeekSlot slot, int slotIndex)
    {
        var offset = slotIndex * geometry.ViewportWidth;

        foreach (var view in slot.Views)
            view.TranslationX = offset;

        slot.Header.TranslationX = offset;
    }

    #endregion

    #region Horizontal pager

    private void OnPagerScrolled(object? sender, ScrolledEventArgs e)
    {
        // The header sits outside the pager, so it is the one thing here that does not move by itself
        // and has to be mirrored by hand. This runs unconditionally, including while recentring:
        // gating it meant that any programmatic scroll which did not settle exactly as expected left
        // the strip stranded at a stale offset — off-screen, reading as a missing header — until some
        // later rebuild happened to reset it.
        headerSurface.TranslationX = -e.ScrollX;

        var scrollDelta = e.ScrollX - trackedScrollX;
        trackedScrollX = e.ScrollX;

        // While an appointment is being dragged across weeks the surface slides behind it. The lifted
        // view is positioned in surface coordinates, so it has to be pushed the opposite way by the
        // same amount to stay under a finger that has not moved.
        if (dragArmed && dragView is not null)
        {
            dragView.TranslationX += scrollDelta;
        }

        // A drag drives the pager itself, so nothing here should be mistaken for a swipe.
        if (recentring || dragArmed)
            return;

        lastScrollX = e.ScrollX;

        // Where the platform pages natively this only asks "has it arrived yet", because the target
        // was already chosen on finger release. Elsewhere it is also what picks the target, which is
        // worse: inertia has to run out first. Every new Scrolled event restarts the timer.
        snapTimer?.Stop();
        snapTimer ??= CreateSnapTimer();
        snapTimer.Start();
    }

    private IDispatcherTimer CreateSnapTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(SnapDetectionDelayMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => _ = SnapAsync();
        return timer;
    }

    private async Task SnapAsync()
    {
        if (snapping || recentring || geometry.ViewportWidth <= 0)
            return;

        // An accepted drop waits for the host to feed the change back before rejoining a week. If that
        // never comes, its view would stay pinned to the surface and drift over whatever week is
        // scrolled to next. Changing period is a safe moment to give up waiting and go back to what
        // the model says.
        if (floatingAppointment is not null)
            RepopulateAllSlots();

        var page = (int)Math.Round(lastScrollX / geometry.ViewportWidth);
        page = Math.Clamp(page, 0, SchedulerGeometry.SlotCount - 1);

        if (page == 1)
        {
            // Already centred; only correct a partial drag that did not change week.
            if (Math.Abs(lastScrollX - geometry.ViewportWidth) > 0.5)
                await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, true);
            return;
        }

        snapping = true;
        try
        {
            // Let the page the user chose settle under the finger first. This one is animated and
            // visible, so Scrolled must keep running to drag the day headers along with it.
            await pagerScroll.ScrollToAsync(page * geometry.ViewportWidth, 0, true);

            // Now rotate and jump back to the middle. Scroll handling is suppressed for this part so
            // nothing repaints the header between the rotation and the instant recentre.
            recentring = true;

            if (page == SchedulerGeometry.SlotCount - 1)
                Advance();
            else
                Retreat();

            headerSurface.TranslationX = -geometry.ViewportWidth;
            await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, false);
            lastScrollX = geometry.ViewportWidth;
        }
        finally
        {
            recentring = false;
            snapping = false;
        }

        SyncSlotWeeks();
        UpdateSelectionView();
        SyncDisplayDate();
        RaiseVisibleDatesChanged();
    }

    private void Advance()
    {
        var recycled = slots[0];
        slots[0] = slots[1];
        slots[1] = slots[2];
        slots[2] = recycled;

        ShiftSlot(slots[0], 0);
        ShiftSlot(slots[1], 1);

        recycled.WeekStart = slots[1].WeekStart.AddDays(7);
        PopulateSlot(recycled, 2);
    }

    private void Retreat()
    {
        var recycled = slots[2];
        slots[2] = slots[1];
        slots[1] = slots[0];
        slots[0] = recycled;

        ShiftSlot(slots[1], 1);
        ShiftSlot(slots[2], 2);

        recycled.WeekStart = slots[1].WeekStart.AddDays(-7);
        PopulateSlot(recycled, 0);
    }

    private async Task RecentreAsync(bool animated)
    {
        if (geometry.ViewportWidth <= 0)
            return;

        recentring = true;
        try
        {
            headerSurface.TranslationX = -geometry.ViewportWidth;
            await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, animated);
            lastScrollX = geometry.ViewportWidth;
        }
        finally
        {
            recentring = false;
        }
    }

    private void RaiseVisibleDatesChanged()
    {
        var centre = slots[1].WeekStart;
        var visible = Enumerable.Range(0, 7)
            .Select(i => centre.AddDays(i).ToDateTime(TimeOnly.MinValue))
            .ToArray();

        VisibleDatesChanged?.Invoke(this, new SchedulerVisibleDatesChangedEventArgs(
            visible,
            slots[0].WeekStart.ToDateTime(TimeOnly.MinValue),
            slots[2].WeekStart.AddDays(6).ToDateTime(TimeOnly.MaxValue)));
    }

    private void SyncDisplayDate()
    {
        suppressDisplayDateSync = true;
        try
        {
            DisplayDate = slots[1].WeekStart.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            suppressDisplayDateSync = false;
        }
    }

    #endregion

    #region Property change handlers

    private static void OnDisplayDateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerWeekView)bindable;

        if (view.suppressDisplayDateSync || !view.initialised)
            return;

        var target = view.StartOfWeek(DateOnly.FromDateTime((DateTime)newValue));
        if (target == view.slots[1].WeekStart)
            return;

        view.RebuildAll(target);
        view.SyncDisplayDate();
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerWeekView)bindable;

        if (oldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= view.OnItemsCollectionChanged;

        if (newValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += view.OnItemsCollectionChanged;

        view.RepopulateAllSlots();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RepopulateAllSlots();

    private void RepopulateAllSlots()
    {
        if (!initialised)
            return;

        // Never re-lay-out under a drag in progress: the calendar would churn beneath the finger for
        // data the user cannot see yet. Whatever arrived is picked up when the drag ends, because
        // every way a drag can finish ends in another pass through here.
        if (dragArmed)
            return;

        // An accepted drop leaves the appointment floating where the user put it until the host feeds
        // the change back. This is that moment.
        ReleaseFloatingAppointment();

        for (var i = 0; i < slots.Length; i++)
            PopulateSlot(slots[i], i);
    }

    private static void OnAppointmentTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerWeekView)bindable;

        foreach (var slot in view.slots)
            view.ReleaseSlot(slot);

        view.pool.Clear();
        view.pool.Template = (DataTemplate?)newValue;
        view.RepopulateAllSlots();
    }

    private static void OnCellSelectionTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerWeekView)bindable;

        if (view.selectionView is not null)
        {
            view.surface.Remove(view.selectionView);
            view.selectionView = null;
        }

        view.UpdateSelectionView();
    }

    private static void OnSelectedSlotChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerWeekView)bindable).UpdateSelectionView();

    private static void OnIsBusyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerWeekView)bindable;
        view.busyIndicator.IsVisible = (bool)newValue;
        view.busyIndicator.IsRunning = (bool)newValue;
    }

    #endregion

    #region Cell selection

    private void UpdateSelectionView()
    {
        if (SelectedSlot is not { } slot || geometry.ViewportWidth <= 0)
        {
            if (selectionView is not null)
                selectionView.IsVisible = false;
            return;
        }

        var slotIndex = SlotIndexForDate(DateOnly.FromDateTime(slot.Start));
        if (slotIndex < 0)
        {
            if (selectionView is not null)
                selectionView.IsVisible = false;
            return;
        }

        selectionView ??= CreateSelectionView();
        selectionView.BindingContext = slot;
        selectionView.IsVisible = true;

        var dayIndex = DateOnly.FromDateTime(slot.Start).DayNumber - slots[slotIndex].WeekStart.DayNumber;
        var y = geometry.YFromMinutes(slot.Start.TimeOfDay.TotalMinutes);
        var height = Math.Max(MinimumAppointmentHeight, geometry.YFromMinutes(slot.End.TimeOfDay.TotalMinutes) - y);

        AbsoluteLayout.SetLayoutFlags(selectionView, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(selectionView, new Rect(dayIndex * geometry.DayWidth, y, geometry.DayWidth, height));
        selectionView.TranslationX = slotIndex * geometry.ViewportWidth;

        SemanticProperties.SetDescription(
            selectionView,
            slot.Start.ToString("D", CultureInfo.CurrentUICulture) + " " + slot.Start.ToString(TimeFormat, CultureInfo.CurrentUICulture));
    }

    private View CreateSelectionView()
    {
        var view = CellSelectionTemplate?.CreateContent() as View ?? new Border
        {
            BackgroundColor = Color.FromArgb("#F3E8FC"),
            Stroke = Color.FromArgb("#DAB8F4"),
            StrokeThickness = 1,
            Content = new Label
            {
                Text = "+",
                FontSize = 20,
                TextColor = Color.FromArgb("#6B3FA0"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        view.InputTransparent = true;
        view.ZIndex = SelectionZIndex;
        surface.Add(view);
        return view;
    }

    private int SlotIndexForDate(DateOnly date)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            var offset = date.DayNumber - slots[i].WeekStart.DayNumber;
            if (offset is >= 0 and < 7)
                return i;
        }

        return -1;
    }

    #endregion

    #region Interaction

    // All input for the scrolling surface is handled here, on the drawing surface, rather than by
    // gesture recognizers attached to each appointment.
    //
    // The reason is StartInteraction: GraphicsView reports the raw touch lifecycle, so a press is
    // known at touch-down, before any movement. PanGestureRecognizer cannot provide that -- it only
    // reports Started once the platform has already recognised a pan, i.e. after the finger has
    // moved -- which makes "hold still to pick an appointment up" impossible to detect with it.
    // Handling everything here also means pooled views need no recognizers attached at all.
    private void OnSurfaceStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || geometry.ViewportWidth <= 0)
            return;

        var point = new Point(e.Touches[0].X, e.Touches[0].Y);

        interactionStart = point;
        interactionMoved = false;
        pressedView = HitTestAppointment(point);

        if (pressedView is not null && AllowDragAndDrop)
            BeginDragCandidate(pressedView, point);
    }

    private void OnSurfaceDragInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
            return;

        var point = new Point(e.Touches[0].X, e.Touches[0].Y);
        var travelled = Math.Abs(point.X - interactionStart.X) + Math.Abs(point.Y - interactionStart.Y);

        if (travelled > TapMovementToleranceDp)
            interactionMoved = true;

        if (dragArmed)
        {
            UpdateDragPosition(point);
            return;
        }

        // Moving before the press has been held long enough means the user is scrolling.
        if (travelled > DragMovementToleranceDp)
            CancelDragCandidate();
    }

    // Raised when an ancestor scroll view claims the gesture. That is a far better "this is a scroll,
    // not a drag" signal than inferring it from a distance threshold, because it is the platform's
    // own decision.
    private void OnSurfaceCancelInteraction(object? sender, EventArgs e)
    {
        pressedView = null;
        CancelDragCandidate();
    }

    private void OnSurfaceEndInteraction(object? sender, TouchEventArgs e)
    {
        if (dragArmed)
        {
            CompleteDrag(committed: true);
            return;
        }

        var candidate = pressedView;
        var moved = interactionMoved;

        pressedView = null;
        CancelDragCandidate();

        if (moved || e.Touches.Length == 0)
            return;

        if (candidate is not null)
        {
            if (appointmentsByView.TryGetValue(candidate, out var appointment))
                AppointmentTapped?.Invoke(this, new SchedulerAppointmentTappedEventArgs(Resolve(appointment)));

            return;
        }

        if (ResolveSlot(new Point(e.Touches[0].X, e.Touches[0].Y)) is not { } slot)
            return;

        SelectedSlot = slot;
        CellTapped?.Invoke(this, new SchedulerCellTappedEventArgs(slot));
    }

    /// <summary>Finds the topmost appointment under a surface point, mirroring the paint order.</summary>
    private View? HitTestAppointment(Point point)
    {
        for (var i = slots.Length - 1; i >= 0; i--)
        {
            var views = slots[i].Views;

            for (var v = views.Count - 1; v >= 0; v--)
            {
                var view = views[v];
                var bounds = AbsoluteLayout.GetLayoutBounds(view);
                var rect = new Rect(
                    bounds.X + view.TranslationX,
                    bounds.Y + view.TranslationY,
                    bounds.Width,
                    bounds.Height);

                if (rect.Contains(point))
                    return view;
            }
        }

        return null;
    }

    private SchedulerTimeSlot? ResolveSlot(Point point)
    {
        if (geometry.ViewportWidth <= 0)
            return null;

        var slotIndex = Math.Clamp((int)(point.X / geometry.ViewportWidth), 0, SchedulerGeometry.SlotCount - 1);
        var xInSlot = point.X - slotIndex * geometry.ViewportWidth;
        var dayIndex = Math.Clamp((int)(xInSlot / geometry.DayWidth), 0, 6);

        var snap = Math.Max(1, SnapMinutes);
        var minutes = geometry.MinutesFromY(point.Y);
        var snapped = Math.Floor(minutes / snap) * snap;
        snapped = Math.Clamp(snapped, geometry.WindowStartMinutes, geometry.WindowEndMinutes - snap);

        var date = slots[slotIndex].WeekStart.AddDays(dayIndex);
        var start = date.ToDateTime(TimeOnly.MinValue).AddMinutes(snapped);

        return new SchedulerTimeSlot(start, TimeSpan.FromMinutes(snap));
    }

    #endregion

    #region Drag and drop

    private void BeginDragCandidate(View view, Point point)
    {
        CancelDragCandidate();

        if (!appointmentsByView.ContainsKey(view))
            return;

        dragView = view;
        dragOriginalBounds = AbsoluteLayout.GetLayoutBounds(view);

        // Remember where inside the appointment the finger landed so it does not jump on pick-up.
        dragGrabOffset = new Point(
            point.X - (dragOriginalBounds.X + view.TranslationX),
            point.Y - (dragOriginalBounds.Y + view.TranslationY));

        // The timer starts at touch-down, so holding still is what wins. This is the whole reason
        // interaction is handled on the drawing surface -- see OnSurfaceStartInteraction.
        longPressTimer = Dispatcher.CreateTimer();
        longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressDelayMs);
        longPressTimer.IsRepeating = false;
        longPressTimer.Tick += (_, _) => ArmDrag();
        longPressTimer.Start();
    }

    private void ArmDrag()
    {
        longPressTimer?.Stop();
        longPressTimer = null;

        if (dragView is null || !appointmentsByView.TryGetValue(dragView, out var appointment))
            return;

        var args = new SchedulerAppointmentDragStartingEventArgs(Resolve(appointment));
        AppointmentDragStarting?.Invoke(this, args);

        if (args.Cancel)
        {
            CancelDragCandidate();
            return;
        }

        // The appointment leaves its week and becomes two views: the original stays put as a faded
        // ghost so the slot it came from stays visible, and a second one is lifted onto the finger.
        // Neither belongs to a week any more, which is what lets the weeks rotate underneath them.
        floatingAppointment = appointment;

        ghostView = dragView;
        DetachFromSlot(ghostView);
        ghostView.Opacity = GhostOpacity;

        var lifted = pool.Rent();

        if (lifted is null)
        {
            ghostView.Opacity = 1;
            ghostView = null;
            floatingAppointment = null;
            CancelDragCandidate();
            return;
        }

        lifted.BindingContext = appointment;
        SetAppointmentSemantics(lifted, appointment);
        appointmentsByView[lifted] = appointment;

        AbsoluteLayout.SetLayoutFlags(lifted, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(lifted, dragOriginalBounds);
        lifted.TranslationX = ghostView.TranslationX;
        lifted.TranslationY = ghostView.TranslationY;
        lifted.ZIndex = DraggedAppointmentZIndex;
        lifted.Opacity = LiftedOpacity;

        floatingView = lifted;
        dragView = lifted;
        dragArmed = true;

        SetScrollingEnabled(false);

        UpdateDragPosition(new Point(
            dragOriginalBounds.X + lifted.TranslationX + dragGrabOffset.X,
            dragOriginalBounds.Y + lifted.TranslationY + dragGrabOffset.Y));
    }

    private void CancelDragCandidate()
    {
        longPressTimer?.Stop();
        longPressTimer = null;

        var wasArmed = dragArmed;

        dragArmed = false;
        dragView = null;

        if (!wasArmed)
            return;

        StopEdgePaging();
        SetScrollingEnabled(true);
        ClearDragIndicator();
        RepopulateAllSlots();
    }

    /// <summary>Takes a view out of its week so rotation and reconciliation leave it alone.</summary>
    private void DetachFromSlot(View view)
    {
        if (slotsByView.TryGetValue(view, out var slot))
            slot.Views.Remove(view);

        slotsByView.Remove(view);
    }

    /// <summary>Hands the drag's views back to the pool so the appointment is laid out normally again.</summary>
    private void ReleaseFloatingAppointment()
    {
        ReleaseGhost();

        if (floatingView is not null)
        {
            appointmentsByView.Remove(floatingView);
            pool.Return(floatingView);
        }

        floatingView = null;
        floatingAppointment = null;
    }

    /// <summary>Removes the faded original. Done as soon as the finger lifts, whatever the outcome.</summary>
    private void ReleaseGhost()
    {
        if (ghostView is null)
            return;

        appointmentsByView.Remove(ghostView);
        pool.Return(ghostView);
        ghostView = null;
    }

    /// <summary>
    /// One-time platform setup for the two scroll views.
    /// </summary>
    /// <remarks>
    /// A UIScrollView holds back touchesBegan while it decides whether a touch is the start of a
    /// scroll. The long press that picks an appointment up needs to know about the press the moment
    /// the finger lands, so that delay is turned off.
    /// </remarks>
    private void ConfigurePlatformScrolling()
    {
#if IOS || MACCATALYST
        if (pagerScroll.Handler?.PlatformView is UIKit.UIScrollView horizontalPlatformScroll)
        {
            horizontalPlatformScroll.DelaysContentTouches = false;

            // Paging makes the platform commit to a week the moment the finger leaves the screen,
            // using the release velocity. Without it the decision waits for inertia to run out, which
            // can take a second or more, and the user sees the content drift to a stop and only then
            // slide again to settle on a week. The scroll view's frame is exactly one viewport and
            // its content exactly three, so page boundaries already fall on 0 / W / 2W.
            horizontalPlatformScroll.PagingEnabled = true;
        }

        if (verticalScroll.Handler?.PlatformView is UIKit.UIScrollView verticalPlatformScroll)
            verticalPlatformScroll.DelaysContentTouches = false;
#endif
    }

    /// <summary>
    /// Stops the two scroll views from competing with an armed drag.
    /// </summary>
    /// <remarks>
    /// This is the one place the control reaches past MAUI. <c>IsEnabled = false</c> is not usable:
    /// it disables interaction for the whole subtree and cancels the very touch that is driving the
    /// drag. Both platforms expose a way to stop scrolling while still delivering touches.
    /// </remarks>
    private void SetScrollingEnabled(bool enabled)
    {
#if IOS || MACCATALYST
        if (pagerScroll.Handler?.PlatformView is UIKit.UIScrollView horizontalPlatformScroll)
            horizontalPlatformScroll.ScrollEnabled = enabled;

        if (verticalScroll.Handler?.PlatformView is UIKit.UIScrollView verticalPlatformScroll)
            verticalPlatformScroll.ScrollEnabled = enabled;
#elif ANDROID
        // Android ancestors keep intercepting until a descendant opts out for the rest of the gesture.
        if (gridView.Handler?.PlatformView is Android.Views.View platformView)
            platformView.Parent?.RequestDisallowInterceptTouchEvent(!enabled);
#endif
    }

    private void UpdateDragPosition(Point point)
    {
        if (dragView is null)
            return;

        lastDragPoint = point;

        // Scrolling is frozen for the duration of a drag and the pager always rests on the centre
        // slot, so that is the week being dropped into — whichever week has since been rotated into
        // it by edge paging.
        const int slotIndex = 1;

        var slotOffset = slotIndex * geometry.ViewportWidth;
        var height = dragOriginalBounds.Height;

        // Where the appointment's top-left wants to be, keeping the grabbed point under the finger.
        var desiredX = point.X - dragGrabOffset.X - slotOffset;
        var desiredY = point.Y - dragGrabOffset.Y;

        var dayIndex = Math.Clamp((int)Math.Round(desiredX / geometry.DayWidth), 0, 6);

        var snap = Math.Max(1, SnapMinutes);
        var minutes = geometry.MinutesFromY(desiredY);
        var snapped = Math.Round(minutes / snap) * snap;
        snapped = Math.Clamp(
            snapped,
            geometry.WindowStartMinutes,
            Math.Max(geometry.WindowStartMinutes, geometry.WindowEndMinutes - height / geometry.HourHeight * 60));

        var snappedX = dayIndex * geometry.DayWidth + AppointmentGap;
        var snappedY = geometry.YFromMinutes(snapped);

        dragView.TranslationX = slotOffset + (snappedX - dragOriginalBounds.X);
        dragView.TranslationY = snappedY - dragOriginalBounds.Y;

        dragDropStart = slots[slotIndex].WeekStart.AddDays(dayIndex).ToDateTime(TimeOnly.MinValue).AddMinutes(snapped);

        // Shown in the time gutter rather than over the grid: anywhere near the appointment is under
        // the finger doing the dragging, which is precisely where it cannot be read.
        if (ShowDragTimeIndicator)
        {
            gutterDrawable.HighlightMinutes = snapped;
            gutterDrawable.HighlightText = dragDropStart.ToString(TimeFormat, CultureInfo.CurrentUICulture);
            gutterView.Invalidate();
        }

        UpdateEdgePaging(point);
    }

    /// <summary>
    /// Holding a dragged appointment against the leading or trailing edge pages to the adjacent week,
    /// which is how an appointment is moved out of the week it started in.
    /// </summary>
    /// <remarks>
    /// A dwell rather than an immediate flip: the edges are exactly where someone drags to reach the
    /// first and last day of the week, so paging on contact would make those two columns unusable.
    /// The timer repeats, so continuing to hold keeps walking through the weeks.
    /// </remarks>
    private void UpdateEdgePaging(Point point)
    {
        if (!AllowDragAcrossPeriods || !dragArmed)
        {
            StopEdgePaging();
            return;
        }

        var xInPage = point.X - geometry.ViewportWidth;
        var direction = xInPage <= EdgePagingZoneDp
            ? -1
            : xInPage >= geometry.ViewportWidth - EdgePagingZoneDp
                ? 1
                : 0;

        if (direction == 0)
        {
            StopEdgePaging();
            return;
        }

        // Already counting down towards this edge — let it run rather than restarting the dwell.
        if (direction == edgePagingDirection)
            return;

        StopEdgePaging();
        edgePagingDirection = direction;

        edgePagingTimer = Dispatcher.CreateTimer();
        edgePagingTimer.Interval = TimeSpan.FromMilliseconds(EdgePagingDwellMs);
        edgePagingTimer.IsRepeating = true;
        edgePagingTimer.Tick += (_, _) => _ = PageDuringDragAsync();
        edgePagingTimer.Start();
    }

    private void ClearDragIndicator()
    {
        gutterDrawable.HighlightMinutes = null;
        gutterDrawable.HighlightText = null;
        gutterView.Invalidate();
    }

    private void StopEdgePaging()
    {
        edgePagingTimer?.Stop();
        edgePagingTimer = null;
        edgePagingDirection = 0;
    }

    private async Task PageDuringDragAsync()
    {
        if (!dragArmed || edgePagingDirection == 0)
        {
            StopEdgePaging();
            return;
        }

        // The dwell repeats, and the slide takes a moment; do not start a second one over the top.
        if (pagingDuringDrag)
            return;

        pagingDuringDrag = true;

        try
        {
            var forward = edgePagingDirection > 0;

            if (forward)
                Advance();
            else
                Retreat();

            // The ghost marks a slot in the week the drag started from, so it travels with that week
            // and slides off screen once the drag has moved on.
            if (ghostView is not null)
                ghostView.TranslationX += forward ? -geometry.ViewportWidth : geometry.ViewportWidth;

            SyncSlotWeeks();
            UpdateSelectionView();
            SyncDisplayDate();
            RaiseVisibleDatesChanged();

            // Rotating swaps the weeks without moving anything, so on its own the calendar simply
            // changes contents and it is hard to see that anything happened. Instead, jump to where
            // the outgoing week has landed — visually identical to the frame before — and then slide
            // across to the centre, so the change reads as the same motion as a swipe.
            var outgoing = forward ? 0 : geometry.SurfaceWidth - geometry.ViewportWidth;

            await pagerScroll.ScrollToAsync(outgoing, 0, false);
            await pagerScroll.ScrollToAsync(geometry.ViewportWidth, 0, true);

            lastScrollX = geometry.ViewportWidth;

            // The finger has not moved but the week beneath it has, so the drop target and its label
            // are now for a different date.
            UpdateDragPosition(lastDragPoint);
        }
        finally
        {
            pagingDuringDrag = false;
        }
    }

    private void CompleteDrag(bool committed)
    {
        longPressTimer?.Stop();
        longPressTimer = null;
        StopEdgePaging();

        var view = dragView;
        var wasArmed = dragArmed;

        dragArmed = false;
        dragView = null;
        pressedView = null;

        if (!wasArmed || view is null)
            return;

        SetScrollingEnabled(true);
        ClearDragIndicator();
        ReleaseGhost();
        view.ZIndex = AppointmentZIndex;
        view.Opacity = 1;

        if (!committed || !appointmentsByView.TryGetValue(view, out var appointment))
        {
            RepopulateAllSlots();
            return;
        }

        var args = new SchedulerAppointmentDroppedEventArgs(Resolve(appointment), dragDropStart);
        AppointmentDropped?.Invoke(this, args);

        // On success the view is deliberately left floating at the dropped position: the host's
        // update is asynchronous, and snapping back only to jump forward again would read as a
        // glitch. It rejoins its week when the host feeds the change back through ItemsSource.
        if (args.Cancel)
            RepopulateAllSlots();
    }

    #endregion
}
