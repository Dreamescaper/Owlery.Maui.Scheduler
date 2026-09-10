# 15. Verification status

`Owlery.Maui.Scheduler.Tests` covers the platform-independent behaviour headlessly on `net10.0`:
overlap layout and clipping, appointment placement, view reuse across a refresh, cell and appointment
taps, long-press-to-drag including refusal and the scroll-not-drag case, and week paging. It runs
without a simulator, and found two bugs that had already shipped.

The month surface of [section 18](month.md) is covered to the same standard: grid alignment for Monday and Sunday
weeks, the six-row window, per-day stacking and the overflow rule as arithmetic; then, through the
control, chip placement, month paging across a year boundary, the 42-day visible-dates report, whole-
day cell selection, that a held chip is not picked up, and that switching modes leaves nothing of the
old one behind.

The month surface has now been seen on an Android emulator through `Owlery.Maui.Scheduler.Sample`: the
six-row grid, the chips, the muted leading and trailing days and the day-cell selection all read
correctly at phone size, and switching modes and back leaves nothing behind. It has not been seen on
iOS, and the host does not yet offer the mode.

The agenda layout engine, surface and host integration are covered headlessly: grouping and stable
ordering, empty month/week/day rules, window slicing, measured-height reflow and compensation, cached
measurements across rebuilds, bounded realized view count, identity-preserving reuse while windows
overlap, forward and backward range growth with anchor preservation, section accessibility, mode
cleanup, disabled paging and date navigation. Device verification on the Android emulator found the
upward-scroll correction wobble discussed in [section 21](agenda.md), and the sample now avoids it by supplying a
known row height; gesture arbitration with the shared drawing surface is still to be confirmed on a
device.

Fast downward scrolling has been profiled on the Android `sdk_gphone64_arm64` emulator with 5,000
appointments per month in a linked Release build. Three identical 30-fling runs produced median
frame percentiles of 17/20/22/32 ms (p50/p90/p95/p99) with measured heights and 17/19/21/32 ms with
the exact 42.5/62.7-unit heights supplied through `AgendaRowHeight`. Deadline jank was 8.44% versus
9.24%, respectively, so the height function did not materially improve frame delivery. It did remove
all measure/correction passes and reduced steady managed work from about 2.51 to 2.16 ms per processed
scroll update. Reconciliation remained the managed hotspot; the roughly 17 ms frame centre and
overlapping jank results show that rendering/frame scheduling, not row measurement, sets perceived
smoothness in this workload.

The cost of laying out from the whole `ItemsSource` has been measured, headlessly, to answer whether
the per-slot scan should be replaced by a date index now that hosts are told to keep everything loaded
(§9). On an M-series Mac in Release, per call, fastest of forty:

| Items loaded | On the week page | Timeline layout | …pre-filtered to the page | Month grid | …pre-filtered | Agenda, 6-month range |
|---|---|---|---|---|---|---|
| 4,800 (200/mo × 24) | 36 | 0.036 ms | 0.008 ms | 0.058 ms | 0.036 ms | 0.222 ms |
| 12,000 (1,000/mo × 12) | 217 | 0.077 ms | 0.039 ms | 0.147 ms | 0.132 ms | 0.720 ms |
| 60,000 (5,000/mo × 12) | 1,117 | 0.306 ms | 0.196 ms | 0.821 ms | 0.713 ms | 1.630 ms |

A whole repopulate — three slots laid out, then every view on the page reconciled, rebound and
positioned — cost 0.27 ms at 4,800 items, 1.00 ms at 12,000 and 4.75 ms at 60,000, of which the layout
was 58%, 38% and 38% respectively.

Re-measured after appointments gained a zone model (§9), same machine and method, timeline layout of
one page:

| Items loaded | Floating (fast path) | UTC | Zoned (full conversion) |
|---|---|---|---|
| 4,800 | 0.046 ms | 0.399 ms | 1.627 ms |
| 12,000 | 0.104 ms | 1.019 ms | 1.420 ms |
| 60,000 | 0.410 ms | 1.649 ms | 6.681 ms |

