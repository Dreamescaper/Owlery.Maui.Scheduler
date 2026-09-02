# Scheduler Playground

A bare .NET MAUI app for exercising [`Owlery.Maui.Scheduler`](../docs/API.md) on iOS
and Android without going anywhere near the real app.

No Blazor, no XAML, no `Owlery.*` references beyond the control itself. The whole app is the control,
some generated data, and a drawer of knobs — which makes it the fastest way to see what a property
actually does, and the only place drag-and-drop can be tried against a few thousand appointments.

## Running it

```sh
dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-ios -t:Run
```

```sh
dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-android -t:Run
```

## Driving it from a terminal

The app carries `Microsoft.Maui.DevFlow.Agent` in **Debug only**, so `maui devflow` can query its
visual tree, tap, type and screenshot it without anyone touching the device:

```sh
maui devflow ui tap --text "›"
```

```sh
maui devflow ui property <element-id> TranslationX
```

This is how anything the headless suite cannot reach gets checked — gesture arbitration, platform
paging, drag-and-drop, and whether a transition actually animates rather than cutting. Reading a
property back mid-animation is often a better answer than a screenshot: a burst of `TranslationX`
samples across a page change shows the intermediate positions, where a screenshot usually arrives
after everything has settled.

Two things to know before trusting a result:

- **The `maui` CLI needs `DOTNET_ROOT` set explicitly**, while `dotnet` must not have it set when a
  path contains a space — see the DevFlow skill's `references/ios-and-mac.md`.
- **Rapid repeated taps on one element are dropped.** Four taps 0.6 s apart on `›` moved the calendar
  once; the same happened on `+`, which shares no code with it. Space taps out, and assert on what the
  app reports rather than on how many taps were sent.

The agent is `Debug`-only and the control's own project never references it — `Owlery.Maui.Scheduler`
has exactly one package reference, and this is the host's tooling.

## What is on screen

| Chrome | Does |
|---|---|
| `‹` `Today` `›` | Writes `DisplayDate` — by `VisibleDays`, or by a month in `Month` mode. |
| `−` *count* `+` | Steps the number of generated appointments **per month** through 0, 10, 25, 50, 100, 250, 500, 1 000, 2 500, 5 000. |
| **Knobs** | Opens the drawer. Every row writes straight to the property it is named after, and follows it back when something else changes it — tapping a day header drops `VisibleDays` to 1, and the knob says so. |
| The bottom strip | The last three events the control raised, with what it reported. |

The drawer also carries the two knobs that are not control properties — *Cancel drags of locked items*
and *Cancel every drop*. Those are host policy, answered in `AppointmentDragStarting` and
`AppointmentDropped`, and they are there because the cancellable events are the part of the API that is
hardest to picture from the documentation.

## The data

`SampleDataGenerator` spreads the chosen number of appointments through every calendar month the
control requests, at teaching hours, so overlaps happen on their own. It is deterministic: the same
count gives the same month until **Regenerate** picks a new seed. When the agenda grows at either end,
its wider `VisibleDatesChanged` range causes the source to generate the added month while preserving
the appointment instances in months that were already loaded.

Every tenth appointment is marked locked and drawn with a padlock, which is what *Cancel drags of
locked items* refuses to pick up.

Paging or scrolling beyond the current range loads the newly requested months, which is the same host
response a real app would make to `VisibleDatesChanged`.

## Worth reading if you are writing a template

`AppointmentBox` and `AppointmentChip` are written the way the control requires: the whole subtree is
built in the constructor, and `OnBindingContextChanged` reads whatever appointment is bound now. Views
made from `AppointmentTemplate` are pooled and rebound, never rebuilt, so a template that captures its
appointment keeps drawing the first one it ever saw.

`AppointmentBox` also shows the other half of that rule — it subscribes to `SizeChanged` **once**, in
the constructor, to drop the time line and shrink the title in a box only twelve units tall. Subscribing
per binding would leave one live handler per appointment the view had ever shown.

`AgendaRow` supplies its own height through the optimized contract: the sample hands
`SchedulerView.AgendaRowHeight` a static method (`AgendaRow.RowHeight`) that states the height from the
appointment alone. That avoids the estimate→measurement correction pass, which otherwise shows up as the
card wobbling a frame or two on an upward scroll (see [design §21](../docs/design/agenda.md)). The tradeoff is size, not
smoothness: the value is authoritative and fixed, so a row is a consistent amount off rather than
resized to its text. The **AgendaRowHeight** knob toggles between that known height and letting the row
be measured — most visible with the 2,000- and 5,000-per-month data presets during a fast fling.
