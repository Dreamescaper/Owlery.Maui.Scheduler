using System.Collections.Specialized;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler;

/// <summary>The public surface: what a host can set, bind to and listen for.</summary>
public partial class SchedulerView
{
    public static readonly BindableProperty DisplayDateProperty = BindableProperty.Create(
        nameof(DisplayDate), typeof(DateTime), typeof(SchedulerView), DateTime.Today,
        BindingMode.TwoWay, propertyChanged: OnDisplayDateChanged);

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable<ISchedulerAppointment>), typeof(SchedulerView), null,
        propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty ViewModeProperty = BindableProperty.Create(
        nameof(ViewMode), typeof(SchedulerViewMode), typeof(SchedulerView), SchedulerViewMode.Timeline,
        propertyChanged: OnViewModeChanged);

    public static readonly BindableProperty AppointmentTemplateProperty = BindableProperty.Create(
        nameof(AppointmentTemplate), typeof(DataTemplate), typeof(SchedulerView), null,
        propertyChanged: OnAppointmentTemplateChanged);

    public static readonly BindableProperty MonthAppointmentTemplateProperty = BindableProperty.Create(
        nameof(MonthAppointmentTemplate), typeof(DataTemplate), typeof(SchedulerView), null,
        propertyChanged: OnAppointmentTemplateChanged);

    public static readonly BindableProperty MonthOverflowFormatProperty = BindableProperty.Create(
        nameof(MonthOverflowFormat), typeof(string), typeof(SchedulerView), "+{0} more",
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty CellSelectionTemplateProperty = BindableProperty.Create(
        nameof(CellSelectionTemplate), typeof(DataTemplate), typeof(SchedulerView), null,
        propertyChanged: OnCellSelectionTemplateChanged);

    public static readonly BindableProperty SelectedSlotProperty = BindableProperty.Create(
        nameof(SelectedSlot), typeof(SchedulerTimeSlot?), typeof(SchedulerView), null,
        BindingMode.TwoWay, propertyChanged: OnSelectedSlotChanged);

    public static readonly BindableProperty StartHourProperty = BindableProperty.Create(
        nameof(StartHour), typeof(int), typeof(SchedulerView), 8, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty EndHourProperty = BindableProperty.Create(
        nameof(EndHour), typeof(int), typeof(SchedulerView), 23, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty HourHeightProperty = BindableProperty.Create(
        nameof(HourHeight), typeof(double), typeof(SchedulerView), 50d, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty VisibleDaysProperty = BindableProperty.Create(
        nameof(VisibleDays), typeof(int), typeof(SchedulerView), 7, propertyChanged: OnVisibleDaysChanged);

    public static readonly BindableProperty FirstDayOfWeekProperty = BindableProperty.Create(
        nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(SchedulerView), DayOfWeek.Monday,
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty TimeZoneProperty = BindableProperty.Create(
        nameof(TimeZone), typeof(TimeZoneInfo), typeof(SchedulerView), TimeZoneInfo.Local,
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty TimeFormatProperty = BindableProperty.Create(
        nameof(TimeFormat), typeof(string), typeof(SchedulerView), "HH:mm",
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty TimeGutterWidthProperty = BindableProperty.Create(
        nameof(TimeGutterWidth), typeof(double), typeof(SchedulerView), 52d, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty HeaderHeightProperty = BindableProperty.Create(
        nameof(HeaderHeight), typeof(double), typeof(SchedulerView), 52d, propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty SnapMinutesProperty = BindableProperty.Create(
        nameof(SnapMinutes), typeof(int), typeof(SchedulerView), 15);

    public static readonly BindableProperty AllowDragAndDropProperty = BindableProperty.Create(
        nameof(AllowDragAndDrop), typeof(bool), typeof(SchedulerView), true);

    public static readonly BindableProperty AllowDragAcrossPeriodsProperty = BindableProperty.Create(
        nameof(AllowDragAcrossPeriods), typeof(bool), typeof(SchedulerView), true);

    public static readonly BindableProperty ShowDragTimeIndicatorProperty = BindableProperty.Create(
        nameof(ShowDragTimeIndicator), typeof(bool), typeof(SchedulerView), true);

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(SchedulerView), false, propertyChanged: OnIsBusyChanged);

    public static readonly BindableProperty GridBackgroundColorProperty = BindableProperty.Create(
        nameof(GridBackgroundColor), typeof(Color), typeof(SchedulerView), Colors.White,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty ShowNonWorkingDaysShadingProperty = BindableProperty.Create(
        nameof(ShowNonWorkingDaysShading), typeof(bool), typeof(SchedulerView), true,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty ShowCurrentDayHighlightProperty = BindableProperty.Create(
        nameof(ShowCurrentDayHighlight), typeof(bool), typeof(SchedulerView), true,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty ShowNonWorkingHoursShadingProperty = BindableProperty.Create(
        nameof(ShowNonWorkingHoursShading), typeof(bool), typeof(SchedulerView), false,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty WorkingDaysProperty = BindableProperty.Create(
        nameof(WorkingDays), typeof(IReadOnlyCollection<DayOfWeek>), typeof(SchedulerView), DefaultWorkingDays,
        propertyChanged: OnAppearanceChanged,
        // A null would only surface later, as an exception thrown inside a draw pass rather than on
        // the caller's stack. A host that clears working days passes an empty collection instead.
        coerceValue: static (_, value) => value ?? DefaultWorkingDays);

    public static readonly BindableProperty WorkingHoursStartProperty = BindableProperty.Create(
        nameof(WorkingHoursStart), typeof(TimeOnly), typeof(SchedulerView), new TimeOnly(9, 0),
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty WorkingHoursEndProperty = BindableProperty.Create(
        nameof(WorkingHoursEnd), typeof(TimeOnly), typeof(SchedulerView), new TimeOnly(17, 0),
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty GridLineColorProperty = AppearanceColor(
        nameof(GridLineColor), "#E0E0E0");

    public static readonly BindableProperty MinorGridLineColorProperty = AppearanceColor(
        nameof(MinorGridLineColor), "#F0F0F0");

    public static readonly BindableProperty PrimaryTextColorProperty = AppearanceColor(
        nameof(PrimaryTextColor), "#212121");

    public static readonly BindableProperty SecondaryTextColorProperty = AppearanceColor(
        nameof(SecondaryTextColor), "#6E6E6E");

    public static readonly BindableProperty NonWorkingDaysBackgroundColorProperty = AppearanceColor(
        nameof(NonWorkingDaysBackgroundColor), "#FAFAFA");

    public static readonly BindableProperty NonWorkingHoursBackgroundColorProperty = AppearanceColor(
        nameof(NonWorkingHoursBackgroundColor), "#FAFAFA");

    public static readonly BindableProperty CurrentDayBackgroundColorProperty = AppearanceColor(
        nameof(CurrentDayBackgroundColor), "#F3E8FC");

    public static readonly BindableProperty CurrentDayTextColorProperty = AppearanceColor(
        nameof(CurrentDayTextColor), "#4458C8");

    public static readonly BindableProperty CurrentTimeIndicatorColorProperty = AppearanceColor(
        nameof(CurrentTimeIndicatorColor), "#FD4225");

    public static readonly BindableProperty AdjacentMonthBackgroundColorProperty = AppearanceColor(
        nameof(AdjacentMonthBackgroundColor), "#F5F5F5");

    public static readonly BindableProperty AdjacentMonthTextColorProperty = AppearanceColor(
        nameof(AdjacentMonthTextColor), "#B0B0B0");

    public static readonly BindableProperty CellSelectionBackgroundColorProperty = AppearanceColor(
        nameof(CellSelectionBackgroundColor), "#F3E8FC");

    public static readonly BindableProperty CellSelectionBorderColorProperty = AppearanceColor(
        nameof(CellSelectionBorderColor), "#DAB8F4");

    public static readonly BindableProperty CellSelectionTextColorProperty = AppearanceColor(
        nameof(CellSelectionTextColor), "#6B3FA0");

    public static readonly BindableProperty DragTimeIndicatorBackgroundColorProperty = AppearanceColor(
        nameof(DragTimeIndicatorBackgroundColor), "#212121");

    public static readonly BindableProperty DragTimeIndicatorTextColorProperty = AppearanceColor(
        nameof(DragTimeIndicatorTextColor), "#FFFFFF");

    public static readonly BindableProperty BusyIndicatorColorProperty = BindableProperty.Create(
        nameof(BusyIndicatorColor), typeof(Color), typeof(SchedulerView), null,
        propertyChanged: OnAppearanceChanged);

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
    /// Whether the control shows columns of hours or a calendar month.
    /// </summary>
    /// <remarks>
    /// The two are different surfaces rather than different settings of one: a month has no time
    /// gutter, does not scroll, and ignores <see cref="VisibleDays"/>, <see cref="StartHour"/>,
    /// <see cref="EndHour"/> and <see cref="HourHeight"/>. Dragging is not offered in a month.
    /// </remarks>
    public SchedulerViewMode ViewMode
    {
        get => (SchedulerViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
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

    /// <summary>
    /// Template for an appointment chip in a month cell.
    /// </summary>
    /// <remarks>
    /// A separate template because a chip is a single line a few device-independent units tall,
    /// whereas an appointment box is sized by its duration; one template rarely reads well at both.
    /// Falls back to <see cref="AppointmentTemplate"/> when not set.
    /// </remarks>
    public DataTemplate? MonthAppointmentTemplate
    {
        get => (DataTemplate?)GetValue(MonthAppointmentTemplateProperty);
        set => SetValue(MonthAppointmentTemplateProperty, value);
    }

    /// <summary>
    /// Composed with the number of appointments a month cell could not show, e.g. <c>"+3 more"</c>.
    /// </summary>
    public string MonthOverflowFormat
    {
        get => (string)GetValue(MonthOverflowFormatProperty);
        set => SetValue(MonthOverflowFormatProperty, value);
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

    /// <summary>
    /// How many days a page shows: 7 for a week, 3 for a three-day view, 1 for a single day.
    /// </summary>
    /// <remarks>
    /// Anything from 1 to 7 works — 5 gives a working week. Only a full week snaps to
    /// <see cref="FirstDayOfWeek"/>; shorter pages start on <see cref="DisplayDate"/>, and swiping
    /// moves by exactly one page. Ignored while <see cref="ViewMode"/> is
    /// <see cref="SchedulerViewMode.Month"/>.
    /// </remarks>
    public int VisibleDays
    {
        get => (int)GetValue(VisibleDaysProperty);
        set => SetValue(VisibleDaysProperty, value);
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
    /// Fill behind the grid and the time gutter. It is an opaque colour rather than transparent so the
    /// drawing surface reliably receives the taps that select an empty cell.
    /// </summary>
    public Color GridBackgroundColor
    {
        get => (Color)GetValue(GridBackgroundColorProperty);
        set => SetValue(GridBackgroundColorProperty, value);
    }

    /// <summary>Whether days outside <see cref="WorkingDays"/> receive a distinct background.</summary>
    public bool ShowNonWorkingDaysShading
    {
        get => (bool)GetValue(ShowNonWorkingDaysShadingProperty);
        set => SetValue(ShowNonWorkingDaysShadingProperty, value);
    }

    /// <summary>Whether today's background and day number are emphasised.</summary>
    /// <remarks>The current-time indicator is independent and remains visible.</remarks>
    public bool ShowCurrentDayHighlight
    {
        get => (bool)GetValue(ShowCurrentDayHighlightProperty);
        set => SetValue(ShowCurrentDayHighlightProperty, value);
    }

    /// <summary>Whether time outside the working interval is shaded on working days.</summary>
    /// <remarks>Only applies to the timeline; a month has no hours.</remarks>
    public bool ShowNonWorkingHoursShading
    {
        get => (bool)GetValue(ShowNonWorkingHoursShadingProperty);
        set => SetValue(ShowNonWorkingHoursShadingProperty, value);
    }

    /// <summary>The recurring days regarded as working days.</summary>
    /// <remarks>
    /// Replace the collection to report a change; in-place mutations are not observed. An empty
    /// collection makes every day non-working; <see langword="null"/> restores the default week.
    /// </remarks>
    public IReadOnlyCollection<DayOfWeek> WorkingDays
    {
        get => (IReadOnlyCollection<DayOfWeek>)GetValue(WorkingDaysProperty);
        set => SetValue(WorkingDaysProperty, value);
    }

    /// <summary>Beginning of the same-day working interval.</summary>
    [System.ComponentModel.TypeConverter(typeof(TimeOnlyTypeConverter))]
    public TimeOnly WorkingHoursStart
    {
        get => (TimeOnly)GetValue(WorkingHoursStartProperty);
        set => SetValue(WorkingHoursStartProperty, value);
    }

    /// <summary>End of the same-day working interval.</summary>
    /// <remarks>Equal or earlier than <see cref="WorkingHoursStart"/> suppresses hour shading.</remarks>
    [System.ComponentModel.TypeConverter(typeof(TimeOnlyTypeConverter))]
    public TimeOnly WorkingHoursEnd
    {
        get => (TimeOnly)GetValue(WorkingHoursEndProperty);
        set => SetValue(WorkingHoursEndProperty, value);
    }

    public Color GridLineColor
    {
        get => (Color)GetValue(GridLineColorProperty);
        set => SetValue(GridLineColorProperty, value);
    }

    public Color MinorGridLineColor
    {
        get => (Color)GetValue(MinorGridLineColorProperty);
        set => SetValue(MinorGridLineColorProperty, value);
    }

    public Color PrimaryTextColor
    {
        get => (Color)GetValue(PrimaryTextColorProperty);
        set => SetValue(PrimaryTextColorProperty, value);
    }

    public Color SecondaryTextColor
    {
        get => (Color)GetValue(SecondaryTextColorProperty);
        set => SetValue(SecondaryTextColorProperty, value);
    }

    public Color NonWorkingDaysBackgroundColor
    {
        get => (Color)GetValue(NonWorkingDaysBackgroundColorProperty);
        set => SetValue(NonWorkingDaysBackgroundColorProperty, value);
    }

    public Color NonWorkingHoursBackgroundColor
    {
        get => (Color)GetValue(NonWorkingHoursBackgroundColorProperty);
        set => SetValue(NonWorkingHoursBackgroundColorProperty, value);
    }

    public Color CurrentDayBackgroundColor
    {
        get => (Color)GetValue(CurrentDayBackgroundColorProperty);
        set => SetValue(CurrentDayBackgroundColorProperty, value);
    }

    public Color CurrentDayTextColor
    {
        get => (Color)GetValue(CurrentDayTextColorProperty);
        set => SetValue(CurrentDayTextColorProperty, value);
    }

    public Color CurrentTimeIndicatorColor
    {
        get => (Color)GetValue(CurrentTimeIndicatorColorProperty);
        set => SetValue(CurrentTimeIndicatorColorProperty, value);
    }

    public Color AdjacentMonthBackgroundColor
    {
        get => (Color)GetValue(AdjacentMonthBackgroundColorProperty);
        set => SetValue(AdjacentMonthBackgroundColorProperty, value);
    }

    public Color AdjacentMonthTextColor
    {
        get => (Color)GetValue(AdjacentMonthTextColorProperty);
        set => SetValue(AdjacentMonthTextColorProperty, value);
    }

    /// <summary>Background of the built-in selected-cell affordance. Custom templates ignore it.</summary>
    public Color CellSelectionBackgroundColor
    {
        get => (Color)GetValue(CellSelectionBackgroundColorProperty);
        set => SetValue(CellSelectionBackgroundColorProperty, value);
    }

    /// <summary>Border of the built-in selected-cell affordance. Custom templates ignore it.</summary>
    public Color CellSelectionBorderColor
    {
        get => (Color)GetValue(CellSelectionBorderColorProperty);
        set => SetValue(CellSelectionBorderColorProperty, value);
    }

    /// <summary>Text of the built-in selected-cell affordance. Custom templates ignore it.</summary>
    public Color CellSelectionTextColor
    {
        get => (Color)GetValue(CellSelectionTextColorProperty);
        set => SetValue(CellSelectionTextColorProperty, value);
    }

    public Color DragTimeIndicatorBackgroundColor
    {
        get => (Color)GetValue(DragTimeIndicatorBackgroundColorProperty);
        set => SetValue(DragTimeIndicatorBackgroundColorProperty, value);
    }

    public Color DragTimeIndicatorTextColor
    {
        get => (Color)GetValue(DragTimeIndicatorTextColorProperty);
        set => SetValue(DragTimeIndicatorTextColorProperty, value);
    }

    /// <summary><see langword="null"/> preserves the platform activity-indicator colour.</summary>
    public Color? BusyIndicatorColor
    {
        get => (Color?)GetValue(BusyIndicatorColorProperty);
        set => SetValue(BusyIndicatorColorProperty, value);
    }

    /// <summary>Raised when empty grid space is tapped.</summary>
    public event EventHandler<SchedulerCellTappedEventArgs>? CellTapped;

    public event EventHandler<SchedulerAppointmentTappedEventArgs>? AppointmentTapped;

    /// <summary>Raised before a drag begins; set <c>Cancel</c> to refuse it.</summary>
    public event EventHandler<SchedulerAppointmentDragStartingEventArgs>? AppointmentDragStarting;

    /// <summary>Raised after a drag is released; set <c>Cancel</c> to snap the appointment back.</summary>
    public event EventHandler<SchedulerAppointmentDroppedEventArgs>? AppointmentDropped;

    /// <summary>
    /// Raised when the header above a day column is tapped, carrying that day.
    /// </summary>
    /// <remarks>
    /// Not raised while <see cref="ViewMode"/> is <see cref="SchedulerViewMode.Month"/>: a month's
    /// header names weekdays, and one column stands for six dates rather than one.
    /// </remarks>
    public event EventHandler<SchedulerHeaderTappedEventArgs>? HeaderTapped;

    /// <summary>
    /// Raised when the hour gutter is tapped, carrying the time at that point.
    /// </summary>
    /// <remarks>
    /// A month has no gutter, so it never raises this.
    /// </remarks>
    public event EventHandler<SchedulerTimeGutterTappedEventArgs>? TimeGutterTapped;

    /// <summary>Raised whenever the centre week changes, including on first layout.</summary>
    public event EventHandler<SchedulerVisibleDatesChangedEventArgs>? VisibleDatesChanged;

    private static void OnDisplayDateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        if (view.suppressDisplayDateSync || !view.initialised)
            return;

        var target = view.pageSurface.StartOfPage(DateOnly.FromDateTime((DateTime)newValue));
        if (target == view.slots[1].PageStart)
            return;

        if (view.TrySlideToPage(target))
            return;

        view.RebuildAll(target);
        view.SyncDisplayDate();
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        if (oldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= view.OnItemsCollectionChanged;

        if (newValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += view.OnItemsCollectionChanged;

        view.QueueRepopulate();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRepopulate();

    /// <summary>
    /// Asks for a repopulate on the next tick, collapsing a burst of them into one.
    /// </summary>
    /// <remarks>
    /// A host that loads in chunks hands over its collection once per chunk — cached results, then
    /// fresh ones, per period — and every one of those used to rebuild all three pages immediately.
    /// A single change of view was measured doing it eight times. Nothing is lost by waiting for the
    /// tick: the data is read when the rebuild runs, not when it was announced.
    /// <para>
    /// Only the data paths come through here. Everything a drag does still repopulates synchronously,
    /// because those calls are how a gesture puts the pages back and cannot be left until later.
    /// </para>
    /// </remarks>
    private void QueueRepopulate()
    {
        if (repopulateQueued)
            return;

        repopulateQueued = true;

        Dispatcher.Dispatch(() =>
        {
            repopulateQueued = false;
            RepopulateAllSlots();
        });
    }

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

    /// <summary>
    /// Refuses a <see cref="DataTemplateSelector"/>, which this control cannot use.
    /// </summary>
    /// <remarks>
    /// <see cref="DataTemplateSelector"/> derives from <see cref="DataTemplate"/>, so one can be
    /// assigned here and would fail much later, deep inside the pool, with nothing to say for itself:
    /// a selector carries no <c>LoadTemplate</c> for <c>CreateContent</c> to call. Failing at the
    /// assignment, with the alternative named, is worth the few lines.
    /// <para>
    /// Supporting selectors would mean a pool per resolved template, and the reuse this control gets
    /// most of its speed from is why that is not obviously a win — see DESIGN.md section 6. A single
    /// template that varies its own content recycles better than several that do not.
    /// </para>
    /// </remarks>
    private static void RejectTemplateSelector(object? template)
    {
        if (template is DataTemplateSelector)
        {
            throw new NotSupportedException(
                "A DataTemplateSelector cannot be used as an appointment template. Views are pooled and "
                + "rebound rather than rebuilt, so they are all made from one template. Use a single "
                + "template that varies its own content — build every variant once and show the one "
                + "that applies — which also rebinds far more cheaply than swapping templates would. "
                + "See API.md.");
        }
    }

    private static void OnViewModeChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerView)bindable).ChangeViewMode((SchedulerViewMode)newValue);

    private static void OnAppointmentTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        RejectTemplateSelector(newValue);

        // A drag in flight has taken its view out of every page, so none of the cleanup below would
        // reach it: it is in no slot, not in the pool's spares, and RepopulateAllSlots declines to
        // run while a drag is armed. Left alone it survives the template change and is later handed
        // back into a pool that no longer matches it. ChangeViewMode has always done this first.
        view.CancelDragCandidate();

        foreach (var slot in view.slots)
            view.ReleaseSlot(slot);

        if (view.dragOverlayView is not null)
        {
            view.dragOverlay.Remove(view.dragOverlayView);
            view.dragOverlayView = null;
        }

        view.pool.Clear();
        view.pool.Template = view.ActiveTemplate;
        view.RepopulateAllSlots();
    }

    private static void OnCellSelectionTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        view.cellSelection.Template = (DataTemplate?)newValue;
        view.cellSelection.Reset();
        view.UpdateSelectionView();
    }

    private static void OnSelectedSlotChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerView)bindable).UpdateSelectionView();

    private static void OnIsBusyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;
        view.busyIndicator.IsVisible = (bool)newValue;
        view.busyIndicator.IsRunning = (bool)newValue;
    }

    private static BindableProperty AppearanceColor(string name, string value) => BindableProperty.Create(
        name, typeof(Color), typeof(SchedulerView), Color.FromArgb(value), propertyChanged: OnAppearanceChanged);

    private static void OnAppearanceChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerView)bindable).ApplyAppearance();
}
