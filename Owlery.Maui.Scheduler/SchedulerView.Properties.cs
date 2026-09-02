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

    public static readonly BindableProperty AgendaAppointmentTemplateProperty = BindableProperty.Create(
        nameof(AgendaAppointmentTemplate), typeof(DataTemplate), typeof(SchedulerView), null,
        propertyChanged: OnAppointmentTemplateChanged);

    public static readonly BindableProperty AgendaSectionTemplateProperty = BindableProperty.Create(
        nameof(AgendaSectionTemplate), typeof(DataTemplate), typeof(SchedulerView), null,
        propertyChanged: OnAgendaSectionTemplateChanged);

    public static readonly BindableProperty AgendaEstimatedRowHeightProperty = BindableProperty.Create(
        nameof(AgendaEstimatedRowHeight), typeof(double), typeof(SchedulerView), 64d,
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty AgendaRowHeightProperty = BindableProperty.Create(
        nameof(AgendaRowHeight), typeof(Func<ISchedulerAppointment, double>), typeof(SchedulerView), null,
        propertyChanged: OnAgendaRowHeightChanged);

    public static readonly BindableProperty AgendaDayGutterWidthProperty = BindableProperty.Create(
        nameof(AgendaDayGutterWidth), typeof(double), typeof(SchedulerView), 56d,
        propertyChanged: OnGeometryChanged);

    public static readonly BindableProperty AgendaEmptyTextProperty = BindableProperty.Create(
        nameof(AgendaEmptyText), typeof(string), typeof(SchedulerView), "No appointments",
        propertyChanged: OnAgendaEmptyTextChanged);

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

    public static readonly BindableProperty SlotMinutesProperty = BindableProperty.Create(
        nameof(SlotMinutes), typeof(int), typeof(SchedulerView), 15);

    public static readonly BindableProperty DragSnapMinutesProperty = BindableProperty.Create(
        nameof(DragSnapMinutes), typeof(int), typeof(SchedulerView), 15);

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

    public static readonly BindableProperty MinorGridLineMinutesProperty = BindableProperty.Create(
        nameof(MinorGridLineMinutes), typeof(int), typeof(SchedulerView), 30,
        propertyChanged: OnAppearanceChanged);

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
    /// moves by exactly one page. Ignored unless <see cref="ViewMode"/> is
    /// <see cref="SchedulerViewMode.Timeline"/>.
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

    /// <summary>How long a slot is — the length of what a tap on empty grid selects.</summary>
    /// <remarks>
    /// This is the reported <c>Duration</c>, not merely a rounding: at <c>60</c> the timeline is
    /// divided into hour-long slots and a tap anywhere inside one selects that whole hour. The start
    /// therefore falls <em>down</em> to the slot containing the point touched, never past it — which
    /// is what separates this from <see cref="DragSnapMinutes"/>, where a drop is pulled to the
    /// nearest boundary in either direction. Also the granularity of the time reported by
    /// <see cref="TimeGutterTapped"/>. A month cell ignores it and reports a whole day.
    /// </remarks>
    public int SlotMinutes
    {
        get => (int)GetValue(SlotMinutesProperty);
        set => SetValue(SlotMinutesProperty, value);
    }

    /// <summary>Granularity a dragged appointment lands on.</summary>
    /// <remarks>
    /// Rounded to the <em>nearest</em> boundary rather than down: a drop should go where the
    /// appointment looks like it is going. Separate from <see cref="SlotMinutes"/>
    /// because picking a time and moving an existing booking are different acts — a host may want to
    /// offer whole hours to book while still letting an existing lesson be nudged by a quarter.
    /// </remarks>
    public int DragSnapMinutes
    {
        get => (int)GetValue(DragSnapMinutesProperty);
        set => SetValue(DragSnapMinutesProperty, value);
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

    /// <summary>How often the timeline is marked inside the hour, in minutes.</summary>
    /// <remarks>
    /// <c>30</c> draws the half hour, <c>15</c> the quarters. <c>60</c> is how you ask for hour lines
    /// only — at that spacing every mark would land on an hour line, so none is drawn. Zero and
    /// negative values are treated the same way rather than throwing. Independent of
    /// <see cref="DragSnapMinutes"/>: this is what the grid shows, that is what a drag lands on.
    /// </remarks>
    public int MinorGridLineMinutes
    {
        get => (int)GetValue(MinorGridLineMinutesProperty);
        set => SetValue(MinorGridLineMinutesProperty, value);
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

    /// <summary>The template each agenda row is built from.</summary>
    /// <remarks>
    /// Falls back to <see cref="AppointmentTemplate"/> when unset, which renders but rarely reads
    /// well: a timeline box is as tall as its appointment is long, whereas an agenda row is as tall
    /// as its own content requires and usually wants to state its time, having no hour gutter to read
    /// it from.
    /// </remarks>
    public DataTemplate? AgendaAppointmentTemplate
    {
        get => (DataTemplate?)GetValue(AgendaAppointmentTemplateProperty);
        set => SetValue(AgendaAppointmentTemplateProperty, value);
    }

    /// <summary>The template the agenda's headings and day markers are built from.</summary>
    /// <remarks>
    /// One template for all three kinds, bound to a <see cref="SchedulerAgendaSection"/> and branching
    /// on its <see cref="SchedulerAgendaSection.Kind"/>. Unset, a plain built-in heading is used.
    /// </remarks>
    public DataTemplate? AgendaSectionTemplate
    {
        get => (DataTemplate?)GetValue(AgendaSectionTemplateProperty);
        set => SetValue(AgendaSectionTemplateProperty, value);
    }

    /// <summary>What an agenda row is assumed to be worth before it has been measured.</summary>
    /// <remarks>
    /// Only an estimate: a row is measured the first time it is built and the list corrects itself.
    /// Setting it close to the truth keeps that correction small and the scrollbar honest from the
    /// start; setting it wildly wrong is a visual settling, not an error.
    /// </remarks>
    public double AgendaEstimatedRowHeight
    {
        get => (double)GetValue(AgendaEstimatedRowHeightProperty);
        set => SetValue(AgendaEstimatedRowHeightProperty, value);
    }

    /// <summary>
    /// Returns an authoritative height for an agenda appointment row, bypassing view measurement.
    /// </summary>
    /// <remarks>
    /// Unset, each row is measured the first time it is realized and the list corrects itself. Set
    /// this to state the height from the appointment alone. Setting it is <b>highly recommended</b>
    /// for an agenda — and especially when the template's height is content-dependent — because it
    /// removes the measure-and-correct pass from the scroll path. Left unmeasured, rows are placed at
    /// <see cref="AgendaEstimatedRowHeight"/>, then measured and reflowed as they appear, which on a
    /// live scroll reads as the row content wobbling up and down for a frame or so; a known height
    /// also keeps the scrollbar extent honest from the first render.
    /// <para>
    /// The returned value is authoritative: the agenda lays the row out at exactly that height, then
    /// arranges the row's own content within it. It is clamped to the agenda's minimum row height, and
    /// a non-finite result falls back to <see cref="AgendaEstimatedRowHeight"/>. Because the height is
    /// fixed, a value too short clips the content and one too long leaves white space, so state the
    /// template's height as it actually renders and err slightly generous rather than tight.
    /// </para>
    /// <para>
    /// Templates whose height depends on available width, wrapping, font scale or other view state
    /// cannot be sized from the appointment alone and should keep the default measured behaviour —
    /// which is why the measured path remains the default rather than being removed.
    /// </para>
    /// </remarks>
    public Func<ISchedulerAppointment, double>? AgendaRowHeight
    {
        get => (Func<ISchedulerAppointment, double>?)GetValue(AgendaRowHeightProperty);
        set => SetValue(AgendaRowHeightProperty, value);
    }

    /// <summary>Width of the column down the agenda's leading edge that day markers sit in.</summary>
    public double AgendaDayGutterWidth
    {
        get => (double)GetValue(AgendaDayGutterWidthProperty);
        set => SetValue(AgendaDayGutterWidthProperty, value);
    }

    /// <summary>Text the built-in month heading shows when that agenda month has no appointments.</summary>
    /// <remarks>A custom <see cref="AgendaSectionTemplate"/> owns its empty presentation.</remarks>
    public string AgendaEmptyText
    {
        get => (string?)GetValue(AgendaEmptyTextProperty) ?? string.Empty;
        set => SetValue(AgendaEmptyTextProperty, value);
    }

    /// <summary>Raised when empty grid space is tapped.</summary>
    public event EventHandler<SchedulerCellTappedEventArgs>? CellTapped;

    public event EventHandler<SchedulerAppointmentTappedEventArgs>? AppointmentTapped;

    /// <summary>Raised before a drag begins; set <c>Cancel</c> to refuse it.</summary>
    public event EventHandler<SchedulerAppointmentDragStartingEventArgs>? AppointmentDragStarting;

    /// <summary>Raised while dragging, each time the drop target moves to a different boundary.</summary>
    public event EventHandler<SchedulerAppointmentDropTargetChangedEventArgs>? AppointmentDropTargetChanged;

    public event EventHandler<SchedulerAppointmentDroppedEventArgs>? AppointmentDropped;

    /// <summary>
    /// Raised when the header above a day column is tapped, carrying that day.
    /// </summary>
    /// <remarks>
    /// Raised only in <see cref="SchedulerViewMode.Timeline"/>. A month header names weekdays rather
    /// than dates, and an agenda names its days down the list instead of in a header strip.
    /// </remarks>
    public event EventHandler<SchedulerHeaderTappedEventArgs>? HeaderTapped;

    /// <summary>
    /// Raised when the hour gutter is tapped, carrying the time at that point.
    /// </summary>
    /// <remarks>
    /// Raised only in <see cref="SchedulerViewMode.Timeline"/>; the other surfaces have no hour
    /// gutter.
    /// </remarks>
    public event EventHandler<SchedulerTimeGutterTappedEventArgs>? TimeGutterTapped;

    /// <summary>Raised whenever the visible period changes, including on first layout.</summary>
    public event EventHandler<SchedulerVisibleDatesChangedEventArgs>? VisibleDatesChanged;

    private static void OnDisplayDateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;


        if (view.suppressDisplayDateSync || !view.initialised)
            return;

        var target = view.pageSurface.StartOfPage(DateOnly.FromDateTime((DateTime)newValue));
        if (target == view.slots[1].PageStart)
        {
            if (view.ViewMode is SchedulerViewMode.Agenda)
                view.ScrollAgendaToDate(DateOnly.FromDateTime((DateTime)newValue));

            return;
        }

        if (view.TrySlideToPage(target))
            return;

        if (view.ViewMode is SchedulerViewMode.Agenda)
            view.agendaNavigationInProgress = true;

        view.RebuildAll(target);

        if (view.ViewMode is SchedulerViewMode.Agenda)
        {
            view.ScrollAgendaToDate(DateOnly.FromDateTime((DateTime)newValue));
            return;
        }

        view.SyncDisplayDate();
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        if (oldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= view.OnItemsCollectionChanged;

        if (newValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += view.OnItemsCollectionChanged;

        view.agendaSurface.Invalidate();
        view.QueueRepopulate();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        agendaSurface.Invalidate();
        QueueRepopulate();
    }

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

        // The agenda's row table is built from the items, so new items make it stale. Scrolling does
        // not — that only moves the window over a table that is still good.
        agendaSurface.Invalidate();

        // Items arriving is the host answering whatever the last growth asked for: the range it was
        // asked to cover can now be laid out, and the edge may ask again.
        agendaSurface.AdoptRequestedRange();
        forwardGrowAwaitingData = false;

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
    /// most of its speed from is why that is not obviously a win — see
    /// docs/design/appointment-views.md. A single template that varies its own content recycles
    /// better than several that do not.
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
                + "See docs/API.md.");
        }
    }

    private static void OnViewModeChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SchedulerView)bindable).ChangeViewMode((SchedulerViewMode)newValue);

    private static void OnAgendaSectionTemplateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        RejectTemplateSelector(newValue);

        foreach (var slot in view.slots)
            view.ReleaseSectionViews(slot);

        view.agendaSurface.Invalidate(clearMeasurements: true);
        view.sectionPool.Clear();
        view.sectionPool.Template = view.ActiveSectionTemplate;
        view.RepopulateAllSlots();
    }

    private static void OnAgendaRowHeightChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SchedulerView)bindable;

        view.agendaSurface.RowHeightResolver = (Func<ISchedulerAppointment, double>?)newValue;
        view.agendaSurface.Invalidate(clearMeasurements: true);
        view.RepopulateAllSlots();
    }

    private static void OnAgendaEmptyTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var scheduler = (SchedulerView)bindable;

        foreach (var section in scheduler.slots
                     .SelectMany(slot => slot.SectionViews)
                     .OfType<AgendaSectionView>())
        {
            section.EmptyText = newValue as string ?? string.Empty;
            section.Refresh();
        }
    }

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

        if (view.ViewMode is SchedulerViewMode.Agenda)
            view.agendaSurface.Invalidate(clearMeasurements: true);

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