The floating column is the same order as the 0.306 ms measured before the change, so the default costs
what it always did. The other two are the whole argument for the fast path: reading appointments as
instants costs roughly 4× and reading them as wall-clock in another zone roughly 16×, per slot, three
slots per rebuild. At 60,000 zoned appointments a repopulate is about 20 ms of layout on this machine
before a phone multiplier — so a host that puts a zone on every appointment across a wide loaded range
is in different territory, and that is worth knowing before adopting the feature rather than after.

**The index was not built, and the measurement is why.** Pre-filtering to the page is the ceiling of
what a date index could deliver, and it removes a third to a half of the layout — a quarter of a
repopulate at worst, on an operation that is coalesced to one per tick and never runs during a drag.
The reason the saving is small is that the rejection is the cheap half: two subtractions and a
comparison per off-page item. What costs is bucketing, sorting and cluster-packing the items that are
genuinely on the page, and at 5,000 a month a single week page holds 1,117 of them — work no index can
remove, for a page no one would want to look at. Revisit only if a real host reports a problem, and
note that at that scale the view reconciliation, not the scan, is the larger share.

Not covered by this: it is a desktop measurement, not a device one. A phone would be some multiple
slower, and that multiple has not been measured.

Rendering, paging, week rotation, overlap layout, the current-time line and appointment semantics have
also been checked on the iOS simulator through DevFlow.

Paging has now been **profiled on the iOS simulator** — an iPhone 17 Pro on iOS 26.5, a Debug build,
through `Owlery.Maui.Scheduler.Sample`. The workload is `PerfRun` in the sample, run unattended by
launching with `OWLERY_PERF=1`: it steps `DisplayDate` twelve times per case, six forward and six
back, and reports two numbers per case. A `Stopwatch` says what the synchronous managed pass cost; a
`CADisplayLink`, in the sample's `FrameMeter`, says what intervals the display actually presented
around it — which is where the layout and native arranging that the pass only *queued* land, and a
stopwatch cannot see them. A swipe itself still cannot be injected on iOS, so `DisplayDate` stands in
for one; it settles through the same rotation and repopulate.

That run is what found the pool round trip on rotation described in [section 6](appointment-views.md),
which cost 60–76% of a page change depending on surface and volume. It also answered two questions the
other way, and those answers are worth keeping:

- **Replacing `ItemsSource` wholesale is cheap.** Twelve whole-collection replacements with equivalent
  data measured 0.10–0.14ms of managed work each and dropped no frames at any volume up to 5,500
  loaded, because the reconciliation matches every view by key and the guards then skip every write.
  A burst of five replacements per run-loop turn, eight times over — the shape a host publishing once
  per answered period produces — cost 0–7% late frames. A host doing that is not paying for it.
- **The host's own callback is not in the path.** `VisibleDatesChanged` is raised synchronously inside
  a page change, so what the host does there is inside the pass; the sample's handler measured 0.4–0.7ms
  of a 240–310ms pass.

Caveats: it is a simulator rather than a device, and a Debug build — a Release build for the simulator
fails to load corlib on this SDK, so the absolute figures overstate what a shipped build does. The
*shape* is what the run is for, and the shape is that a page change costs work proportional to the
number of appointment views on the incoming page, at roughly 2ms per view before the fix and roughly
0.6ms after it. Neither number has been checked on hardware.

Drag-and-drop is confirmed working on the iOS simulator by manual testing. It could not be automated:
DevFlow drives gestures by invoking a MAUI gesture recognizer and reports that "native pan injection
is not available on iOS", and input is deliberately handled through `GraphicsView` touch events rather
than a recognizer. The *failure* of the previous implementation was captured through DevFlow before it
was replaced — that trace is what identified the race described in [section 11](interaction.md).

