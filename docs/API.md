# Owlery.Maui.Scheduler — API reference

A scheduler control for .NET MAUI. This document covers the public surface. For *what it
does*, see the [functional requirements](requirements/README.md); for *why* it is built the way it is,
see the [design notes](design/README.md).

Namespace: `Owlery.Maui.Scheduler`

---

## Contents

- [Setup](#setup)
- [Quick start](#quick-start)
- [SchedulerView](#schedulerweekview)
  - [View mode](#view-mode)
  - [Data and templates](#data-and-templates)
  - [Time window and layout](#time-window-and-layout)
  - [Month layout](#month-layout)
  - [Agenda layout](#agenda-layout)
  - [Interaction](#interaction)
  - [Appearance and state](#appearance-and-state)
  - [Events](#events)
  - [Methods](#methods)
- [Contracts](#contracts)
  - [`ISchedulerAppointment`](#ischedulerappointment)
  - [`SchedulerTimeSlot`](#schedulertimeslot)
  - [`SchedulerAppointmentCollection<T>`](#schedulerappointmentcollectiont)
- [Event argument types](#event-argument-types)
- [Using it from Blazor](#using-it-from-blazor)
- [Behaviour worth knowing](#behaviour-worth-knowing)

---

`Owlery.Maui.Scheduler` is a plain .NET MAUI control library. Its only package reference is
`Microsoft.Maui.Controls`, and any MAUI host can use it — templates are ordinary `DataTemplate`s, the
public surface is bindable properties and CLR events. `Owlery.Maui.Scheduler.Sample` is a bare MAUI
app showing exactly that, with no Blazor involved.

## Setup

The control ships one platform handler of its own, and MAUI gives a library no way to register a
handler on its own behalf. Add this line to `MauiProgram`:

```csharp
builder
    .UseMauiApp<App>()
    .UseOwleryScheduler();
```

Without it the horizontal pager falls back to the handler for its base type and does not scroll. It
is the only setup the control requires; everything behind it is internal.

## Quick start

```csharp
var scheduler = new SchedulerView
{
    TimeZone = TimeZoneInfo.Local,
    StartHour = 8,
    EndHour = 23,
    AppointmentTemplate = new DataTemplate(() => new MyAppointmentView())
};

scheduler.VisibleDatesChanged += async (_, e) =>
    scheduler.ItemsSource = await LoadAsync(e.PrefetchFrom, e.PrefetchTo);

scheduler.AppointmentTapped += (_, e) => OpenDetail(e.Appointment);
```

The control fetches nothing itself. It tells you which dates it needs through `VisibleDatesChanged`;
you assign `ItemsSource`.

---

## SchedulerView

`public class SchedulerView : ContentView`

Every property below is backed by a `BindableProperty` named `<PropertyName>Property`.

### View mode

| Property | Type | Default | Description |
|---|---|---|---|
| `ViewMode` | `SchedulerViewMode` | `Timeline` | Whether the control shows columns of hours, a calendar month, or a grouped agenda. |

```csharp
public enum SchedulerViewMode { Timeline, Month, Agenda }
```

The three are different surfaces rather than settings of one, so a fair amount does not carry over.
In `Month`:

- `VisibleDays`, `StartHour`, `EndHour`, `HourHeight` and `TimeGutterWidth` are ignored. There is no
  hour gutter, and a month is never taller than the viewport — it does not scroll.
- Every appointment on a day is shown regardless of the hour it starts at, whereas the timeline clips
  to `StartHour`..`EndHour`.
- Dragging is not offered, whatever `AllowDragAndDrop` says, so `AppointmentDragStarting` and
  `AppointmentDropped` are never raised. `ScrollToTime` does nothing.
- `CellTapped` reports a slot a **day** long rather than `SlotMinutes` — a month cell has no finer
  target. The `Duration` is what tells the two apart.
- `VisibleDatesChanged` reports all 42 days of the grid, and its prefetch range spans the grids either
  side — roughly four months rather than three weeks. Size your queries accordingly.

The grid is always six rows, so a short month beginning on `FirstDayOfWeek` shows two full rows of the
next one. Leading and trailing days from the neighbouring months are drawn muted but behave normally:
they hold appointments and can be tapped.

In `Agenda`:

- One continuous vertical list replaces the horizontal pager, day-header strip and hour gutter.
- Full-width month headings are always present. Non-empty weeks get an inset heading, empty days are
  omitted, and each non-empty day gets one marker in the leading gutter.
- Appointment rows are content-sized and virtualized. A row is estimated until it first appears,
  then measured; views outside the viewport buffer return to the same pool used by the other modes.
- Approaching either vertical edge extends the loaded range by another month. Prepending keeps the
  row under the reader fixed while the new range and any later-arriving data are inserted above it.
- Dragging and selected-cell chrome are not offered. `HeaderTapped` and `TimeGutterTapped` are silent.
- `VisibleDatesChanged.VisibleDates` is the whole loaded agenda range, not only the pixels currently
  in the viewport; every date in that range is reachable by vertical scrolling.

### Data and templates

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable<ISchedulerAppointment>?` | `null` | Every appointment the host has loaded, across as many weeks as it likes. The control selects what belongs to each rendered week. Honours `INotifyCollectionChanged` — an in-place change refreshes, as does assigning a new collection instance. The control filters the visible period itself, so keep the whole loaded range in one collection and change it in place; `SchedulerAppointmentCollection<T>` adds the range methods for doing that in one event. |
| `AppointmentTemplate` | `DataTemplate?` | `null` | Template for one appointment box. Its binding context is the `ISchedulerAppointment`. The grid still draws without it, but no appointments appear. |
| `MonthAppointmentTemplate` | `DataTemplate?` | `null` | Template for one appointment chip in a month cell. Falls back to `AppointmentTemplate` when not set — which renders, but rarely reads well: a chip is one line about 16 units tall, not a box sized by its duration. |
| `AgendaAppointmentTemplate` | `DataTemplate?` | `null` | Template for one content-sized agenda row. Falls back to `AppointmentTemplate` when not set. |
| `AgendaSectionTemplate` | `DataTemplate?` | `null` | One template for month, week and day chrome, bound to `SchedulerAgendaSection`. A built-in section view is used when unset. |
| `CellSelectionTemplate` | `DataTemplate?` | `null` | Optional template for the selected-cell affordance, bound to the selected `SchedulerTimeSlot`. In a month it is stretched across the whole day cell. When `null` the control draws a bordered **+** box. |

> Views created from `AppointmentTemplate` are **pooled and rebound**, never rebuilt. A template must
> therefore tolerate its binding context changing — build the whole subtree up front and toggle it,
> rather than assuming one instance per appointment.

> **A `DataTemplateSelector` is not accepted** and throws where it is assigned. Every view comes from
> one template so that any view can be reused for any appointment. Vary the content *inside* a single
> template instead: build each variant once and show the one that applies.
>
> ```csharp
> protected override void OnBindingContextChanged()
> {
>     base.OnBindingContextChanged();
>
>     if (BindingContext is not MyAppointment appointment)
>         return;
>
>     logo.IsVisible = appointment.IsExternal;      // built in the constructor,
>     subject.IsVisible = !appointment.IsExternal;  // shown or hidden here
> }
> ```
>
> This is faster as well as simpler. Switching between variants inside a live view constructs nothing,
> so it costs a rebind (~0.25ms) rather than a fresh build (~3ms) — see [design §6](design/appointment-views.md).

> **Hand back the same instance for an appointment that has not changed.** Binding contexts are
> compared by reference, so an unchanged appointment that arrives as a new object re-renders its
> template for nothing, and a host that reloads in chunks pays that for everything on screen on every
> chunk. Conversely an appointment that *has* changed must be a **new** instance: mutating one in
> place changes nothing the control can see, and it will keep showing — and describing — the old
> values. Treat them as immutable snapshots.

> A template built from MAUI primitives binds roughly a third faster than the equivalent written as a
> Blazor component through generated bindings — measured per *fresh* bind, which is what a page
> rotation pays for. Rebinding a reused view is unaffected. See [design §6](design/appointment-views.md) if that matters
> to you; either kind of template works.

> Assigning `ItemsSource` repeatedly is fine. Several assignments arriving together are collapsed into
> a single rebuild on the next tick, so loading in chunks costs one rebuild rather than one per chunk.

### Time window and layout

| Property | Type | Default | Description |
|---|---|---|---|
| `StartHour` | `int` | `8` | First hour shown on the timeline. |
| `EndHour` | `int` | `23` | Last hour shown. Content height is `(EndHour - StartHour) * HourHeight`. |
| `HourHeight` | `double` | `50` | Height in device-independent pixels of one hour row. |
| `VisibleDays` | `int` | `7` | How many days a timeline page shows. `7` is a week, `3` a three-day view, `1` a single day; `5` gives a working week. Changing it animates the columns to their new width. Ignored outside `Timeline`. |
| `FirstDayOfWeek` | `DayOfWeek` | `Monday` | Which day starts the week. Only applies when `VisibleDays` is 7 — shorter pages start on `DisplayDate` instead, which is what puts today in the leading column. |
| `TimeGutterWidth` | `double` | `52` | Width of the fixed left column holding the hour labels. |
| `HeaderHeight` | `double` | `52` | Height of the day-name/day-number strip above the grid. |
| `TimeFormat` | `string` | `"HH:mm"` | Format string for hour-gutter labels and the drag time indicator. |
| `TimeZone` | `TimeZoneInfo` | `TimeZoneInfo.Local` | The zone every `DateTime` crossing this API is expressed in. Used to place the current-time line and decide which column is today. |

Appointments falling outside `StartHour`–`EndHour` are clipped to the visible window; an appointment
running past midnight is clipped to its own day.

**Changing any property in this group rebuilds all three rendered weeks.** They are configuration, not
per-frame state — set them once rather than animating them.

### Working time and shading

| Property | Type | Default | Description |
|---|---|---|---|
| `ShowNonWorkingDaysShading` | `bool` | `true` | Whether days absent from `WorkingDays` have a distinct background in timeline and month views. |
| `ShowCurrentDayHighlight` | `bool` | `true` | Whether today's background and day number are emphasised. In an agenda the background runs through today's appointment rows. Does not control the current-time indicator. |
| `ShowNonWorkingHoursShading` | `bool` | `false` | Whether time before and after the working interval is shaded on working days. Ignored in month view. |
| `WorkingDays` | `IReadOnlyCollection<DayOfWeek>` | Monday–Friday | Days regarded as working. Replace the collection to report a change; in-place mutations are not observed. An empty collection makes every day non-working, and `null` restores the default week rather than throwing later from inside a draw. |
| `WorkingHoursStart` | `TimeOnly` | `09:00` | Beginning of the same-day working interval. |
| `WorkingHoursEnd` | `TimeOnly` | `17:00` | End of the same-day working interval. Equal or earlier than `WorkingHoursStart` suppresses hour shading rather than throwing. |

On a working current day, non-working-hour bands are painted over the current-day background so both
states remain visible. A current non-working day keeps the current-day background. Overnight working
intervals and date-specific exceptions are not modelled.

The whole group is settable from XAML. The endpoints take invariant `HH:mm` text, so a page reads the
same on every device regardless of the phone's locale:

```xml
<scheduler:SchedulerView WorkingHoursStart="09:00"
                         WorkingHoursEnd="17:30"
                         ShowNonWorkingHoursShading="True">
    <scheduler:SchedulerView.WorkingDays>
        <x:Array Type="{x:Type sys:DayOfWeek}">
            <sys:DayOfWeek>Monday</sys:DayOfWeek>
            <sys:DayOfWeek>Tuesday</sys:DayOfWeek>
            <sys:DayOfWeek>Wednesday</sys:DayOfWeek>
            <sys:DayOfWeek>Thursday</sys:DayOfWeek>
        </x:Array>
    </scheduler:SchedulerView.WorkingDays>
</scheduler:SchedulerView>
```

with `xmlns:sys="clr-namespace:System;assembly=System.Runtime"`.

### Month layout

Only meaningful while `ViewMode` is `Month`.

| Property | Type | Default | Description |
|---|---|---|---|
| `MonthOverflowFormat` | `string` | `"+{0} more"` | Composed with the number of appointments a day could not show. Cell capacity comes from the control's height; when a day has more than fits, the last line becomes this marker, so one *fewer* appointment is shown than would physically fit and `{0}` counts the one the marker displaced. |

### Agenda layout

Only meaningful while `ViewMode` is `Agenda`.

| Property | Type | Default | Description |
|---|---|---|---|
| `AgendaEstimatedRowHeight` | `double` | `64` | Estimated row height before the row first appears. The measured height is cached by appointment `Key`; a close estimate keeps the initial scrollbar extent accurate and minimizes correction. |
| `AgendaRowHeight` | `Func<ISchedulerAppointment, double>?` | `null` | Authoritative row-height function. When set, the agenda lays every appointment row out at the returned height and skips view measurement and correction passes. The value is clamped to the minimum row height, and a non-finite result falls back to `AgendaEstimatedRowHeight`. Because the height is fixed the row's content is arranged within it: too short clips, too long leaves white space. Default (`null`): row is measured on first realisation and reflowed, which on a live scroll reads as the content wobbling for a frame. Use the default when height depends on width, wrapping, font scale or other view state. **Setting this is highly recommended for an agenda — see below.** |
| `AgendaDayGutterWidth` | `double` | `56` | Width of the leading gutter holding one marker beside each non-empty day. |
| `AgendaEmptyText` | `string` | `"No appointments"` | Text shown beneath the built-in heading of a month with no appointments. A custom `AgendaSectionTemplate` owns its empty presentation. |

**Setting `AgendaRowHeight` is highly recommended for an agenda.** Unlike the timeline and month
surfaces, an agenda row's height is not arithmetic — it is whatever the host's template renders, so by
default the control estimates it and then measures and corrects each row the first time it appears.
That correction runs on the scroll path, and because MAUI applies the offset change on a later pass
than the gesture that caused it, the reader can see the row content wobble up and down for a frame or
two (worst at a slow scrolling pace). A host that can state the height from the appointment alone
avoids the entire measure-and-correct pass. This is especially worthwhile when the template is
dynamically sized — rows that grow or shrink with their content invoke a fresh correction for each
distinct height. When the template's height is genuinely fixed or can be computed, supply a
`Func<ISchedulerAppointment, double>`; when it depends on width, wrapping, font scale or localization,
keep the measured default (see DESIGN §14).

`AgendaSectionTemplate` receives this binding context:

```csharp
public enum SchedulerAgendaSectionKind { Month, Week, Day }

public sealed record SchedulerAgendaSection(
    DateTime Date,
    SchedulerAgendaSectionKind Kind,
    int AppointmentCount);
```

Use one template and switch pre-built variants on `Kind`; a `DataTemplateSelector` is rejected for
the same pooling reason as appointment templates. `AppointmentCount` is the number of appointments
in that day, week or month within the loaded range.

### Interaction

| Property | Type | Default | Description |
|---|---|---|---|
| `DisplayDate` | `DateTime` | `DateTime.Today` | A date identifying the period to show. **Two-way**: after horizontal navigation the control writes back the start of the new centre period; while an agenda scrolls, it writes back the date at the viewport anchor. An agenda uses the month containing an externally assigned date as its range identity and scrolls to the first appointment on or after the exact date. |
| `SelectedSlot` | `SchedulerTimeSlot?` | `null` | The currently selected empty cell, or `null`. **Two-way**: set by the control when empty space is tapped, and settable by the host to move or clear the affordance. |
| `SlotMinutes` | `int` | `15` | How long a slot is: the timeline is divided into slots of this length, and a tap anywhere inside one selects the whole of it. This is the `Duration` reported by `CellTapped`, not just a rounding — the start falls **down** to the containing slot and never past the point touched. Also the granularity of `TimeGutterTapped.Time`. A month cell ignores it and reports a whole day. |
| `DragSnapMinutes` | `int` | `15` | Granularity a dragged appointment lands on, rounded to the **nearest** boundary so a drop goes where it looks like it is going. Independent of `SlotMinutes`: offering whole hours to book does not mean an existing lesson cannot be nudged by a quarter. |
| `AllowDragAndDrop` | `bool` | `true` | When `false`, appointments cannot be picked up at all and no drag events are raised. |
| `ShowDragTimeIndicator` | `bool` | `true` | Whether the time an appointment would take is shown in the hour gutter while it is being dragged. Turning it off does not affect rescheduling itself — only the readout. |
| `AllowDragAcrossPeriods` | `bool` | `true` | Whether holding a dragged appointment against the leading or trailing edge pages to the adjacent period — here, the adjacent week — after a short dwell. When `false`, an appointment can only be moved within the period it started in. Has no effect unless `AllowDragAndDrop` is also `true`. Named for the period rather than the week because the behaviour belongs to paging itself. |

Tapping empty space sets `SelectedSlot` **and** raises `CellTapped` on every tap. The control does not
implement tap-to-arm-then-tap-to-confirm; that is host policy.

### Appearance and state

| Property | Type | Default | Description |
|---|---|---|---|
| `IsBusy` | `bool` | `false` | Shows a non-blocking activity indicator over the grid. |
| `GridBackgroundColor` | `Color` | `Colors.White` | Fill behind the grid and the time gutter. Keep it opaque: the drawing surface must reliably receive touches, since it handles all input. The inherited `BackgroundColor` still covers the control and its header container. |
| `GridLineColor` | `Color` | `#E0E0E0` | Hour lines, day separators and month grid lines. |
| `MinorGridLineColor` | `Color` | `#F0F0F0` | The marks inside the hour. |
| `MinorGridLineMinutes` | `int` | `30` | How often the timeline is marked inside the hour. `30` draws the half hour, `15` the quarters. `60` asks for hour lines only — every mark would land on an hour line, so none is drawn; `0` and negatives are treated the same rather than throwing. Independent of `SlotMinutes` and `DragSnapMinutes`: this is what the grid shows, those are what a tap and a drag land on. The pattern restarts at every hour rather than running through the day, so marks stay hour-aligned; a value that does not divide 60 simply leaves a short gap before the hour (`57` marks `:57` and nothing else). Ignored in month view, which has no hours. |
| `PrimaryTextColor` | `Color` | `#212121` | Ordinary day numbers. |
| `SecondaryTextColor` | `Color` | `#6E6E6E` | Weekday names, time-zone text, gutter labels and month overflow text. |
| `NonWorkingDaysBackgroundColor` | `Color` | `#FAFAFA` | Background of a shaded non-working day. |
| `NonWorkingHoursBackgroundColor` | `Color` | `#FAFAFA` | Background of shaded time outside the working interval. |
| `CurrentDayBackgroundColor` | `Color` | `#F3E8FC` | Background used when today's highlight is enabled, including today's agenda rows. |
| `CurrentDayTextColor` | `Color` | `#4458C8` | Emphasised day-number colour for today. |
| `CurrentTimeIndicatorColor` | `Color` | `#FD4225` | Current-time line and dot. |
| `AdjacentMonthBackgroundColor` | `Color` | `#F5F5F5` | Background of leading and trailing month cells. |
| `AdjacentMonthTextColor` | `Color` | `#B0B0B0` | Day number of a leading or trailing month cell. |
| `CellSelectionBackgroundColor` | `Color` | `#F3E8FC` | Built-in selected-cell background; ignored by a custom `CellSelectionTemplate`. |
| `CellSelectionBorderColor` | `Color` | `#DAB8F4` | Built-in selected-cell border; ignored by a custom template. |
| `CellSelectionTextColor` | `Color` | `#6B3FA0` | Built-in selected-cell text; ignored by a custom template. |
| `DragTimeIndicatorBackgroundColor` | `Color` | `#212121` | Background of the time chip shown while dragging. |
| `DragTimeIndicatorTextColor` | `Color` | `Colors.White` | Text in the drag-time chip. |
| `BusyIndicatorColor` | `Color?` | `null` | Activity-indicator colour; `null` preserves the platform default. |

These are ordinary bindable properties, so a MAUI style is the theme. Appointment templates and a
custom selection template keep ownership of their own colours:

```xml
<Style x:Key="DarkScheduler" TargetType="scheduler:SchedulerView">
    <Setter Property="BackgroundColor" Value="#121212" />
    <Setter Property="GridBackgroundColor" Value="#121212" />
    <Setter Property="PrimaryTextColor" Value="#F2F2F2" />
    <Setter Property="SecondaryTextColor" Value="#A8A8A8" />
    <Setter Property="GridLineColor" Value="#3A3A3A" />
    <Setter Property="MinorGridLineColor" Value="#292929" />
    <Setter Property="NonWorkingDaysBackgroundColor" Value="#181818" />
    <Setter Property="CurrentDayBackgroundColor" Value="#28213D" />
    <Setter Property="CurrentDayTextColor" Value="#B9C2FF" />
</Style>
```

Changing an appearance property updates native chrome and redraws the canvas. It does not rebuild or
rebind appointment views.

### Events

| Event | Argument type | Raised when |
|---|---|---|
| `CellTapped` | `SchedulerCellTappedEventArgs` | Empty grid space is tapped. `SelectedSlot` has already been updated. |
| `AppointmentTapped` | `SchedulerAppointmentTappedEventArgs` | An appointment is tapped without dragging it. |
| `AppointmentDragStarting` | `SchedulerAppointmentDragStartingEventArgs` | A long press has been held on an appointment, before it lifts. **Cancellable.** |
| `AppointmentDropTargetChanged` | `SchedulerAppointmentDropTargetChangedEventArgs` | A drag comes to rest on a different boundary. Not a movement event — silent while the finger travels within one boundary, and silent when the appointment is first picked up. |
| `AppointmentDropped` | `SchedulerAppointmentDroppedEventArgs` | A dragged appointment is released. **Cancellable.** |
| `HeaderTapped` | `SchedulerHeaderTappedEventArgs` | The header above a timeline day column is tapped. Silent outside `Timeline`. |
| `TimeGutterTapped` | `SchedulerTimeGutterTappedEventArgs` | The timeline hour gutter is tapped. Silent outside `Timeline`. |
| `VisibleDatesChanged` | `SchedulerVisibleDatesChangedEventArgs` | The visible period changes, including on first layout and when an agenda extends at either edge. This is the data-loading hook. |

### Methods

| Method | Description |
|---|---|
| `void ScrollToTime(TimeSpan time)` | Scrolls the timeline so `time` sits near the top of the viewport. The control calls this itself on load to open near the current time. Does nothing outside `Timeline`. |
| `void ScrollToDate(DateTime date)` | Brings a date into view. On a timeline or month this changes `DisplayDate`; in an agenda it also scrolls the first appointment on or after that date to the top. |

---

## Contracts

### `ISchedulerAppointment`

What the control needs to know about an item.

```csharp
public interface ISchedulerAppointment
{
    object Key { get; }
    DateTime Start { get; }
    DateTime End { get; }
    string? Subject { get; }
}
```

| Member | Description |
|---|---|
| `Key` | Stable identity, compared with `Equals`. Any stable value will do — an id, a composite string. |
| `Start` | Wall-clock start in `SchedulerView.TimeZone`. |
| `End` | Wall-clock end. Items shorter than 15 minutes still get a tappable box. |
| `Subject` | Short text used to build the accessibility description. May be `null`. |

Implement this on your own type and keep the domain object on it — that instance is the template's
binding context, so the template can read whatever it needs.

`Key` is what makes the control safe to use with a collection you rebuild. It never assumes it is
handed the same object twice: views are matched to appointments by key, so an unchanged reload repaints
nothing and an insert does not disturb its neighbours, and everything reported back to you is looked up
in your *current* collection first. Two appointments sharing a key within one period is not meaningful;
give recurring instances distinct keys.

### `SchedulerTimeSlot`

```csharp
public readonly record struct SchedulerTimeSlot(DateTime Start, TimeSpan Duration)
```

| Member | Type | Description |
|---|---|---|
| `Start` | `DateTime` | Start of the slot. |
| `Duration` | `TimeSpan` | Slot length — `SlotMinutes` for a cell tapped on the timeline, one day for a cell tapped in a month. |
| `End` | `DateTime` | `Start + Duration`. |
| `Date` | `DateOnly` | Calendar day the slot falls on. |

### `SchedulerAppointmentCollection<T>`

An observable appointments collection with range methods, so a host can mutate its data in bulk.
Optional: it is a convenience for keeping one collection and changing it in place, not a requirement.
`ItemsSource` accepts any `IEnumerable<ISchedulerAppointment>`, with or without change notification.
`T` is your own appointment type — any class implementing `ISchedulerAppointment`.

```csharp
public class SchedulerAppointmentCollection<T> : ObservableCollection<T>
    where T : class, ISchedulerAppointment
{
    public SchedulerAppointmentCollection();
    public SchedulerAppointmentCollection(IEnumerable<T> items);

    public void AddRange(IEnumerable<T> items);
    public void RemoveRange(IEnumerable<T> items);
    public void RemoveRange(int index, int count);
    public void ReplaceRange(int index, int count, IEnumerable<T> items);
}
```

| Member | Raises one | Carrying |
|---|---|---|
| `AddRange` | `Add` | the appended items, from the index the first landed on |
| `RemoveRange(IEnumerable<T>)` | `Remove`, or `Reset` when the items did not sit together | the removed items and where the run began |
| `RemoveRange(int, int)` | `Remove` | the removed items and `index` |
| `ReplaceRange(int, int, IEnumerable<T>)` | `Replace`, or `Add`/`Remove` when one side is empty, or `Reset` when the lengths differ | the new and old items and `index` |

The framework's `ObservableCollection<T>` deliberately has no range methods
(see <https://github.com/dotnet/runtime/issues/18087>), so adding a month of appointments means one
event per appointment, and a host loading several months pays hundreds of events. This collection's
range methods change a whole range and raise a single event for it. Keep everything you have loaded
in one instance and append with `AddRange` rather than rebuilding and re-assigning `ItemsSource`. To
reload everything at once, `ReplaceRange(0, Count, everything)` is the one-event way to do it.

**The event says what changed.** Each range method reports the precise action, with `NewItems`,
`OldItems` and the starting index, for every change a single notification can describe. Some cannot
be described by one: `NotifyCollectionChangedEventArgs` states a multi-item add or remove as a run of
items from a single index, so a scattered removal — or a replacement of a different length than the
range it replaces — has no faithful single-event form. Those report `Reset`, meaning "re-read the
collection", rather than a payload that would put items in the wrong place. Handle `Reset` whatever
you do with the payloads; a plain `Clear()` raises one too.

The scheduler itself reads no payload — it repopulates on any change and coalesces bursts, which is
why a `Reset` costs it nothing. The payloads are for your other consumers: the same collection bound
to a list elsewhere, or your own diagnostics.

Null sources throw `ArgumentNullException`, a range that changes nothing raises nothing, and an
out-of-range `index`/`count` throws as `List<T>` would. `RemoveRange(IEnumerable<T>)` matches items
with `EqualityComparer<T>.Default`, **not** by `ISchedulerAppointment.Key` — so pass the instance
that is in the collection, not the new one you built to replace it.

---

## Event argument types

### `SchedulerCellTappedEventArgs`

| Member | Type | Description |
|---|---|---|
| `Slot` | `SchedulerTimeSlot` | The tapped cell. |

### `SchedulerAppointmentTappedEventArgs`

| Member | Type | Description |
|---|---|---|
| `Appointment` | `ISchedulerAppointment` | The tapped appointment. |

### `SchedulerAppointmentDragStartingEventArgs`

| Member | Type | Description |
|---|---|---|
| `Appointment` | `ISchedulerAppointment` | The appointment about to be picked up. |
| `Cancel` | `bool` (settable) | Set `true` to refuse the drag. The press falls through as an ordinary touch. |

This is the right place to veto a drag — read-only items, items owned by an external calendar, or an
offline app. It is raised synchronously.

### `SchedulerAppointmentDropTargetChangedEventArgs`

| Member | Type | Description |
|---|---|---|
| `Appointment` | `ISchedulerAppointment` | The appointment being dragged, resolved against the current `ItemsSource`. |
| `DropStart` | `DateTime` | The boundary it has moved to, in `TimeZone`. Where it would land if released now; the drag may still move on or be cancelled. |

Raised once per boundary crossed, which makes it the hook for feedback a person should feel one step
at a time — a short haptic tick is the obvious use. Do not treat it as a commitment: only
`AppointmentDropped` says where the appointment actually went. Edge paging into an adjacent week
raises it too, since the drop target genuinely moved.

### `SchedulerAppointmentDroppedEventArgs`

| Member | Type | Description |
|---|---|---|
| `Appointment` | `ISchedulerAppointment` | The dropped appointment. |
| `DropStart` | `DateTime` | Snapped start it was dropped on, in `TimeZone`. |
| `Cancel` | `bool` (settable) | Set `true` to reject the drop and return the appointment to its old position. |

### `SchedulerHeaderTappedEventArgs`

| Member | Type | Meaning |
|---|---|---|
| `Date` | `DateTime` | Midnight on the day whose header was tapped, in `TimeZone`. |

Raised only while `ViewMode` is `Timeline`. A month's header names weekdays rather than dates, while
an agenda names days in its leading gutter. Tap a month cell or agenda space instead — `CellTapped`
reports the whole day.

A natural use is drilling in from a week to a single day:

```csharp
scheduler.HeaderTapped += (_, e) =>
{
    scheduler.DisplayDate = e.Date;
    scheduler.VisibleDays = 1;
};
```

### `SchedulerTimeGutterTappedEventArgs`

| Member | Type | Meaning |
|---|---|---|
| `Time` | `TimeSpan` | Time of day at the point tapped, floored to `SlotMinutes` and held inside the day window. |

A time of day rather than a `DateTime`, because the gutter runs alongside every day on the page at
once — a tap on it names an hour, not a date. Read `DisplayDate` if you need to pair it with one.

Floored, the same way `CellTapped` is: what a tap names begins at or before the point touched,
so tapping just below an hour line gives that hour rather than the next. The last slot of the day
starts one interval before `EndHour`.

### `SchedulerVisibleDatesChangedEventArgs`

| Member | Type | Description |
|---|---|---|
| `VisibleDates` | `IReadOnlyList<DateTime>` | The dates the active surface exposes: `VisibleDays` on a timeline, 42 on a month, and the whole loaded range in an agenda. |
| `PrefetchFrom` | `DateTime` | Beginning of the range the host should have loaded. |
| `PrefetchTo` | `DateTime` | End of the range the host should have loaded. |

Timeline and month surfaces render three pages. Loading `PrefetchFrom`–`PrefetchTo` rather than just
`VisibleDates` is what keeps the next swipe from showing an empty page. An agenda has one vertical
surface, so all three values describe its current loaded range.

---

## Using it from Blazor

The control has no Blazor dependency. A wrapper is generated by
`BlazorBindings.Maui.ComponentGenerator` from an attribute in the host app's `Properties/Elements.cs`:

```csharp
[assembly: GenerateComponent(typeof(SchedulerView),
    MakeItemsGeneric = false,
    PropertyChangedEvents = [nameof(SchedulerView.DisplayDate), nameof(SchedulerView.SelectedSlot)],
    GenericProperties =
    [
        $"{nameof(SchedulerView.AppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment",
        $"{nameof(SchedulerView.MonthAppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment",
        $"{nameof(SchedulerView.AgendaAppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment",
        $"{nameof(SchedulerView.AgendaSectionTemplate)}:Owlery.Maui.Scheduler.SchedulerAgendaSection"
    ])]
```

Names differ on the Blazor side:

| MAUI member | Blazor parameter |
|---|---|
| `AppointmentTemplate` (`DataTemplate`) | `AppointmentTemplate` (`RenderFragment<ISchedulerAppointment>`) |
| `AgendaAppointmentTemplate` (`DataTemplate`) | Generated generic template parameter for `ISchedulerAppointment` when included in the host's generator configuration. |
| `AgendaSectionTemplate` (`DataTemplate`) | Generated generic template parameter for `SchedulerAgendaSection` when included in the host's generator configuration. |
| `CellSelectionTemplate` (`DataTemplate`) | `CellSelectionTemplate` (`RenderFragment`, no context) |
| `DisplayDate` | `DisplayDate` + `DisplayDateChanged` → `@bind-DisplayDate` |
| `SelectedSlot` | `SelectedSlot` + `SelectedSlotChanged` → `@bind-SelectedSlot` |
| `VisibleDays` | `VisibleDays` + `VisibleDaysChanged` → `@bind-VisibleDays` |
| `CellTapped` | `OnCellTapped` |
| `AppointmentTapped` | `OnAppointmentTapped` |
| `AppointmentDragStarting` | `OnAppointmentDragStarting` |
| `AppointmentDropTargetChanged` | `OnAppointmentDropTargetChanged` |
| `AppointmentDropped` | `OnAppointmentDropped` |
| `HeaderTapped` | `OnHeaderTapped` |
| `TimeGutterTapped` | `OnTimeGutterTapped` |
| `VisibleDatesChanged` | `OnVisibleDatesChanged` |

Value properties keep their names and become nullable parameters. Example:

```razor
<SchedulerView @bind-DisplayDate="displayDate"
                   @bind-SelectedSlot="selectedSlot"
                   @bind-VisibleDays="visibleDays"
                   ItemsSource="appointments"
                   TimeZone="TimeZoneHelper.GetSelectedTimeZone()"
                   StartHour="8"
                   EndHour="23"
                   OnVisibleDatesChanged="OnVisibleDatesChanged"
                   OnAppointmentTapped="OnAppointmentTapped">
    <AppointmentTemplate>
        @if (context is SessionAppointment appointment)
        {
            <WeekViewAppointment Session="appointment.Session" />
        }
    </AppointmentTemplate>
</SchedulerView>
```

Regenerate with `dotnet generate-maui-blazor-components` after changing the control's public surface.

---

## Behaviour worth knowing

**Keep everything loaded; the control filters.** `ItemsSource` may hold any span the host likes — the
control picks out what belongs to each rendered week, month or agenda window. Do not trim the collection
when a period scrolls off screen: keep the whole loaded range in one collection and change it in place.
`SchedulerAppointmentCollection<T>` is the easiest way to do a bulk change in one event; the framework's
`ObservableCollection<T>` has no range methods, so without it a bulk change is one event per item.

The control watches your collection only while it is loaded — it unsubscribes on `Unloaded` and
subscribes again on `Loaded`, so a collection that outlives the view does not keep the view alive.
Nothing is missed by that: the collection is read again on reload, so changes made while the view was
off screen show up when it comes back.

**Time zones are yours to handle.** Every `DateTime` crossing this API — `ISchedulerAppointment.Start`,
`SchedulerTimeSlot.Start`, `AppointmentDropped.DropStart`, `VisibleDatesChanged` — is wall-clock in
`TimeZone`. The control never converts. Convert once, at the boundary where you build your
`ISchedulerAppointment` items, and convert back when writing a drop result to storage.

**`Cancel` is read synchronously.** Both cancellable events check `Cancel` the moment the handler
returns. An `async` handler must do its validation *before* its first `await`; anything after it is too
late to veto. Checks that must block a drag therefore belong in `AppointmentDragStarting`.

**Events hand you a live instance.** Dragging across periods pages the calendar, which raises
`VisibleDatesChanged` and will typically make you load data and rebuild your collection mid-gesture.
The appointment carried by `AppointmentTapped`, `AppointmentDragStarting` and `AppointmentDropped` is
resolved against your current `ItemsSource` by `Key` before the event is raised, so acting on what you
were given acts on something you are still displaying. This is why `Key` is on the interface.

**A successful drop leaves the appointment where it was dropped.** The control does not mutate your
model and does not wait for you. When your update completes, re-emit `ItemsSource` — that repositions
the appointment from the model on success, and moves it back on failure. If you never do, the control
gives up waiting at the next change of period and the appointment returns to where your model says it
is. Re-emitting matters more than
it used to: a dragged appointment is lifted out of its week for the duration, and re-emitting is what
puts it back under one.

**A drag leaves a faded copy behind.** While an appointment is being moved, the place it came from
keeps showing it at half opacity. Both that copy and the one under the finger come from
`AppointmentTemplate`, so nothing extra is needed to support it.

**Not supported.** Multi-week grids; all-day and multi-day appointment spans; resizing an appointment
by its edges. See [design §14](design/README.md).
