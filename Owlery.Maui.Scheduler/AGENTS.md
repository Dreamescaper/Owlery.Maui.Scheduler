# Scheduler Control Guidelines

## Scope

- These instructions apply to `Owlery.Maui.Scheduler`, the control project. The sample and the tests
  have guidance of their own; anything not covered here is a judgement call.
- `CLAUDE.md` in this folder is a symlink to this file. Keep it a symlink — never replace it with a
  copy, and never edit the two separately.

## What This Project Is

- A reusable .NET MAUI scheduler control — week, day and month surfaces — built from plain MAUI
  primitives.
- **It is a public library in a repository of its own, published to nuget.org.** It cannot reach for
  anything outside this repository, its documentation is written for readers who have never seen the
  app it came from, and its public surface is a contract with strangers. Renaming a public member is
  now a breaking change for people you cannot ask.
- It has exactly one package reference: `Microsoft.Maui.Controls`. Do not add BlazorBindings,
  Syncfusion, CommunityToolkit, or any `Owlery.*` project reference.
- **It is not BlazorBindings-specific.** One known consumer happens to reach it through generated
  Blazor wrappers, and that is a fact about that host, not about the control. A plain MAUI host is a
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

Four documents describe this project. They answer different questions and must not be merged. All
of them except this one live in `docs/`, at the repository root — the project folder holds code and
agent instructions only:

| Document | Answers |
|---|---|
| `docs/requirements/` | What the control does, as behaviour. No implementation detail. |
| `docs/API.md` | What the public surface is — properties, events, contracts, defaults. |
| `docs/design/` | Why it is built this way — decisions, alternatives rejected, costs. |
| `AGENTS.md` | How to work on it. |

`docs/design/` is one file per subject, and each keeps the section numbers the decisions were written
with — §1 to §21, grouped rather than renumbered, so an older reference to "DESIGN.md section 19"
still resolves through `docs/design/README.md`. Cite them as `docs/design/pager.md` (§19) rather than
by number alone, and add a new section to the file whose subject it shares rather than starting a
file per decision.

### Keeping Them In Sync

Documentation drift here is treated as a defect, not a follow-up. When you change the code, update
the matching document in the same change:

- Added, removed or renamed a public property, event or method → update `docs/API.md`, including its
  default value, and regenerate the Blazor bindings (see below).
- Changed what the user sees or can do → update the relevant file in `docs/requirements/`, keeping
  the numbered identifiers stable. Add new numbers rather than renumbering existing ones.
- Chose an approach over an alternative, or hit a platform constraint that shaped the code → record
  it in the matching `docs/design/` file. A decision that cost you an hour to work out is worth a
  paragraph.
- Discovered the code does not meet a stated requirement → add a row to the conformance table in
  `docs/requirements/README.md` rather than quietly weakening the requirement.
- Verified something on a device or simulator → update `docs/design/verification.md` (§15).

If a change makes an existing statement wrong, fix the statement. Do not append a contradiction.

## Invariants

These were each arrived at the hard way. Changing one is a design decision, not a refactor — read the
cited design section first; `docs/design/README.md` maps every § to its file.

- **Every drawn view belongs to a page** (§6). A visible view in none of the three pages cannot be
  pressed and is never translated again, so it sits on one column while the weeks scroll past — and
  nothing recovers it. Any path that takes a view out of a page must put it in another or hand it to
  the pool. The drag's view is the only exception, and `PlacedViews` names it so the tests can hold
  the rest to the rule; add to that property, do not work around it.
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
  `docs/design/` and explain why MAUI could not do the job. `Handlers/` earned its place the hard
  way — read §19 before changing it, and do not assume a platform scroll view behaves like the other
  one.
- **The pager decides which page a swipe lands on** (§3, §19). The control reacts to `PageSettled`;
  it does not infer the page from the offset going quiet. `OnPageSettled` must stay synchronous — a
  frame drawn between the rotation and the recentre shows a page the user never swiped to.
- **Identity is `ISchedulerAppointment.Key`, never the instance** (§6, §11). A host may rebuild its
  collection at any moment, including mid-gesture. Match on the key, and resolve anything handed back
  to the host against the current `ItemsSource` first.
- **Appointments are read, never observed** (§9). Nothing on `ISchedulerAppointment` is a bindable
  property and the contract does not require `INotifyPropertyChanged`; a changed appointment reaches
  the control as a new instance in the collection. Do not add a per-appointment subscription — that is
  one subscription per item across the whole range a host is told to keep loaded, to learn something
  the collection already reports.
