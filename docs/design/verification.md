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
settles where the host re-emitted it. A locked appointment is refused, so the cancel path is exercised
as well. This is where `SetScrollingEnabled` takes its `RequestDisallowInterceptTouchEvent` branch.

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
