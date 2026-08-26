# Owlery.Maui.Scheduler — API reference

A week-view scheduler control for .NET MAUI. This document covers the public surface. For *what it
does*, see the [functional requirements](requirements/README.md); for *why* it is built the way it is,
see [DESIGN.md](DESIGN.md).

Namespace: `Owlery.Maui.Scheduler`

---

## Contents

- [Quick start](#quick-start)
- [SchedulerWeekView](#schedulerweekview)
  - [Data and templates](#data-and-templates)
  - [Time window and layout](#time-window-and-layout)
  - [Interaction](#interaction)
  - [Appearance and state](#appearance-and-state)
  - [Events](#events)
  - [Methods](#methods)
- [Contracts](#contracts)
- [Event argument types](#event-argument-types)
- [Using it from Blazor](#using-it-from-blazor)
- [Behaviour worth knowing](#behaviour-worth-knowing)

---

## Quick start

```csharp
var scheduler = new SchedulerWeekView
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

## SchedulerWeekView

`public class SchedulerWeekView : ContentView`

Every property below is backed by a `BindableProperty` named `<PropertyName>Property`.

### Data and templates

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable<ISchedulerAppointment>?` | `null` | Every appointment the host has loaded, across as many weeks as it likes. The control selects what belongs to each rendered week. Honours `INotifyCollectionChanged`; assigning a new collection instance also refreshes. |
| `AppointmentTemplate` | `DataTemplate?` | `null` | Template for one appointment box. Its binding context is the `ISchedulerAppointment`. The grid still draws without it, but no appointments appear. |
| `CellSelectionTemplate` | `DataTemplate?` | `null` | Optional template for the selected-cell affordance, bound to the selected `SchedulerTimeSlot`. When `null` the control draws a bordered **+** box. |

> Views created from `AppointmentTemplate` are **pooled and rebound**, never rebuilt. A template must
> therefore tolerate its binding context changing — build the whole subtree up front and toggle it,
> rather than assuming one instance per appointment.

### Time window and layout

| Property | Type | Default | Description |
|---|---|---|---|
| `StartHour` | `int` | `8` | First hour shown on the timeline. |
| `EndHour` | `int` | `23` | Last hour shown. Content height is `(EndHour - StartHour) * HourHeight`. |
| `HourHeight` | `double` | `50` | Height in device-independent pixels of one hour row. |
| `FirstDayOfWeek` | `DayOfWeek` | `Monday` | Which day starts the week, and therefore where week boundaries fall. |
| `TimeGutterWidth` | `double` | `52` | Width of the fixed left column holding the hour labels. |
| `HeaderHeight` | `double` | `52` | Height of the day-name/day-number strip above the grid. |
| `TimeFormat` | `string` | `"HH:mm"` | Format string for hour-gutter labels and the drag time indicator. |
| `TimeZone` | `TimeZoneInfo` | `TimeZoneInfo.Local` | The zone every `DateTime` crossing this API is expressed in. Used to place the current-time line and decide which column is today. |

Appointments falling outside `StartHour`–`EndHour` are clipped to the visible window; an appointment
running past midnight is clipped to its own day.

**Changing any property in this group rebuilds all three rendered weeks.** They are configuration, not
per-frame state — set them once rather than animating them.

### Interaction

| Property | Type | Default | Description |
|---|---|---|---|
| `DisplayDate` | `DateTime` | `DateTime.Today` | Any date inside the week to show. **Two-way**: after a swipe the control writes back the start of the new centre week, so a bound field follows the user. Setting a date in the week already displayed is a no-op. |
| `SelectedSlot` | `SchedulerTimeSlot?` | `null` | The currently selected empty cell, or `null`. **Two-way**: set by the control when empty space is tapped, and settable by the host to move or clear the affordance. |
| `SnapMinutes` | `int` | `15` | Granularity for cell selection and for dropped appointments. Selection rounds **down** into the containing cell; a drop rounds to the **nearest** boundary. |
| `AllowDragAndDrop` | `bool` | `true` | When `false`, appointments cannot be picked up at all and no drag events are raised. |

Tapping empty space sets `SelectedSlot` **and** raises `CellTapped` on every tap. The control does not
implement tap-to-arm-then-tap-to-confirm; that is host policy.

### Appearance and state

| Property | Type | Default | Description |
|---|---|---|---|
| `IsBusy` | `bool` | `false` | Shows a non-blocking activity indicator over the grid. |
| `GridBackgroundColor` | `Color` | `Colors.White` | Fill behind the grid. Keep it opaque: the drawing surface must reliably receive touches, since it handles all input. Like the layout group above, changing it rebuilds all three weeks. |

### Events

| Event | Argument type | Raised when |
|---|---|---|
| `CellTapped` | `SchedulerCellTappedEventArgs` | Empty grid space is tapped. `SelectedSlot` has already been updated. |
| `AppointmentTapped` | `SchedulerAppointmentTappedEventArgs` | An appointment is tapped without dragging it. |
| `AppointmentDragStarting` | `SchedulerAppointmentDragStartingEventArgs` | A long press has been held on an appointment, before it lifts. **Cancellable.** |
| `AppointmentDropped` | `SchedulerAppointmentDroppedEventArgs` | A dragged appointment is released. **Cancellable.** |
| `VisibleDatesChanged` | `SchedulerVisibleDatesChangedEventArgs` | The centre week changes, including on first layout. This is the data-loading hook. |

### Methods

| Method | Description |
|---|---|
| `void ScrollToTime(TimeSpan time)` | Scrolls the timeline so `time` sits near the top of the viewport. The control calls this itself on load to open near the current time. |

---

## Contracts

### `ISchedulerAppointment`

What the control needs to know about an item.

```csharp
public interface ISchedulerAppointment
{
    DateTime Start { get; }
    DateTime End { get; }
    string? Subject { get; }
}
```

| Member | Description |
|---|---|
| `Start` | Wall-clock start in `SchedulerWeekView.TimeZone`. |
| `End` | Wall-clock end. Items shorter than 15 minutes still get a tappable box. |
| `Subject` | Short text used to build the accessibility description. May be `null`. |

Implement this on your own type and keep the domain object on it — that instance is the template's
binding context, so the template can read whatever it needs.

### `SchedulerTimeSlot`

```csharp
public readonly record struct SchedulerTimeSlot(DateTime Start, TimeSpan Duration)
```

| Member | Type | Description |
|---|---|---|
| `Start` | `DateTime` | Snapped start of the slot. |
| `Duration` | `TimeSpan` | Slot length — `SnapMinutes` for a tapped cell. |
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

### `SchedulerVisibleDatesChangedEventArgs`

| Member | Type | Description |
|---|---|---|
| `VisibleDates` | `IReadOnlyList<DateTime>` | The seven days the user is looking at. |
| `PrefetchFrom` | `DateTime` | Start of the first rendered week. |
| `PrefetchTo` | `DateTime` | End of the last rendered week. |

Three weeks are rendered at all times. Loading `PrefetchFrom`–`PrefetchTo` rather than just
`VisibleDates` is what keeps the next swipe from showing an empty week.

---

## Using it from Blazor

The control has no Blazor dependency. A wrapper is generated by
`BlazorBindings.Maui.ComponentGenerator` from an attribute in the host app's `Properties/Elements.cs`:

```csharp
[assembly: GenerateComponent(typeof(SchedulerWeekView),
    MakeItemsGeneric = false,
    PropertyChangedEvents = [nameof(SchedulerWeekView.DisplayDate), nameof(SchedulerWeekView.SelectedSlot)],
    GenericProperties = [$"{nameof(SchedulerWeekView.AppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment"])]
```

Names differ on the Blazor side:

| MAUI member | Blazor parameter |
|---|---|
| `AppointmentTemplate` (`DataTemplate`) | `AppointmentTemplate` (`RenderFragment<ISchedulerAppointment>`) |
| `CellSelectionTemplate` (`DataTemplate`) | `CellSelectionTemplate` (`RenderFragment`, no context) |
| `DisplayDate` | `DisplayDate` + `DisplayDateChanged` → `@bind-DisplayDate` |
| `SelectedSlot` | `SelectedSlot` + `SelectedSlotChanged` → `@bind-SelectedSlot` |
| `CellTapped` | `OnCellTapped` |
| `AppointmentTapped` | `OnAppointmentTapped` |
| `AppointmentDragStarting` | `OnAppointmentDragStarting` |
| `AppointmentDropped` | `OnAppointmentDropped` |
| `VisibleDatesChanged` | `OnVisibleDatesChanged` |

Value properties keep their names and become nullable parameters. Example:

```razor
<SchedulerWeekView @bind-DisplayDate="displayDate"
                   @bind-SelectedSlot="selectedSlot"
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
</SchedulerWeekView>
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

**A successful drop leaves the appointment where it was dropped.** The control does not mutate your
model and does not wait for you. When your update completes, re-emit `ItemsSource` — that repositions
the appointment from the model on success, and moves it back on failure.

**Not supported.** Day, month and agenda views; all-day and multi-day appointments; resizing an
appointment by its edges; dragging into an adjacent week; theme switching. See
[DESIGN.md §14](DESIGN.md).
