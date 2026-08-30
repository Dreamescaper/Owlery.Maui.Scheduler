# Owlery.Maui.Scheduler — API reference

A week-view scheduler control for .NET MAUI. This document covers the public surface. For *what it
does*, see the [functional requirements](requirements/README.md); for *why* it is built the way it is,
see [DESIGN.md](DESIGN.md).

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
  - [Interaction](#interaction)
  - [Appearance and state](#appearance-and-state)
  - [Events](#events)
  - [Methods](#methods)
- [Contracts](#contracts)
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
| `ViewMode` | `SchedulerViewMode` | `Timeline` | Whether the control shows columns of hours or a calendar month. |

```csharp
public enum SchedulerViewMode { Timeline, Month }
```

The two are different surfaces rather than two settings of one, so a fair amount does not carry over.
In `Month`:

- `VisibleDays`, `StartHour`, `EndHour`, `HourHeight` and `TimeGutterWidth` are ignored. There is no
  hour gutter, and a month is never taller than the viewport — it does not scroll.
- Every appointment on a day is shown regardless of the hour it starts at, whereas the timeline clips
  to `StartHour`..`EndHour`.
- Dragging is not offered, whatever `AllowDragAndDrop` says, so `AppointmentDragStarting` and
  `AppointmentDropped` are never raised. `ScrollToTime` does nothing.
- `CellTapped` reports a slot a **day** long rather than `SnapMinutes` — a month cell has no finer
  target. The `Duration` is what tells the two apart.
- `VisibleDatesChanged` reports all 42 days of the grid, and its prefetch range spans the grids either
  side — roughly four months rather than three weeks. Size your queries accordingly.

The grid is always six rows, so a short month beginning on `FirstDayOfWeek` shows two full rows of the
next one. Leading and trailing days from the neighbouring months are drawn muted but behave normally:
they hold appointments and can be tapped.

### Data and templates

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable<ISchedulerAppointment>?` | `null` | Every appointment the host has loaded, across as many weeks as it likes. The control selects what belongs to each rendered week. Honours `INotifyCollectionChanged`; assigning a new collection instance also refreshes. |
| `AppointmentTemplate` | `DataTemplate?` | `null` | Template for one appointment box. Its binding context is the `ISchedulerAppointment`. The grid still draws without it, but no appointments appear. |
| `MonthAppointmentTemplate` | `DataTemplate?` | `null` | Template for one appointment chip in a month cell. Falls back to `AppointmentTemplate` when not set — which renders, but rarely reads well: a chip is one line about 16 units tall, not a box sized by its duration. |
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
> so it costs a rebind (~0.25ms) rather than a fresh build (~3ms) — see `DESIGN.md` section 6.

> **Hand back the same instance for an appointment that has not changed.** Binding contexts are
> compared by reference, so an unchanged appointment that arrives as a new object re-renders its
> template for nothing, and a host that reloads in chunks pays that for everything on screen on every
> chunk. Conversely an appointment that *has* changed must be a **new** instance: mutating one in
> place changes nothing the control can see, and it will keep showing — and describing — the old
> values. Treat them as immutable snapshots.

> A template built from MAUI primitives binds roughly a third faster than the equivalent written as a
> Blazor component through generated bindings — measured per *fresh* bind, which is what a page
> rotation pays for. Rebinding a reused view is unaffected. See `DESIGN.md` section 6 if that matters
> to you; either kind of template works.

> Assigning `ItemsSource` repeatedly is fine. Several assignments arriving together are collapsed into
> a single rebuild on the next tick, so loading in chunks costs one rebuild rather than one per chunk.

### Time window and layout

| Property | Type | Default | Description |
|---|---|---|---|
| `StartHour` | `int` | `8` | First hour shown on the timeline. |
| `EndHour` | `int` | `23` | Last hour shown. Content height is `(EndHour - StartHour) * HourHeight`. |
| `HourHeight` | `double` | `50` | Height in device-independent pixels of one hour row. |
| `VisibleDays` | `int` | `7` | How many days a page shows. `7` is a week, `3` a three-day view, `1` a single day; `5` gives a working week. Changing it animates the columns to their new width. Ignored while `ViewMode` is `Month`. |
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
| `ShowCurrentDayHighlight` | `bool` | `true` | Whether today's background and day number are emphasised. Does not control the current-time indicator. |
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

### Interaction

| Property | Type | Default | Description |
|---|---|---|---|
| `DisplayDate` | `DateTime` | `DateTime.Today` | Any date inside the week to show. **Two-way**: after a swipe the control writes back the start of the new centre week, so a bound field follows the user. Setting a date in the week already displayed is a no-op; setting one in the week either side slides across to it, the way a swipe would. |
| `SelectedSlot` | `SchedulerTimeSlot?` | `null` | The currently selected empty cell, or `null`. **Two-way**: set by the control when empty space is tapped, and settable by the host to move or clear the affordance. |
| `SnapMinutes` | `int` | `15` | Granularity for cell selection and for dropped appointments. Selection rounds **down** into the containing cell; a drop rounds to the **nearest** boundary. |
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
| `MinorGridLineColor` | `Color` | `#F0F0F0` | Half-hour lines. |
| `PrimaryTextColor` | `Color` | `#212121` | Ordinary day numbers. |
| `SecondaryTextColor` | `Color` | `#6E6E6E` | Weekday names, time-zone text, gutter labels and month overflow text. |
| `NonWorkingDaysBackgroundColor` | `Color` | `#FAFAFA` | Background of a shaded non-working day. |
| `NonWorkingHoursBackgroundColor` | `Color` | `#FAFAFA` | Background of shaded time outside the working interval. |
| `CurrentDayBackgroundColor` | `Color` | `#F3E8FC` | Background used when today's highlight is enabled. |
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
| `AppointmentDropped` | `SchedulerAppointmentDroppedEventArgs` | A dragged appointment is released. **Cancellable.** |
| `HeaderTapped` | `SchedulerHeaderTappedEventArgs` | The header above a day column is tapped. Silent in `Month`. |
| `TimeGutterTapped` | `SchedulerTimeGutterTappedEventArgs` | The hour gutter is tapped. A month has no gutter, so it never raises this. |
| `VisibleDatesChanged` | `SchedulerVisibleDatesChangedEventArgs` | The centre week changes, including on first layout. This is the data-loading hook. |

### Methods

| Method | Description |
|---|---|
| `void ScrollToTime(TimeSpan time)` | Scrolls the timeline so `time` sits near the top of the viewport. The control calls this itself on load to open near the current time. Does nothing while `ViewMode` is `Month`. |

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
| `Start` | `DateTime` | Snapped start of the slot. |
| `Duration` | `TimeSpan` | Slot length — `SnapMinutes` for a cell tapped on the timeline, one day for a cell tapped in a month. |
| `End` | `DateTime` | `Start + Duration`. |
| `Date` | `DateOnly` | Calendar day the slot falls on. |

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

Not raised while `ViewMode` is `Month`: a month's header names weekdays, and one column stands for six
dates rather than one. Tap a month *cell* instead — `CellTapped` reports the whole day.

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
| `Time` | `TimeSpan` | Time of day at the point tapped, snapped down to `SnapMinutes` and held inside the day window. |

A time of day rather than a `DateTime`, because the gutter runs alongside every day on the page at
once — a tap on it names an hour, not a date. Read `DisplayDate` if you need to pair it with one.

Snapped *down*, the same way `CellTapped` is: what a tap names begins at or before the point touched,
so tapping just below an hour line gives that hour rather than the next. The last slot of the day
starts one interval before `EndHour`.

### `SchedulerVisibleDatesChangedEventArgs`

| Member | Type | Description |
|---|---|---|
| `VisibleDates` | `IReadOnlyList<DateTime>` | The days the user is looking at — as many as `VisibleDays`. |
| `PrefetchFrom` | `DateTime` | Start of the first rendered week. |
| `PrefetchTo` | `DateTime` | End of the last rendered week. |

Three pages are rendered at all times, whatever `VisibleDays` is. Loading `PrefetchFrom`–`PrefetchTo`
rather than just `VisibleDates` is what keeps the next swipe from showing an empty page.

---

## Using it from Blazor

The control has no Blazor dependency. A wrapper is generated by
`BlazorBindings.Maui.ComponentGenerator` from an attribute in the host app's `Properties/Elements.cs`:

```csharp
[assembly: GenerateComponent(typeof(SchedulerView),
    MakeItemsGeneric = false,
    PropertyChangedEvents = [nameof(SchedulerView.DisplayDate), nameof(SchedulerView.SelectedSlot)],
    GenericProperties = [$"{nameof(SchedulerView.AppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment"])]
```

Names differ on the Blazor side:

| MAUI member | Blazor parameter |
|---|---|
| `AppointmentTemplate` (`DataTemplate`) | `AppointmentTemplate` (`RenderFragment<ISchedulerAppointment>`) |
| `CellSelectionTemplate` (`DataTemplate`) | `CellSelectionTemplate` (`RenderFragment`, no context) |
| `DisplayDate` | `DisplayDate` + `DisplayDateChanged` → `@bind-DisplayDate` |
| `SelectedSlot` | `SelectedSlot` + `SelectedSlotChanged` → `@bind-SelectedSlot` |
| `VisibleDays` | `VisibleDays` + `VisibleDaysChanged` → `@bind-VisibleDays` |
| `CellTapped` | `OnCellTapped` |
| `AppointmentTapped` | `OnAppointmentTapped` |
| `AppointmentDragStarting` | `OnAppointmentDragStarting` |
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

**Not supported.** Day, month and agenda views; all-day and multi-day appointments; resizing an
appointment by its edges; theme switching. See
[DESIGN.md §14](DESIGN.md).