Android has now been exercised, on a `Medium_Phone` emulator through DevFlow: the week and month
surfaces render, the gutter labels correctly, and paging moves exactly one page per fling in both
directions and stays exact over repeated swipes. Three defects were found doing it, all recorded in
[section 19](pager.md); none of them was reachable from the headless suite, and two of them looked like correct
code until the platform disagreed.

Drag-and-drop is confirmed on Android too, on the same emulator through the sample app, by injecting a
press, a dwell and a sequence of moves: the long press lifts the appointment, the faded copy stays
behind, the gutter shows the time under the finger, and the drop is reported at the snapped start and
settles where the host put it. A locked appointment is refused, so the cancel path is exercised as
well. This is where `SetScrollingEnabled` takes its `RequestDisallowInterceptTouchEvent` branch.

That pass predates the drop settling from the model ([section 11](interaction.md)), and the sample
applied its move synchronously then as it does now, so what was watched still holds. What nobody has
watched on a device is the drop that changes nothing — dropping an appointment back on its own time,
then picking it up again — which is the case that settling was changed for.

Still unverified there: paging mid-drag against a leading or trailing edge, and whether the
neighbouring-page flash is gone — the window it happened in is closed by construction, but nobody has
watched for it.

The animated slide when paging mid-drag is asserted in tests only as a sequence of scroll requests —
the test shim applies them instantly. That it *looks* right, and that a programmatic scroll still runs
while `UIScrollView.ScrollEnabled` is false, both need a device.

**The slide onto an adjacent page ([section 4](paging.md)) is confirmed on the iOS simulator**, through the sample
app and DevFlow. The test shim cannot show that a scroll animates, so the header strip's
`TranslationX` — which mirrors the pager offset on every scroll frame — was sampled repeatedly across
a page change. It moves, in the right direction, and comes to rest centred:

| | forward (`›`) | backward (`‹`) | four weeks back (`Today`) |
|---|---|---|---|
| at rest | −350 | −350 | −350 |
| after the jump | −24 | −664 | −350 |
| mid-slide | −237 | −396 | −350 |
| settled | −350 | −350 | −350 |

The two neighbours animate between the outgoing page and the centre; the far jump stays pinned at the
centre for the whole tap, which is the rebuild taking the blunt path on purpose. The title tracked
each one.

**`HeaderTapped` and `TimeGutterTapped` are confirmed on the Android emulator**, by injecting real
touches with `adb shell input tap`. A tap on a day header reports that day and the sample drops into a
one-day view; a tap on the gutter reports the time under the finger, snapped down — 13:15 for a touch
just below the 13:30 line. The hour labels still render above the input surface on both platforms.

Getting there found a regression that only a real touch could: the gutter's first implementation used
a `TapGestureRecognizer`, which claimed the drag and stopped the timeline scrolling when a swipe began
on the hours. See [section 12](interaction.md). **DevFlow could not have caught it, and worse, could not have caught the
feature working either** — its synthetic tap invokes the recognizer without a position, so
`TappedEventArgs.GetPosition` returns null and the handler correctly declines to report a tap it
cannot place. Both events looked broken through the CLI on both platforms and were fine all along.
`adb shell input` is the way to test a tap; the CLI's tap is not a touch.

The iOS side of both events is therefore still unverified — there is no touch injection for the
simulator here, and DevFlow's tap cannot stand in for one. Only the rendering was checked there.

**Vertical scrolling is confirmed correct on both platforms** after the two measurement fixes in
sections [18](month.md) and [19](pager.md). On the simulator the numbers line up exactly — a month's content, the pager and
the scroll viewport all measure 588 where the pager used to insist on 900. On the emulator, where a
real gesture can be injected, `ScrollView.ScrollY` was read across an `adb shell input swipe`:

| | after an up-swipe |
|---|---|
| timeline, `HourHeight` 50 | 75.0 — scrolls, as it should |
| timeline, `HourHeight` 20 (content shorter than the viewport) | 0 |
| month | 0.38 |

That 0.38 is one physical pixel at this emulator's 2.625 density — rounding in the device-independent
to pixel conversion, not scrollable slack. It was several hundred before. Left alone deliberately:
flooring the measured height to whole pixels to chase it risks clipping the bottom row of cells, which
is a worse defect than a pixel of travel nobody can see.

