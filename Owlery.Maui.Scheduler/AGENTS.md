# Scheduler Control Guidelines

## Scope

- These instructions apply to `Owlery.Maui.Scheduler` and override the repository root where they
  differ. Root guidelines still apply for anything not covered here.
- `CLAUDE.md` in this folder is a symlink to this file. Keep it a symlink — never replace it with a
  copy, and never edit the two separately.

## What This Project Is

- A reusable .NET MAUI scheduler control — week, day and month surfaces — built from plain MAUI
  primitives.
- **It is a public library, and it will move to a repository of its own.** Treat it as though that
  has already happened: it cannot reach for anything in this solution, its documentation is written
  for readers who have never seen this app, and its public surface is a contract with strangers
  rather than with `Owlery.Mobile`.
- It has exactly one package reference: `Microsoft.Maui.Controls`. Do not add BlazorBindings,
  Syncfusion, CommunityToolkit, or any `Owlery.*` project reference.
- **It is not BlazorBindings-specific.** This app happens to consume it through generated Blazor
  wrappers, and that is a fact about the host, not about the control. A plain MAUI host is a
  first-class consumer — `Owlery.Maui.Scheduler.Sample` is one, and is the reference for what using
  it looks like without Blazor. Nothing in the control may assume a Blazor host, and no example in
  its documentation should require one.
- **Raw MAUI is the tie-breaker.** Where two API shapes both work, take the one an ordinary MAUI
  host would expect, even when the Blazor wrapper is indifferent or would prefer the other. The
  wrapper is generated from whatever the control exposes, so it absorbs almost any shape — which is
  exactly what makes it useless as a design signal. Judge a member by how it reads in a `Style`
  setter, in an `AppThemeBinding`, and in plain C# from the sample. Worked example: nested settings
  objects generate cleanly as Blazor child components — Syncfusion's do, in this very app — but in
  raw MAUI they cannot be reached by a per-value `Style` setter or `AppThemeBinding`, and they force
  a "null means inherit" sentinel onto every member so that a host can override one value without
  supplying them all. That is why the colours are flat bindable properties rather than a palette
  object (§20).
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
- **Every view comes from one template** (§6). A `DataTemplateSelector` is refused where it is
  assigned, because a single template is what lets any view be reused for any appointment. Hosts vary
  content *inside* a template instead, which is both the recommended pattern and the faster one — a
  variant switch costs a rebind rather than a fresh build. Supporting selectors means a pool per
  template and a second clause in `PopulateSlot`; read §6 before deciding it is worth that.
- **Only one week is rebuilt per swipe** (§4). The other two move by translation. Do not introduce
  work that touches all three slots on rotation.
- **The day-header sync is never gated by a flag** (§8). It runs on every scroll event. Gating it
  strands the header off-screen.
- **All input for the grid is handled on the drawing surface** (§11). Appointment views stay
  `InputTransparent`. Do not attach gesture recognizers to them, and do not use
  `PanGestureRecognizer` to detect a press — it only reports after movement has started, which makes
  long-press-to-drag impossible. This extends to **anything inside the vertical scroll view**: a
  gesture recognizer there claims the drag and the timeline stops scrolling under it, which is how the
  hour gutter ended up with a `GraphicsView` of its own (§12). The day headers may keep a
  `TapGestureRecognizer` only because they sit outside that scroll view.
- **A tap is verified with `adb shell input tap`, never with `maui devflow ui tap`** (§15). The CLI
  invokes the gesture recognizer without a position, so `TappedEventArgs.GetPosition` returns null and
  a working handler looks broken. It cannot confirm a tap either way.
- **A custom handler must still measure its content — on iOS** (§19). Replacing `ContentViewHandler`
  with a bare `ViewHandler<,>` loses the measurement it did for free, and a platform scroll view asked
  how big it wants to be answers with its current bounds — so the view grows but never shrinks.
  `GetDesiredSize` is overridden for that, and **only in the iOS partial**. Measuring cross-platform
  content from a handler satisfies MAUI's measure cache, which on Android starves the pass that both
  measures and arranges it — every child of the pager comes out unsized and the grid renders blank.
  More generally: taking over a handler takes over *everything* it did, not only the part you wanted.
- **The hour gutter and the pager share one vertical alignment** (§19). Both are `LayoutOptions.Start`.
  When the hours are shorter than the screen the scroll view's row is taller than the content, and the
  platforms split that slack differently — Android centres the gutter, iOS centres the pager — so
  anything other than the two of them agreeing puts the hour labels off the lines they name on one
  platform or the other.
- **Platform-specific code is confined** to `Handlers/`, plus `ConfigurePlatformScrolling` and
  `SetScrollingEnabled` for the timeline's vertical scroll. If you need more of it, say so in
  `DESIGN.md` and explain why MAUI could not do the job. `Handlers/` earned its place the hard way —
  read §19 before changing it, and do not assume a platform scroll view behaves like the other one.
- **The pager decides which page a swipe lands on** (§3, §19). The control reacts to `PageSettled`;
  it does not infer the page from the offset going quiet. `OnPageSettled` must stay synchronous — a
  frame drawn between the rotation and the recentre shows a page the user never swiped to.
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
- Regenerating is a downstream step, never a design input. What the generator makes convenient has no
  bearing on what the public surface should be — see *What This Project Is*.

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
| `PagingScrollView.cs` | The horizontal pager's cross-platform half (§19). Internal. |
| `Handlers/` | Its iOS and Android handlers, and their platform views. Internal. |
| `AppHostBuilderExtensions.cs` | `UseOwleryScheduler()`, the one line a host must call |

State lives in one file on purpose. Fields declared next to the code that uses them is how a partial
class ends up with two of them meaning the same thing.