- **A host's `DateTime` is interpreted in exactly one place** (§9). `AppointmentTime.In` holds the
  whole rule — `DateTimeKind` decides, `ISchedulerAppointment.TimeZone` speaks only when the kind is
  silent — and everything that lays appointments out goes through it. Do not re-derive it per surface.
- **The floating fast path is load-bearing, not a tidy-up** (§9, §15). `Unspecified` with no zone, and
  anything already in the view's zone, must return without touching the time-zone database. Measured:
  removing it costs the timeline layout 4× for UTC appointments and 16× for zoned ones, per slot,
  three slots per rebuild, over a collection hosts are told to keep wholly loaded.
- **What the control reports is a grid position, not an instant** (§9). Outbound values are
  `SchedulerMoment` — wall-clock in `TimeZone` plus that zone — and the host picks a representation
  from it. Do not add an implicit conversion to `DateTime`: the type exists so that choice cannot be
  made by accident.
- **Cancellable events are read synchronously.** `Cancel` is checked the moment the handler returns,
  so it cannot be set after an `await`.
- **The control refers to `INotifyCollectionChanged`, never a concrete collection type** (§9).
  `ItemsSource` stays `IEnumerable<ISchedulerAppointment>`; `SchedulerAppointmentCollection<T>` is a
  convenience for hosts, not a dependency of the control. `INotifyCollectionChanged` is optional: a
  host may hand over a plain enumerable and get the same rendering, minus live mutation. Do not add a
  cast or a `using` of the collection type anywhere in the control.
- **The `ItemsSource` subscription lives as long as the view is loaded, not as long as the property**
  (§9). It is dropped on `Unloaded` and taken again on `Loaded`, because hosts are told to keep one
  long-lived collection and a subscription is a reference from that collection back to the view.
  `SyncItemsSubscription` is the single place that attaches or detaches; do not subscribe anywhere
  else.

## Blazor Bindings

- This project stays Blazor-free. A consuming app that uses BlazorBindings.Maui generates its own
  wrapper **in its own repository**, from `[assembly: GenerateComponent(...)]` against this package —
  there is nothing to regenerate here, and no generated file lives in this repo.
- That wrapper is regenerated on the consumer's side after a release changes the public surface. A
  rename that costs one commit here costs them a package bump and a regeneration.
- XML doc comments on public members are copied into the generated component, so they become the
  tooltips Razor authors see. They also ship in the package, so they are the IntelliSense every
  consumer gets — `GenerateDocumentationFile` is on for that reason.
- Regenerating is a downstream step, never a design input. What the generator makes convenient has no
  bearing on what the public surface should be — see *What This Project Is*.
- `SchedulerAppointmentCollection<T>` is part of the public surface too, so adding to it is a
  consumer-side regeneration like any other member.

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
  changed; build a new one when something did. This is a contract with the host, and it is in
  `docs/API.md` for that reason.
- **Do not write a layout bound that has not changed.** An unchanged write still costs a layout pass,
  and on Android an arrange is a JNI call per child. `PositionAppointmentView` and
  `TimeGutter.ShowIndicator` both guard; anything running per frame or per touch should too.
- **Coalesce data-driven rebuilds, never gesture-driven ones.** `QueueRepopulate` collapses a burst of
  `ItemsSource` changes into one rebuild on the next tick. The repopulate calls a drag makes are how
  the gesture puts the pages back and must stay synchronous.
- **Watch for work that scales with the host's data rather than the screen.** Each `PopulateSlot` runs
  the layout over the whole `ItemsSource`, and `Resolve` scans it per interaction. Measured at up to
  60,000 loaded appointments and left alone deliberately — the off-page rejection is the cheap half of
  the scan, so a date index buys about a quarter of a repopulate; §15 has the numbers and the
  reasoning. Re-measure rather than re-argue if a range widens again.
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
- **Do not make `net10.0-ios` unconditional.** Restore walks every declared target framework, so a
  consumer building only for Android on Linux — a common CI shape, with just `maui-android`
  installed — fails on a target framework it never asked for. The guard costs nothing here and is
  the difference between the package being usable on a Linux runner and not. The release workflow
  runs on macOS precisely so the iOS target is built and packed.
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

- Reach for it before any real host app. It needs no signing identity, no backend and no
  configuration, and its appointment count knob goes to five thousand.
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
  pager were found only by running it; see `docs/design/pager.md`. Android is reachable through the
  DevFlow skills, and `references/android.md` there records the deployment traps.