One trap found doing this, and it is DevFlow's rather than the control's: **rapid repeated taps on a
single element are dropped**. Four taps 0.6 s apart on `›` advanced the calendar once, which reads
exactly like a paging bug. It is not — the same four taps on the appointment-count `+` button, which
shares no code with paging, behaved identically. Space the taps out, and assert on what the app
reports rather than on how many taps were sent.

Nothing about the platform handlers is covered headlessly: with no handler registered, `PagingScrollView`
holds whatever offset it was last told and raises `PageSettled` when a test says so, which is exactly
what the suite wants and exactly what cannot catch a fling prediction or a clipping quirk.

**Opening a day zooms onto that day and nothing else — confirmed on the iOS simulator** (see
[section 16](timeline.md)). The transition's *motion* is skipped without a handler, so no headless test can reach it; what the
suite covers is the shift arithmetic in `DayCountTransition` and the decision not to slide — which is
reachable because the gate is a day-count change in the current tick rather than a running animation.
The motion itself was checked on a device.

Three temporary changes made it observable, none of them kept: the sample's `HeaderTapped` handler was
made to write `VisibleDays` before `DisplayDate` (the order that reproduces the defect, and the one a
host binding both properties may well get); `DayCountAnimationMs` was raised to 8000 so a screenshot
lands mid-transition; and `SlideToAdjacentPageAsync` was made to print a line, read back through
`simctl launch --console-pty`. Then: open Thursday, return to seven days, open Friday.

| | slides after the zoom | mid-transition frame |
|---|---|---|
| before | once, forward | — |
| after | never | Friday's column, widening in place |

The sample's own log shows the double move either way — the centre page is reported as Thursday and
then as Friday for one tap — because re-aiming still rebuilds onto the page asked for. What changed is
that the second move is no longer a page slide.

**Leaving the agenda comes to rest on a whole page — confirmed on the iOS simulator**, iPhone 17 Pro
(402pt wide), iOS 26.5, Debug, through `Owlery.Maui.Scheduler.Sample` with `VisibleDays` 1 and 100
appointments a month. Agenda, then Timeline. Before the fix the pager settled at an offset of 52 —
the gutter's width — and stayed there: the day header read the *previous* day, a page boundary sat at
350pt of the 402pt screen, the chips on the visible page were clipped by their first 52pt, and the
next page's content showed past the boundary.

The cause is not the recentre's arithmetic, which was checked first and is right: the control asks
for one viewport, 350, every time. It is that the offset is asked for while the platform scroll view
still measures the *agenda*, whose surface is one viewport wide rather than three ([section 2](paging.md)) —
a frame and a content size, both real, neither wide enough. `SetOffset` only deferred when one of
them was zero, so it wrote the offset instead, and UIKit clamped it to the old content on the next
layout. Logged from inside `MauiPagingScrollView`:

| | bounds | content | offset |
|---|---|---|---|
| in the agenda | 402 | 402 | 0 |
| `SetOffset(350)` asked for, and taken | 402 | 402 | 350 |
| the layout that followed | 350 | 1050 | **52** |

52 is what 350 clamps to against the agenda's content — 402 − 350 — and nothing re-applied it,
because clearing `deferredOffsetX` had already given up the request. Deferring on whether the content
reaches the offset, which is the test Android's handler already made, leaves the same three steps
ending at 350. Re-checked afterwards on the same device: the day header matches the title and is
centred, no boundary is visible, no chip is clipped; a swipe still settles on the next day; and
Agenda → Month, the other transition that widens the surface, lands on a full-width grid.

The arithmetic is `PagingOffset.Fits`, extracted so it is testable without a platform — the headless
suite cannot reach this defect, since with no handler the harness applies whatever offset it is
handed and models no clamp. A control-level test that the recentre asks for a whole page is worth
having and is *not* what covers the fix: it passed before it too.

