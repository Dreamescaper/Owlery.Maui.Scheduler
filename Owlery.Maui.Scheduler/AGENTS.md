# Scheduler Control Guidelines

## Scope

- These instructions apply to `Owlery.Maui.Scheduler` and override the repository root where they
  differ. Root guidelines still apply for anything not covered here.
- `CLAUDE.md` in this folder is a symlink to this file. Keep it a symlink — never replace it with a
  copy, and never edit the two separately.

## What This Project Is

- A reusable .NET MAUI week-view scheduler control, built from plain MAUI primitives.
- It has exactly one package reference: `Microsoft.Maui.Controls`. Do not add BlazorBindings,
  Syncfusion, CommunityToolkit, or any `Owlery.*` project reference.
- It owns no data and fetches nothing. It renders what the host supplies and reports what the user
  did. Do not add HTTP, storage, or domain types.

## Documentation Set

Four documents describe this project. They answer different questions and must not be merged:

| Document | Answers |
|---|---|
| `requirements/` | What the control does, as behaviour. No implementation detail. |
| `API.md` | What the public surface is — properties, events, contracts, defaults. |
| `DESIGN.md` | Why it is built this way — decisions, alternatives rejected, costs. |
| `AGENTS.md` | How to work on it. |

### Keeping Them In Sync

Documentation drift here is treated as a defect, not a follow-up. When you change the code, update
the matching document in the same change:

- Added, removed or renamed a public property, event or method → update `API.md`, including its
  default value, and regenerate the Blazor bindings (see below).
- Changed what the user sees or can do → update the relevant file in `requirements/`, keeping the
  numbered identifiers stable. Add new numbers rather than renumbering existing ones.
- Chose an approach over an alternative, or hit a platform constraint that shaped the code → record
  it in `DESIGN.md`. A decision that cost you an hour to work out is worth a paragraph.
- Discovered the code does not meet a stated requirement → add a row to the conformance table in
  `requirements/README.md` rather than quietly weakening the requirement.
- Verified something on a device or simulator → update `DESIGN.md` section 15.

If a change makes an existing statement wrong, fix the statement. Do not append a contradiction.

## Invariants

These were each arrived at the hard way. Changing one is a design decision, not a refactor — read the
cited `DESIGN.md` section first.

- **Appointment views are pooled and rebound, never rebuilt** (§5, §6). Anything attached to a view
  must be attached once, at creation, and must read the currently bound appointment rather than
  capture one. The pool is deliberately unbounded; do not add a cap.
- **Only one week is rebuilt per swipe** (§4). The other two move by translation. Do not introduce
  work that touches all three slots on rotation.
- **The day-header sync is never gated by a flag** (§8). It runs on every scroll event. Gating it
  strands the header off-screen.
- **All input for the grid is handled on the drawing surface** (§11). Appointment views stay
  `InputTransparent`. Do not attach gesture recognizers to them, and do not use
  `PanGestureRecognizer` to detect a press — it only reports after movement has started, which makes
  long-press-to-drag impossible.
- **Platform-specific code is confined** to `ConfigurePlatformScrolling` and `SetScrollingEnabled`.
  If you need more of it, say so in `DESIGN.md` and explain why MAUI could not do the job.
- **Identity is `ISchedulerAppointment.Key`, never the instance** (§6, §11). A host may rebuild its
  collection at any moment, including mid-gesture. Match on the key, and resolve anything handed back
  to the host against the current `ItemsSource` first.
- **Every `DateTime` crossing the public API is wall-clock in `TimeZone`** (§9). The control performs
  no time-zone conversion. Do not add any.
- **Cancellable events are read synchronously.** `Cancel` is checked the moment the handler returns,
  so it cannot be set after an `await`.

## Blazor Bindings

- This project stays Blazor-free, but `Owlery.Mobile` consumes it through a generated wrapper.
- The generation attribute lives in `Owlery.Mobile/Properties/Elements.cs`; output goes to
  `Owlery.Mobile/Elements/Owlery.Scheduler/` and is never hand-edited.
- After changing the public surface, regenerate from the `Owlery.Mobile` directory:

  ```sh
  dotnet generate-maui-blazor-components
  ```

- The generator rewrites every element file in that folder. Check the diff and revert files whose
  only change is line endings.
- XML doc comments on public members are copied into the generated component, so they become the
  tooltips Razor authors see.

## File Layout

`SchedulerView` is a partial class split by concern. Keep the split as it is rather than adding
members wherever they are convenient:

| File | Holds |
|---|---|
| `SchedulerView.cs` | **All fields**, the constructor, lifecycle, geometry, platform setup |
| `SchedulerView.Properties.cs` | Bindable properties, events, property-changed handlers |
| `SchedulerView.Rendering.cs` | Laying appointments out, the day-count transition |
| `SchedulerView.Pager.cs` | Snapping, rotating the ring buffer, recentring |
| `SchedulerView.Interaction.cs` | Taps, cell selection, drag and drop |

State lives in one file on purpose. Fields declared next to the code that uses them is how a partial
class ends up with two of them meaning the same thing.

Logic that is arithmetic rather than view manipulation belongs in `Internal/` as an ordinary type —
`DropTargetResolver`, `EdgePagingDetector`, `AppointmentLayoutEngine`, `CellSelectionOverlay`. Those
are directly unit-testable without the MAUI test host, and that is the point of moving them.

Resist extracting the drag machinery wholesale behind an interface. It legitimately touches the
geometry, the pages, the pool, the scroll views and the events, so the interface would have a dozen
members and would relocate the coupling rather than reduce it.

## Build

```sh
dotnet build Owlery.Maui.Scheduler/Owlery.Maui.Scheduler.csproj -f net10.0
dotnet build Owlery.Maui.Scheduler/Owlery.Maui.Scheduler.csproj -f net10.0-ios
dotnet build Owlery.Maui.Scheduler/Owlery.Maui.Scheduler.csproj -f net10.0-android
```

- Build every target framework before considering a change done. The platform-conditional code is
  compiled separately for each, and `net10.0` compiles it out entirely — so a mistake inside
  `#if IOS` is invisible until the iOS target is built.
- **Do not make `net10.0-ios` unconditional.** The Android CI job runs on `ubuntu-latest` with only
  `maui-android` installed and publishes `Owlery.Mobile`, which references this project — restore
  walks all of its declared target frameworks. `Owlery.Mobile.csproj` carries the same guard for the
  same reason.
- `net10.0` contains no platform code, which is what lets the tests below run without a device.

## Tests

`Owlery.Maui.Scheduler.Tests` runs headlessly on `net10.0` — no simulator, no platform.

```sh
dotnet test --project Owlery.Maui.Scheduler.Tests/Owlery.Maui.Scheduler.Tests.csproj
```

- `TestApplication` builds a real MAUI app with a stub handler, so controls behave as they do in an
  app. `TestDispatcher` hands out timers the test fires by hand — both the long press and the
  scroll-quiet snap are dispatcher timers, so a test that cannot fire them cannot reach either.
- `SchedulerHarness` arranges the control to a known size and drives it through `IGraphicsView`
  interaction calls and the scroll views, so tests tap, hold, drag and swipe rather than poking at
  internals. Address positions with `PointAt(slot, day, time)` instead of raw pixels.
- **Add a test with any behaviour change.** Two bugs that had already shipped were found by writing
  this suite rather than by running the app: appointments finishing before the day window were drawn
  at the top of it, and a tap on an appointment was never reported while dragging was enabled.
- What the suite cannot reach — real gesture arbitration between the scroll views, platform paging,
  anything behind `#if IOS` — still needs a device. See the DevFlow skills.