Logic that is arithmetic rather than view manipulation belongs in `Internal/` as an ordinary type —
`DropTargetResolver`, `EdgePagingDetector`, `AppointmentLayoutEngine`, `MonthLayoutEngine`. Those are
directly unit-testable without the MAUI test host, and that is the point of moving them.

A self-contained *piece of chrome* belongs there too, owning its own views rather than scattering
them across the partial class — `CellSelectionOverlay` and `TimeGutter`. Each exposes a view to place
and a method or two to drive it, which is what keeps `SchedulerView.cs` a list of fields rather than a
list of every label in the control.

Resist extracting the drag machinery wholesale behind an interface. It legitimately touches the
geometry, the pages, the pool, the scroll views and the events, so the interface would have a dozen
members and would relocate the coupling rather than reduce it.

## Performance

Everything here was learned by measuring this control, usually after guessing wrong first.

- **Measure before changing anything.** The .NET side has repeatedly turned out to be the cheap part:
  a per-frame animation callback measured 0.05–0.54ms while the frames around it took 17ms. Instrument
  with a `Stopwatch` and `Debug.WriteLine`, read it back with `adb logcat`, and use
  `adb shell dumpsys gfxinfo <pkg>` for what the frame actually cost — `Invalidate()` only queues the
  draw, so a stopwatch cannot see it.
- **Identity, not equality, drives binding** (§6). Hand back the same appointment instance when nothing
  changed; build a new one when something did. This is a contract with the host, and it is in `API.md`
  for that reason.
- **Do not write a layout bound that has not changed.** An unchanged write still costs a layout pass,
  and on Android an arrange is a JNI call per child. `PositionAppointmentView` and
  `TimeGutter.ShowIndicator` both guard; anything running per frame or per touch should too.
- **Coalesce data-driven rebuilds, never gesture-driven ones.** `QueueRepopulate` collapses a burst of
  `ItemsSource` changes into one rebuild on the next tick. The repopulate calls a drag makes are how
  the gesture puts the pages back and must stay synchronous.
- **Watch for work that scales with the host's data rather than the screen.** Each `PopulateSlot` runs
  the layout over the whole `ItemsSource`, and `Resolve` scans it per interaction. Fine today; the
  thing to check when a range widens, as it did when month view took prefetch from three weeks to four
  months.
- **Cache anything that crosses into Java.** `Context.Resources.DisplayMetrics.Density` was being read
  on every scroll frame.

### Testing a performance change

A performance fix usually has no observable behaviour, which makes it very easy to write a test that
passes whether or not the fix is present. **Mutation-check every one**: undo the change, confirm the
test fails, put it back. Three tests in this control's history passed with and without the fix they
were written for, and each looked convincing:

- Counting views could not tell "the drag was cancelled" from "no views were built at all" — both
  leave exactly one. It needed a second view type to identify which template the survivor came from.
- Asserting on geometry to prove a guard was engaged actually measured the clamp beside it. Asserting
  on the drop target's own text was the honest question.
- A guard that *skips* work cannot be caught by removing the skip — the result is identical. Test the
  direction that can break: that the work still happens when its inputs change.

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

## Sample App

`Owlery.Maui.Scheduler.Sample` is a bare MAUI app — no Blazor, no XAML — that hosts nothing but this
control, a generator, and a drawer of knobs bound to the public properties. It is the shortest route to
a device for anything the headless suite cannot reach: gesture arbitration, platform paging, drag-and-
drop, and how a change reads at a real phone size.

```sh
dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-ios -t:Run
dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-android -t:Run
```

- Reach for it before `Owlery.Mobile`. It builds in a fraction of the time, needs no signing identity,
  no Firebase configuration and no API, and its appointment count knob goes to five thousand.
- It carries `Microsoft.Maui.DevFlow.Agent` in **Debug only**, so `maui devflow ui` can query, tap and
  screenshot it from a terminal. Sampling a property back mid-animation — the header strip's
  `TranslationX`, say — proves a transition animates where a screenshot usually arrives too late. Note
  that rapid repeated taps on one element are dropped; see the sample's `README.md`.
- It consumes the same public surface a host does, so it also catches a property that became awkward to
  set from outside. Add a knob when you add a property.
- Its templates are the reference for the pooling rule — see its `README.md`.

## Tests

`Owlery.Maui.Scheduler.Tests` runs headlessly on `net10.0` — no simulator, no platform.

```sh
dotnet test --project Owlery.Maui.Scheduler.Tests/Owlery.Maui.Scheduler.Tests.csproj
```

- `TestApplication` builds a real MAUI app with a stub handler, so controls behave as they do in an
  app. `TestDispatcher` hands out timers the test fires by hand — the long press is a dispatcher
  timer, so a test that cannot fire it cannot reach dragging.
- Paging is driven by `SwipeToPage`, which places the offset and reports the page settled. There is
  no handler in the test host, so `PagingScrollView` holds whatever it was last told and does not
  page by itself — which is what makes the suite deterministic, and why nothing about the platform
  handlers is covered by it.
- `SchedulerHarness` arranges the control to a known size and drives it through `IGraphicsView`
  interaction calls and the scroll views, so tests tap, hold, drag and swipe rather than poking at
  internals. Address positions with `PointAt(slot, day, time)` instead of raw pixels.
- **Add a test with any behaviour change.** Two bugs that had already shipped were found by writing
  this suite rather than by running the app: appointments finishing before the day window were drawn
  at the top of it, and a tap on an appointment was never reported while dragging was enabled.
- What the suite cannot reach — real gesture arbitration between the scroll views, platform paging,
  fling prediction, clipping, anything in `Handlers/` — still needs a device. Three defects in the
  pager were found only by running it; see DESIGN.md §19. Android is reachable through the DevFlow
  skills, and `references/android.md` there records the deployment traps.