---

**The viewport is measured off the body, not the control — confirmed on the Android emulator**, a
2400 × 1080 landscape device with a 136 px camera cutout, at 2.625 density. The control was the
whole content of its page, which is what leaves its own root `Grid` as the first layout able to
consume the platform's inset ([section 8](grid-and-scrolling.md)).

Read back through `maui devflow ui tree`, which is what separated the two widths — a screenshot
shows the clipping but not which element the inset landed on:

| | width (dp) |
|---|---|
| `SchedulerView` | 914.286 |
| its root `Grid` | 914.286 |
| the header grid and the body, both its children | **862.476** |

914.286 − 862.476 is 51.81, the cutout. The pages were built 862.286 wide — 914.286 less the 52
gutter — inside a strip of 810.286, so the seventh column was clipped by the inset.

Measured off the screenshots, before and after, along a row inside the grid:

| | column width | day boundaries visible | last boundary |
|---|---|---|---|
| before | 323 px | 6 | would fall at 2535, off-screen |
| after | 304 px | **8** — every column closed | **2399**, the right edge |

304 px is 810.476 dp ÷ 7, which is the body's width less the gutter: the columns now divide what
they are drawn in.

Two things were checked rather than assumed:

- **Rotating the cutout to the right edge did not move the clipping**, which is what showed this to
  be a width error rather than an offset one. With the cutout on the right the content box became
  `[0, 2264]` and the seventh column was still cut, at 2264.
- **`SafeAreaEdges.None` on a layout does not stop the inset, it moves it down.** Set on the
  sample's page-level `Grid`, the inset reappeared on the next layout inside it — that grid went to
  the full 914.286 and its child moved to x = 63.81, its own 12 padding past the 51.81 inset. This
  is why the control cannot simply opt out and keep measuring its own width.

The headless suite cannot reach any of this: with no handler the body reports `-1`. What it does
cover is the `Padding` half of the same defect, which reaches the geometry by the same route.

**The same fix down the vertical axis was re-checked on the same emulator.** The height path feeds
the month surface, which is exactly one viewport tall, so a mistake there would resize every month
cell. Both surfaces were driven after the change: the timeline still shows seven whole columns, and
the month still draws six rows filling the body with the trailing row whole and the grid ending on
the navigation bar. The app's page consumes the bottom inset itself, so the vertical defect is
latent there rather than visible — what was verified on the device is the absence of a regression;
the defect itself is covered headlessly through `Padding`, as above.

## The agenda's realization pass, measured on Android

A slow drag on the agenda stalls the scroll for a frame or two at a regular interval. Instrumented on
the x86 emulator (Release, no AOT, 100 appointments a month, ~30 realized rows), the stall follows
every `PopulateSlot` on the scroll path, and nothing else: no compensating shift, no table rebuild, no
range growth appears in the trace between the ends of the range. The interval is the realization gate,
`Overscan / 2` — a quarter of a viewport, so four passes per screen of travel.

Three measurements were needed to find where the pass goes, and the first two were wrong:

- **A `Stopwatch` per row measures itself.** A per-row probe attributed 55% of the pass to rebuilding
  the accessibility description. Replacing it with one clock read per phase moved that to 0.01ms. A
  clock read costs ~17µs here; 30 rows × 4 reads is most of a pass.
- **Frame jank cannot validate a change this size.** `dumpsys gfxinfo` over a fixed scroll workload
  gave 23.2%, 21.9% and 15.5% janky frames across three runs *of the same binary*. Anything under a
  few milliseconds is inside that.
- What does work is counting the work and amortizing its cost over repetitions, both of which are
  stable to the third digit.

**The pass is the host's rebinds, and almost nothing else.** `view.BindingContext = appointment` is
60–90% of it, at ~0.9ms per rebind for the sample's `Border` + stack + two `Label`s. The whole
O(window) sweep the reconciliation performs over the other rows — repositioning all 30, both
dictionary writes, the layout-bounds reads — totals **0.02ms**. There is nothing to win there.

**Total rebinds over a distance is invariant; the gate only batches them.** Every row entering the
window is bound exactly once however often the window is recomputed. Measured over the same workload:

| gate | passes | pass median | p90 | rebinds/pass | **total rebinds** | sum of passes |
|---|---|---|---|---|---|---|
| `Overscan / 2` (187dp) | 20 | 4.00ms | 5.58 | 3 | **69** | 83ms |
| 96dp | 40 | 2.65ms | 4.43 | 2 | **75** | 118ms |
| 32dp | 104 | 1.79ms | 2.85 | 1 | **71** | 195ms |

So the stall is a batching artefact rather than a quantity of work, and the gate trades peak against
total: halving it roughly halves the peak and roughly doubles the managed total, because ~1ms of each
pass is fixed cost paid whether or not anything churned. Lowering it is therefore a judgement about
which of the two the reader notices, not a free win, and it is left at `Overscan / 2` until the fixed
cost is smaller.

What was taken instead is the fixed cost itself: slicing no longer allocates a placement per realized
row per pass, headings no longer re-render when their colours have not moved, and a description is no
longer rebuilt to discover it is unchanged. Together they measured a median pass of 4.00 → 3.66ms at
the unchanged gate — inside the run-to-run spread on this rig, which is why the counters rather than
the clock are what say they did anything: descriptions rebuilt per pass fell from 30 to 3, headings
re-rendered from 12 to 2, and allocations per slice from ~54 to none.

**The host's template is the lever a consumer holds.** At ~0.9ms a rebind the sample's row is the
expensive shape; the same content drawn by one self-painting `GraphicsView` was measured at 0.11–0.36ms
(§21). Four of those a pass is a frame; four of the former is three.

### Three renderings of the same row, compared on the device

The row template is the host's, and §21 says its rebind cost is the pass. Three renderings of the
same agenda row — identical content, identical height — were built into the sample behind a knob and
driven through the same scroll workload on the Android emulator (Release, no AOT, landscape,
100 appointments a month):

| rendering | per rebind | per paint | jank | slow draw commands |
|---|---|---|---|---|
| `Border` + stack + 2 `Label`s | 0.835ms | n/a — the platform draws it | 34.0% | 185 |
| one `GraphicsView`, `IDrawable` | 0.181ms | **0.746ms** (n=120) | 32.9% | 174–185 |
| one `SKCanvasView` | 0.183ms | **0.917ms** (n=120) | 40.2% | 203–209 |

Two things come out of it, and only the first was expected.

**A rebind costs what the view subtree costs, not what the drawing costs.** The two self-drawing rows
rebind identically — 0.181 against 0.183ms — and both are ~4.6× cheaper than the view subtree. What
is saved is `BindingContext` propagating to four elements instead of one, and the measure
invalidation that setting `Label.Text` brings with it. The drawing technology has nothing to do with
it, which is why swapping it changes nothing here.

**SkiaSharp draws this row about a quarter slower, not faster.** 0.917 against 0.746ms per paint, both
converged over 120 samples and both trending flat. The measurement understates Skia further: it times
the managed callback only, and `SKCanvasView` on Android rasterizes into a bitmap and blits it around
that callback, while `Microsoft.Maui.Graphics` hands `Draw` the hardware-accelerated Android canvas
directly. The frame counters lean the same way across every run — more janky frames and more slow
draw commands — though each is inside the ±8-point run-to-run spread on its own.

So there is nothing to win by reaching for Skia for content this size. It is a raster surface per
view, and the agenda holds thirty of them; the platform canvas is already the faster path. Skia earns
its place where a drawing needs what `Microsoft.Maui.Graphics` cannot express — path effects, shaders,
blend modes — not where a rounded rectangle and two lines of text do.

**The paint is worth more attention than the rebind.** At 0.746ms a paint against 0.181ms a rebind,
four rows entering the window cost about 0.7ms of binding and 3ms of drawing. That is where an agenda
frame goes, and it is the host's to spend.
