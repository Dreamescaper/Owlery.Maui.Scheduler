# SchedulerView — design decisions

A Google-Calendar-style day, three-day and week view for .NET MAUI, built from plain MAUI primitives.

This document records *why* the control is put together the way it is. Each section states the
decision, what it was chosen over, and what it costs. For the behaviour itself, see the
[functional requirements](requirements/README.md); for the properties and events that expose it, see
[API.md](API.md).

---

## 1. A separate project, with no Blazor in it

`Owlery.Maui.Scheduler` is a MAUI class library. It references `Microsoft.Maui.Controls` and nothing
else — no BlazorBindings, no app types, no `Owlery.*` contracts.

**Why.** Keeping the control free of the app's rendering stack makes it reusable and keeps its public
surface ordinary MAUI: bindable properties, CLR events, and a `DataTemplate` for the appointment box.

The Blazor app consumes it through `BlazorBindings.Maui.ComponentGenerator`. The wrapper is declared in
`Owlery.Mobile/Properties/Elements.cs` and generated into
`Owlery.Mobile/Elements/Owlery.Scheduler/SchedulerView.generated.cs`:

```csharp
[assembly: GenerateComponent(typeof(SchedulerView),
    MakeItemsGeneric = false,
    PropertyChangedEvents = [nameof(SchedulerView.DisplayDate), nameof(SchedulerView.SelectedSlot)],
    GenericProperties = [$"{nameof(SchedulerView.AppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment"])]
```

Two options are doing real work there:

- `GenericProperties` with a constraint type turns `AppointmentTemplate` into
  `RenderFragment<ISchedulerAppointment>`, so the Razor template gets a typed `context` instead of an
  untyped fragment. `CellSelectionTemplate` is deliberately left alone and becomes a plain
  `RenderFragment`, because one component can carry only one such context and the appointment is the
  one that matters.
- `MakeItemsGeneric = false` opts out of the generator's automatic handling of `ItemsSource`. That
  heuristic exists for `object`/`IList` collections; here `ItemsSource` is already
  `IEnumerable<ISchedulerAppointment>`, so without the opt-out the component becomes
  `SchedulerView<T>` with a type parameter nothing uses.

`PropertyChangedEvents` generates `DisplayDateChanged` and `SelectedSlotChanged` from
`INotifyPropertyChanged`, which is what makes `@bind-DisplayDate` work — the control has no dedicated
changed events.

**A `RenderFragment` template is safe to pool**, which is not obvious. BlazorBindings' bridge
(`DataTemplateItemComponent`) creates one `ContentView` root per `CreateContent()` call and then
subscribes to its `BindingContextChanged`, calling `StateHasChanged` when the bound item changes. So
reassigning `BindingContext` on a pooled view re-renders that view's fragment through Blazor's normal
diff rather than rebuilding it. Rebinding and Blazor's rendering model agree here; they do not have to
be traded off against each other.

The practical consequence is that the app reuses its existing
`Pages/Schedule/Templates/WeekViewAppointment.razor` unchanged — the same appointment box the
Syncfusion schedule renders.

---

## 2. Infinite paging as a three-slot ring buffer

The horizontal pager is a `ScrollView` containing one surface three viewport-widths wide:

```
Horizontal ScrollView
┌──────────────┬──────────────┬──────────────┐
│    slot 0    │    slot 1    │    slot 2    │
│   previous   │   current    │     next     │
└──────────────┴──────────────┴──────────────┘
       0               W              2W
```

The scroll position always returns to `W`. After the user lands on slot 2, the weeks rotate one step
and the scroll jumps back to `W` without animation. Because the page now at `W` shows exactly the week
that was just at `2W`, the jump is invisible.

**Chosen over `CarouselView`.** Rejected up front: it owns recycling and scroll state, which is
precisely what needs to be controlled here.

**Chosen over a three-item horizontal `CollectionView`** with `SnapPointsType="MandatorySingle"`. That
would provide snapping for free, but its virtualization buys nothing for exactly three heavyweight
surfaces, and it would take back control of recycling. The trade made here is explicit: give up free
snapping (section 3) to keep recycling.

**Three slots, not five.** Five gives a bigger safety buffer during an aggressive fling, at the cost of
~70% more live appointment views. Three is sufficient because paging is one week per gesture: by the
time `next` becomes `current`, everything that can be revealed is already rendered. Data is prefetched
wider than the rendered pages (section 9), so recycling a page never has to wait on a network call.

---

## 3. Detecting the end of a swipe

The event that should drive paging is **finger release**, not "scrolling stopped". Those are far
apart: a flick keeps decelerating for anything up to a second or more after the touch ends. Deciding
the week only once motion has died means the user sees the content coast to a halt and *then* slide
again to settle — two animations, with the one that actually chooses the week arriving last.

MAUI's `ScrollView` exposes neither event, and has no paging or snap support. The control used to
approximate one with a 90 ms timer restarted on every `Scrolled` event: when the offset went quiet,
the page was inferred from it with `round(scrollX / W)`. On iOS that was tolerable because
`UIScrollView.PagingEnabled` had already committed to a page on release, so the timer only answered
"has it arrived yet". On Android nothing had committed to anything, so the timer waited out the
inertia and a flick was followed by a visible pause — the conformance gap NAV-7 recorded.

**The pager now decides, and says so.** `PagingScrollView` raises `PageSettled` with the page it has
come to rest on, because only the platform knows the release velocity and where a fling would land.
Section 19 covers what that took on each platform. What is left here is the response, and it is
short:

| Landing page | Action |
|---|---|
| 1 | nothing — already centred |
| 2 | rotate forward, recentre on `W` |
| 0 | rotate backward, recentre on `W` |

`OnPageSettled` is **synchronous**, and that is the point. Rotating moves the appointment views onto
their new pages while the scroll offset still points at the old one, so anything drawn between the
two shows a page the user never swiped to — a flash of the neighbouring week, which is exactly what
Android was reporting. A method that never yields cannot have a frame composited part-way through it,
and `PagingScrollView.ScrollTo` is deliberately not awaitable for the same reason.

One flag survives: `recentring` suppresses handling around the rotation and the jump, so the recentre
cannot be mistaken for a new gesture. `snapping` is gone with the timer that needed it. Neither flag
ever gated the day-header sync — that is deliberate and was learned the hard way, see section 8.

---

## 4. Rotating positions, not views — and refreshing exactly one week

Three approaches were considered:

| Approach | Rearranges views? | Pages refreshed per swipe | Used |
|---|---|---|---|
| Move week views between positions | yes | 1 | no |
| Fixed slots, swap child views between them | yes | 1 | no |
| Three fixed pages, change what each represents | no | **3** | no |
| Three fixed pages, rotate their *positions* | no | **1** | **yes** |

The third option keeps the visual tree perfectly stable but has to rebind all three weeks after every
swipe. For pages holding tens of complex appointment views that is the wrong optimization.

What the control does instead: `WeekSlot` objects hold a week plus its views, and the array of slots is
rotated. Two of the three slots survive untouched and only *move*; one is rebuilt.

```
before:   [ week-1 ][ week 0 ][ week+1 ]
                                   ^ visible
after:    [ week 0 ][ week+1 ][ week+2 ]
                        ^ visible
          └── moved ──┘   └─ rebuilt ─┘
```

Moving is deliberately cheap: an appointment view's `AbsoluteLayout` bounds hold its position *within*
its week, and the slot offset lives in `TranslationX`. Shifting a whole week is therefore a translation
update per view and triggers no layout pass. Rebuilding is confined to `PopulateSlot`, which touches
only the recycled slot.

---

## 5. One shared appointment layer with a shared pool

Appointment views are **not** children of the week pages. All of them — for all three weeks, plus the
spares — are siblings in a single `AbsoluteLayout`:

```
surface (AbsoluteLayout, 3 × viewport wide)
├── GraphicsView            ← the whole grid background
├── selection affordance    ← one reusable view
├── AppointmentView × n     ← the three weeks' appointments
├── AppointmentView (hidden) ← spares
└── drag time label
```

**Why one parent.** A per-page pool means one page can hoard 15 idle views while another allocates 10
new ones. A scheduler-level pool with a scheduler-level parent means renting a spare for a different
week involves no reparenting at all — the parent is already correct. An appointment's x-coordinate
simply encodes which slot it belongs to.

## 6. Spares stay in the visual tree, hidden with `IsVisible = false`

Appointment views can be expensive: icons, images, nested layouts. Keeping them *instantiated* is
valuable; keeping them *rendered* is not.

`IsVisible = false` maps to `Collapsed` in MAUI: the element stays attached with its native view and
gesture recognizers intact, but is skipped during measure, arrange and rendering, and receives no
input. That is exactly the wanted trade.

Rejected alternatives:

- `Opacity = 0` — still measured, arranged, and potentially hit-tested. Strictly worse.
- `TranslationX = -10000` — fights the layout system for no benefit.
- Removing from the tree on every recycle — throws away the expensive part.

**Without a cap, deliberately.** The instinct is to bound the pool so one unusually busy week cannot
make the control retain those views forever. That would be wrong here. Every `CreateContent()` call
permanently registers a template root inside BlazorBindings' `DataTemplateItemsComponent`, and that
registration is not released when the view is detached from the layout. Discarding a spare would
therefore free nothing on the Blazor side *and* force a second registration the next time the view
count rises. Retaining every view is strictly cheaper, so `AppointmentViewPool` keeps them all.

The pool consequently settles at the high-water mark of appointments visible across three weeks at
once — tens of views for this workload, all idle and collapsed.

Gesture recognizers are attached **once**, at view creation, and read whatever appointment is currently
bound. Pooling therefore never adds or removes event handlers.

### Reconciling, not rebuilding

Pooling only pays off if the pool is left alone when nothing has changed. `PopulateSlot` originally
released every view for a week and rented them straight back. That is wrong twice over: each view is
hidden and re-shown, and because the pool is a stack the views return in reverse order, so every one
ends up bound to a *different* appointment than it had. A refresh returning byte-identical data still
repainted every appointment on screen — visible as a flash across the whole week.

It now reconciles by identity instead. Each week indexes the views it already has by the `Key` of the
appointment they are showing, and every appointment in the new layout reclaims its own view. An
unchanged reload rebinds each view to an equivalent item in the same place — identical output, nothing
visibly happens — and only a genuine surplus or shortfall touches the pool. Bounds are written only
when they actually differ, since an unchanged write still costs a layout pass.

An earlier version matched positionally, relying on the layout engine's deterministic order. That is
correct for an unchanged reload but degrades quietly: two appointments tying on start and duration can
swap views, and inserting one early shunts every later appointment onto a different view, repainting
the rest of the week for a one-item change. Keys make reuse mean what it says.

This matters more than it looks, because the host re-emits `ItemsSource` freely — cached results
followed by fresh ones, one assignment per loaded month — so a single refresh can run this path
several times.

Two guards keep the pool honest, both of them cases where a view ends up owned by no page and
therefore never handed back — left visible, drifting over whatever scrolls past:

- **Duplicate keys within one page.** Indexing views by key means a second view claiming a key
  displaces the first, which then reaches neither the new layout nor the surplus sweep. Section 9
  tells hosts not to do this, but the punishment should not be a permanent ghost, so the displaced
  view is returned there and then.
- **Changing the template while a drag is in flight.** A dragged view has been taken out of every
  page, so none of the template handler's cleanup reaches it: not in a slot, not among the spares,
  and `RepopulateAllSlots` declines to run while a drag is armed. Left alone it survives the change
  and is later handed into a pool that no longer matches it, to be rented out as though it did. The
  drag is therefore cancelled first, which is what the view-mode switch had always done.

---

## 7. The grid is drawn, not built from cell views

A week at 15-minute resolution is 7 × 96 = 672 cells; three rendered weeks would be over 2,000 views
whose only job is to draw a line. The entire background — day-column shading, hour lines, half-hour
lines, day separators, the current-time indicator — is instead drawn on a single `GraphicsView`
(`SchedulerGridDrawable`).

**One `GraphicsView` for all three slots**, not one per week. The grid is nearly identical between
weeks; the drawable simply draws 21 day columns. Week-specific details (weekend shading, today's
highlight, the current-time line) are handled by redrawing on `Invalidate()` when the weeks rotate,
which is one draw call.

The **hour gutter is not drawn**, though it once was. It is a dozen or so `Label`s, positioned from
the same `YFromMinutes` the grid lines use so they cannot drift apart. The reasoning above does not
reach it: fourteen views are not two thousand, and drawing them cost real things — text that would
not scale with the system font size (the ACC-9 gap), and an obscured-label calculation to keep the
drag-time chip readable. The chip is opaque and the same height as a label, so it simply covers one.

Tapping empty space does not need cell views either — and in fact **all** input for the surface is
handled on the `GraphicsView`, including taps on appointments. Appointment views are
`InputTransparent`, and `HitTestAppointment` resolves a touch point against their known bounds; empty
space falls through to the surface's `SlotAt`, which converts the point to a slot, day and snapped
time arithmetically. Section 11 explains why input is centralised here rather than left to per-appointment
gesture recognizers.

`SchedulerGeometry` is a single mutable object shared by the control, the drawable and the gutter
labels, so the drawn background and the positioned views can never disagree about where an hour line
sits.

---

## 8. Vertical scrolling lives outside the pager

```
Vertical ScrollView
└── time gutter │ Horizontal pager
                └── slot 0 │ slot 1 │ slot 2
```

Because one vertical scroll wraps all three weeks, **the vertical position does not change when the
week changes**. Looking at 14:00 and swiping to next week leaves the user at 14:00. Giving each page
its own vertical scroll would have required synchronising three scroll positions and would still drift.

The left time gutter is a sibling inside that same vertical scroll, so hour labels stay aligned with
the grid for free.

The day-header strip is the awkward one. It must not scroll vertically, so it cannot live inside the
vertical scroll — which means it also cannot live inside the pager, and therefore does not move when
the pager scrolls. It is the **only element in the control whose position is mirrored by hand**, via
`headerSurface.TranslationX = -e.ScrollX` on every `Scrolled` event. Everything else moves because the
platform scrolls it.

That makes the header the single point where a missed or unexpected scroll event becomes visible, and
two rules follow from it:

1. **The mirror runs unconditionally**, including while `recentring` is set. An earlier version
   returned early from the whole handler when recentring, which meant a programmatic scroll that did
   not settle exactly as expected left the strip parked at a stale offset — far enough off-screen to
   read as a missing header — until some later rebuild happened to reset it. Position mirroring and
   gesture detection are separate concerns and are now gated separately.
2. **The strip is `LayoutOptions.Start`**, not the default `Fill`. It is three viewports wide inside a
   one-viewport cell; with `Fill` the parent grid arranges it to the cell width rather than its
   requested width, which puts the second and third weeks' headers outside the arranged box.

---

## 9. Data contract

The control does not fetch anything. It exposes:

- `ItemsSource` — a flat collection of `ISchedulerAppointment`, covering as many weeks as the host
  likes. The control picks out what belongs to each rendered week. `INotifyCollectionChanged` is
  honoured; assigning a new collection instance also works, and is what the page does.
- `VisibleDatesChanged` — raised whenever the centre week changes, carrying the seven visible dates
  plus the full three-week prefetch range.

This is what keeps recycling free of network latency: the host is told about a range wider than the
visible week, so a rotation binds data that is already in memory.

**Time zones are the host's problem, deliberately.** Every `DateTime` crossing the API is wall-clock in
`TimeZone`. The control never converts. The mobile adapter (`SessionAppointment`) converts the UTC
timestamps once, in one place. This is a direct reaction to the timezone workarounds the Syncfusion
implementation needed.

---

## 10. Overlapping appointments

`AppointmentLayoutEngine` groups a day's appointments into clusters of transitively overlapping items,
then greedily assigns each item the first column free at its start time. Width is `1 / columnCount` of
the day, so a cluster of three renders as three equal columns.

Positions are produced as **fractions** of a day column rather than pixels, so a viewport resize
re-positions views without recomputing overlaps.

Appointments shorter than 15 minutes are still given a 15-minute box so they stay tappable, and an
appointment running past midnight is clipped to the visible window of its own day.

---

## 11. Drag-and-drop

Nested scroll views and drag gestures compete for the same touches, so dragging is explicitly *armed*
by a long press. The hard part is detecting the press.

**`PanGestureRecognizer` cannot do it.** It only reports `Started` once the platform has already
recognised a pan — that is, *after* the finger has moved. A first implementation started the
long-press timer there and cancelled it if the finger travelled more than 12 dp. Those two rules race,
and movement always wins, because by the time `Started` arrives the finger is already moving. Traced
on a device, the candidate was cancelled at ~150 ms while the timer needed 350 ms; the drag could
never arm, for anyone. `PointerGestureRecognizer` is not an alternative either: pointer gestures are
supported on Android, iPadOS, Mac Catalyst and Windows — not iPhone.

The signal that does exist everywhere is `GraphicsView`'s raw touch lifecycle. `StartInteraction`
fires on touch-*down*, before any movement. So the grid drawable owns all input:

1. `StartInteraction` hit-tests the point against the appointment bounds and starts a 350 ms timer.
   The timer now starts when the finger lands, so holding still is what wins.
2. `CancelInteraction` — raised when an ancestor scroll view claims the gesture — cancels the
   candidate. This is the platform telling us "that was a scroll", which is far better than inferring
   it from a distance threshold. Movement beyond 12 dp cancels too, as a secondary guard.
3. The timer firing raises `AppointmentDragStarting` (cancellable). If it is not cancelled, scrolling
   is frozen for the duration.
4. `DragInteraction` applies movement via `TranslationX/Y` only, snapped to `SnapMinutes`, keeping the
   grabbed point under the finger. No layout pass runs during the drag.
5. The time the appointment would take is drawn **in the hour gutter**, level with the line it would
   start on. It began as a label floating just above the appointment, which put it under the hand
   doing the dragging — legible in a screenshot, invisible in use. The gutter is the one column
   guaranteed to be clear of the finger. It is a small opaque chip that simply covers whichever hour
   label it lands on; being the same height as one, hiding what is underneath made no visible
   difference and only added arithmetic to get wrong.
6. `EndInteraction` raises `AppointmentDropped`, or — if the drag never armed and the finger did not
   travel — is what raises the tap events instead.

**Outside `Handlers/`, this is the only place the control reaches past MAUI** — in
`SetScrollingEnabled` and `ConfigurePlatformScrolling`, and only for the timeline's *vertical* scroll.
The pager answers for itself now (section 19), so the settings that used to be listed here for it —
paging, bouncing, the touch delay — belong to `PagingScrollView` and are described there.

- Freezing scroll cannot use `IsEnabled = false`: that disables interaction for the whole subtree and
  cancels the very touch driving the drag. Nor can `ScrollOrientation.Neither`, tempting as it looks:
  it maps to `ScrollEnabled` on iOS, but on Android only `OnTouchEvent` is guarded and
  `OnInterceptTouchEvent` is not — so the ancestor still steals the gesture and then declines to
  scroll with it, cancelling the drag and doing nothing in its place. iOS uses
  `UIScrollView.ScrollEnabled`; Android uses `RequestDisallowInterceptTouchEvent`.
- `UIScrollView.DelaysContentTouches` is turned off, because it otherwise withholds touch-down while
  deciding whether the touch is a scroll — exactly the signal the long press depends on.
- `UIScrollView.Bounces` is turned off. It exposes blank space beyond the first and last hour, which
  reads as the calendar coming apart rather than as an affordance.

### Crossing a week boundary

Moving an appointment into another week means the weeks have to rotate while the drag is still in
progress, which the ring buffer (section 4) and the reconciliation (section 6) would otherwise fight:
rotation recycles a slot, and reconciliation would return the dragged view to the pool underneath the
finger.

Instead the appointment **leaves the weeks entirely for the duration of the drag**. On pick-up it is
excluded from what the slots lay out, and becomes two views that belong to no week:

- a **ghost** — the original view, detached where it stood and faded to half opacity, so the slot
  being vacated stays visible. It stays on the scrolling surface, because it belongs to the week it
  came from: it is shifted by one viewport whenever the weeks rotate, travelling with that week and
  sliding off screen behind the drag.
- a **follower**, on an overlay above the whole control, riding the finger.

Because neither is in a slot, rotation and reconciliation cannot touch them, and a mid-drag data load
is harmless. The drop target is always read from the centre slot, whichever week has been rotated into
it, so the same code serves same-week and cross-week drags.

Paging itself is a dwell, not a contact: the edges are exactly where someone drags to reach Monday and
Sunday, so flipping on contact would make those two columns unreachable. The dwell timer repeats, so
holding walks through consecutive weeks.

**The change of week has to be visible.** Rotating the ring buffer swaps the weeks without moving
anything, so on its own the calendar simply changes contents — easy to miss, and hard to read as
"I have moved to another week". So the rotation is followed by the same two-step the snap uses, in
reverse: jump instantly to wherever the *outgoing* week has landed (slot 0 going forward, slot 2 going
back), which is pixel-identical to the frame before, then animate across to the centre. The result is
the same motion a swipe produces.

That slide moves the surface — which is why the follower is not on it.

The first attempt kept the dragged appointment among the other views and pushed it back by each scroll
delta in `OnPagerScrolled`. That is correct on paper and unpleasant in the hand: the correction can
only be applied *after* a scroll event, so every frame of the slide moves the appointment away from
the finger and then puts it back, and any event the platform coalesces is a frame where it simply
runs. Chasing a moving surface from inside it cannot be made smooth.

The follower therefore lives on a transparent, input-transparent overlay spanning the whole control,
outside both scroll views, and is positioned in the control's own coordinates:

```
overlayX = TimeGutterWidth + slotOffset + snappedX - pagerScroll.ScrollX
overlayY = HeaderHeight    + snappedY            - verticalScroll.ScrollY
```

Nothing scrolls out there, so there is nothing to correct — the view stays where it was last put, and
a slide that raises no touch events simply doesn't move it. Subtracting the live scroll offsets also
makes the conversion self-correcting: a given finger position resolves to the same place on screen
wherever the pager happens to be mid-slide, so a finger that *does* move during the animation is still
tracked correctly.

One thing the overlay does not solve is *when* the drop **target** may be resolved. Resolving a column
assumes the pager is at rest on the centre slot, so doing it while the slide is in flight picks a
column out of a week that is only half on screen, and the appointment jumps a whole page sideways. The
target is therefore held for the duration of the slide and recomputed from the last touch point once
the pager settles. The recompute has to happen *after* the in-flight flag is cleared, or the guard
swallows the very update that applies the new week.

The **follower** is not held with it, though it was to begin with. It has no such dependency — the
finger's position on screen is the touch point less the live scroll offsets, which holds whatever the
pager is doing — and freezing it meant the appointment sat still for the length of the slide while the
calendar moved underneath, which looks like the drag has been dropped. So the two are separated: the
view keeps tracking the finger throughout, and only the target and its gutter label wait.

Because only one appointment can be dragged at a time, the overlay keeps a single view built from
`AppointmentTemplate` and rebinds it, rather than renting from the pool — the pool's views all belong
to the surface, and nothing is ever reparented.

On an accepted drop the ghost takes over: it moves to the dropped position and returns to full
strength as the follower is hidden. That keeps the result on the scrolling surface, where it scrolls
with the calendar as it should, without anything having to change parents.

`OnPagerScrolled` still stops scheduling snaps while dragging, since the pager is being driven by the
drag rather than by the user.

Edge paging makes this the sharpest test of the identity rule in section 6. Paging raises
`VisibleDatesChanged`, a host loads the periods it is moving through, and the collection is rebuilt
*while the gesture is still running* — so the appointment being dragged is a different object by the
time it is dropped. Because the exclusion filter and the event payloads both go through `Key`, none of
that is visible: the drag keeps excluding the right appointment, and the drop reports whichever
instance the host is showing at that moment rather than the orphan it was picked up as.

That last point is the one worth stating plainly, because it was originally left to the host and cost a
bug: a handler that captures the dragged item and mutates it would be mutating something nothing
renders, and the appointment would appear to vanish. `Resolve` exists so the natural handler is the
correct one.

A collection change is still not *applied* until the drag ends — re-laying-out the calendar under a
moving finger is churn for data the user cannot see yet — but that is now a comfort measure rather than
load-bearing; every way a drag can finish ends in another repopulate.

A dropped appointment is held at the position it was released until the host feeds the change back,
which makes the host's re-emit load-bearing. That obligation is reasonable — telling a data-bound
control that the data changed is the deal — but forgetting it used to produce something strange rather
than something dull: the held view stayed pinned to the surface and drifted over whatever week was
scrolled to next, a phantom appointment following the user around. Changing period is now treated as
giving up on the wait, so the worst a forgetful host gets is the appointment snapping back to what the
model says.

The appointment rejoins a week when `ItemsSource` is next re-emitted — which is why that already-
documented host obligation matters more here than it looks. Until then the lifted view stays exactly
where it was dropped.

**On a successful drop the view is deliberately left where it was dropped.** The host's update is
asynchronous; snapping back to the old time only to jump forward a moment later would read as a glitch.
The host re-emits its collection when the call finishes, which repositions the appointment from the
model — and that is also what moves it back if the call failed.

**API caveat worth knowing.** `AppointmentDropped.Cancel` is read immediately after the event returns,
so an `async` handler must do its validation *before* its first `await`. Checks that must veto a drag
(external calendars, offline) therefore belong in `AppointmentDragStarting`, which is synchronous.

### Reaching off-screen hours

Edge paging solves reaching another period; the same problem exists vertically, because the day is
taller than the viewport. Holding a dragged appointment against the top or bottom scrolls the
timeline, with two deliberate differences from paging: it starts immediately rather than after a
dwell — dragging towards an off-screen hour is unambiguous, whereas the first and last columns are
somewhere a person legitimately wants to drop — and it moves continuously rather than a page at a
time.

The subtlety is which coordinates the touch arrives in. Points come from the drawing surface, which
lives *inside* the timeline's scroll view, so they are in content coordinates: a finger held still
over a scrolling grid is over a different time each frame, yet raises no new touch events. Each scroll
step therefore advances the remembered point by exactly what it scrolled and re-resolves the target,
which is what keeps the appointment under the finger while the hours move beneath it.

`ViewportHeight` is taken from what the control itself was allocated rather than read back off the
scroll view, which reports `-1` until the platform has measured it — so relying on the scroll view
would have meant the feature silently never triggered, and could not be tested at all.

**Known risk.** The Android branch of `SetScrollingEnabled` is untested — see section 15.

---

## 12. Cell selection

Tapping empty space sets `SelectedSlot` and raises `CellTapped`; the control renders one reusable
overlay view at that position. The **arm-then-confirm** behaviour (first tap selects, second tap on the
same cell opens the create flow) lives in the host page, not the control — it is product behaviour, and
keeping it out means the control does not have to guess what a second tap should do.

---

## 13. Accessibility

Everything drawn on the `GraphicsView` is pixels; screen readers see one element. This is a genuine
cost of section 7, and it is accepted deliberately: exposing 672 empty 15-minute cells as individually
focusable elements would make the calendar *worse* for screen-reader users, not better.

The accessible surface is therefore the meaningful elements only:

| Element | Implementation | Exposed |
|---|---|---|
| Grid lines, shading, hour labels | drawn | no |
| Day headers | real `Label`s | yes |
| Appointments | real views with `SemanticProperties.Description` | yes |
| Selected time slot | one real overlay view, described | yes |
| Add action | host toolbar | yes |

---

## 14. Not implemented

The following are absent by design, not by oversight:

- **Agenda view.** It needs a different surface again, and the app keeps its Syncfusion-based
  `SchedulePage` for it. Month view *is* now supported — see sections 17 and 18 — as are day and
  three-day views, see section 16.
- **All-day / multi-day appointments.** There is no all-day row; an appointment is clipped to its
  starting day.
- **Resizing an appointment by dragging its edges.**
- **Dark theme.** Colours are exposed on the drawables and `GridBackgroundColor` on the control, but no
  `AppThemeBinding` wiring is provided.
- **Releasing template roots.** Bounded by BlazorBindings, not by this control — see section 6.

## 16. Showing fewer than seven days

`VisibleDays` sets how many days a page holds — 7, 5, 3 or 1 — and everything downstream is derived
rather than special-cased. `SchedulerGeometry.DayWidth` becomes `ViewportWidth / VisibleDays`, the
layout engine buckets by day count instead of by seven, and a page advances by `VisibleDays` days
rather than by a week. There is no separate day view: it is the same surface with one column.

Two things genuinely differ rather than scaling:

- **Where a page starts.** A week has to begin on `FirstDayOfWeek`, or it is not a week. A shorter page
  has no such obligation and starts on `DisplayDate`, which is what puts today in the leading column of
  a day or three-day view and makes "Today" mean what it should.
- **What the neighbours contain.** With seven-day pages a week's worth of data lands entirely on the
  centre page; with three-day pages the same data spills onto the pages either side. Nothing in the
  control cares, but it surprises tests written for a week.

### Animating the change

Changing the day count grows or shrinks the columns into place rather than cutting between two
layouts. The page is laid out for the *new* count first, then `DayWidthOverride` is animated from the
old column width to the new one. Because the grid drawable, the appointment positions and the day
headers all measure through `DayWidth`, they move together without any of them knowing an animation is
happening.

It does not fall out for free that the page should stay put while it resizes. A new day count usually
moves where the page *starts* — three days from Wednesday becomes a week from Monday — so laying out
for the new count and then animating the width alone slides the whole page sideways before it begins
to resize, and every appointment visibly re-places itself. Wednesday, which the user was looking at,
ends up two columns in.

So the page carries an `AnimationOffsetX` that starts at exactly the shift the new page start
introduced and eases to zero. Wednesday begins where it already was and the days around it grow in
from the side they belong on: Monday and Tuesday from the left, Saturday and Sunday from the right.
The offset applies to the appointment views, the day headers and the drawable together, so nothing
has to know about it individually.

The other thing that does not fall out for free is the pages either side. They are laid out one page
apart — and mid-animation a page is not a viewport wide, so spacing them by the viewport would have
them overlap the centre page or leave a gap beside it. `SchedulerGeometry.PageSpan` is therefore
`VisibleDays × DayWidth` rather than `ViewportWidth`: identical at rest, and correct throughout the
animation, so the neighbours stay exactly one page away and off screen without anything being hidden.

An earlier version did hide them, by toggling `IsVisible` on their views. That was a mistake worth
recording: `IsVisible` is *also* how the pool marks a view as spare, so the two uses collided. A view
recycled while the animation was running could be shown by `Rent` despite belonging to a page that was
meant to be hidden, and appointments appeared for the duration of the transition that were nowhere to
be seen afterwards. Spacing the pages correctly removes the need for the mechanism altogether.

Appointment views are re-placed from the positions each page already stores, so a frame costs a bounds
write per view and one canvas invalidation — no re-layout of the data. The selected-cell affordance is
re-placed on the same pass: it is anchored to a column like everything else, so it has to widen and
travel with the day it marks rather than sit still while the grid moves under it.

The animation is skipped when the control has no handler, which also keeps it out of the headless
tests. That guard is load-bearing rather than tidy: MAUI's animation ticker does not run without a
platform, and `Commit` applies its first frame immediately — so a transition that starts and never
advances pins the column width at the *old* value, leaving the new day count laid out at the old size.
Guarding on `IsLoaded` alone was not enough, because the test host does mark the control loaded.

## 17. The page surface seam

Month view needs a different surface, not a different day count (section 14). What that means in
practice is that roughly half this control does not care which it is drawing.

`ISchedulerSurface` is where the two part company. A surface answers what a page holds and how it is
measured: where a page starts, how to step to the next and previous one, which dates it shows, how to
turn appointments into placements, and where a placement or a selected cell sits. `TimelineSurface`
is the day/three-day/week implementation and simply reads the geometry it is given.

Everything on the other side of the seam is untouched by a change of mode: the three-slot ring buffer
and its snapping, the view pool, the reconciliation in `PopulateSlot`, identity by `Key`, and every
public event. Those were the expensive parts to get right and none of them had to be duplicated.

Two things are deliberately *not* on the interface:

- **Dragging.** It belongs to the timeline alone, and putting it here would have reproduced exactly
  the interface this document already warns against in `AGENTS.md` — a dozen members relocating the
  coupling rather than reducing it. Every member that survived is arithmetic, which is what makes
  both implementations testable without the MAUI test host.
- **Building the visual tree.** A month has no time gutter and does not scroll vertically, so the two
  modes differ in structure rather than in a value. Structure is settled once, when the mode changes,
  rather than on every page.

`SchedulerGeometry` was left as the timeline's geometry rather than being split up front. The values
both modes genuinely share — viewport size, `SlotStarts`, `PageSpan` — are visible in
`CellSelectionOverlay`, which now reaches for geometry only for those and goes through the surface for
everything else. Lifting them is worth doing when there is a second implementation to check the guess
against, not before.

One generalisation was taken while passing. `VisibleDatesChanged` derives its prefetch bounds from
`DatesOn` rather than from the page starts, because a page need not begin on the first date it shows:
a month grid opens on the tail of the previous month, and the host has to be told to fetch that far
back. For a timeline the two are the same value.

## 18. The month surface

### Six rows, always

A month needs five or six week rows depending on where it starts. The grid is fixed at six anyway,
because a varying count would make `ContentHeight` depend on which page you are looking at — and all
three slots share one surface and one canvas, so the neighbours either side would be drawn at the
centre page's row height. Fixing it keeps every page identical in size, which is the same property
that makes the ring buffer work at all.

The cost is real and shows up twice a decade: a 28-day February beginning on `FirstDayOfWeek` fills
exactly four rows, so the last two are entirely March. Apple's calendar makes the same trade.

A page is identified by the **first of its month**, not by the first cell of its grid. That keeps
`NextPage` a plain `AddMonths(1)` and makes "is this day from another month" a comparison against the
page's own identity rather than against a computed range. `GridStart` derives the top-left cell by
walking back to `FirstDayOfWeek`.

Because of that walk, adjacent pages overlap: a page's greyed trailing days are the next page's real
leading days. So the same appointment can be bound to a view on two slots at once, which never
happened with weeks — they were always disjoint. Nothing breaks, since `appointmentsByView` is keyed
by view rather than by appointment, but it is worth knowing before reading a test that counts views.

### Overflow, and why the marker eats a line

A cell fits `LinesPerCell` chips beneath its day number. When a day has more, the last line is given
over to a "+N more" marker, so one *fewer* appointment is shown than would physically fit and `N`
counts the appointment whose line the marker took. Showing five and hiding the rest silently would be
worse: the cell would look complete when it was not.

The marker is drawn on the canvas rather than rented from the pool. It carries no data and needs no
interaction of its own — a touch anywhere in the cell already resolves to that day — so making it a
view would put something in the pool that is not an appointment for no gain.

Dots instead of chips were considered and rejected. The host's appointments are lessons, and the
useful content is a person's name; a dot says only that the day is not empty.

### The chrome, and what a mode switch costs

The two modes are one visual tree, not two. `pagerScroll`, the surface and the pool are shared, and
what changes is settled by `ApplyMonthChrome` / `ApplyTimelineChrome`: the gutter and the header
corner collapse to zero width, the timeline's scrolling day-header strip hides and a static weekday
row takes its place, and `gridView.Drawable` swaps. Reparenting the pager between two containers was
the alternative and it buys nothing — a month's content is exactly the viewport height, so the
vertical `ScrollView` it already sits in simply has nothing to scroll.

The weekday row is built per page, through the same three-slot header strip the timeline uses. The
columns *do* mean the same thing on every month — every page starts on `FirstDayOfWeek` — so a single
fixed row would have been correct and cheaper. It also looked wrong: a heading that stays put while
the pages slide under it reads as though the calendar has come apart from it. Three label rows is a
small price, and it means the header-mirroring of section 8 applies unchanged in both modes.

The cells carry the day numbers, so a month header names its columns and stops there.

Day numbers are painted, not labelled. There are 42 to a page and three pages rendered at all times,
so labels would mean 126 views that do nothing but show a number. Chips stay as views because they
carry the host's template; the "+N more" marker is painted for the same reason the numbers are.

Switching modes empties the pool rather than reusing it. A chip and an appointment box come from
different templates, so a spare built from one is no use to the other — and `MonthAppointmentTemplate`
exists precisely because one template rarely reads well at both sizes: a chip is a single line of
about 16 units, an appointment box is sized by its duration. It falls back to `AppointmentTemplate`
when unset, which renders badly but renders.

The switch is a hard cut today. The cross-fade belongs with the transition work, and the seam it needs
is already there: both surfaces can measure the same appointment.

`DisplayDate` is deliberately *not* rewritten on a mode change. It means "some day on the page" and is
only written back by a swipe, so switching to a month and back leaves the user on the day they were
looking at rather than collapsing them to the first of the month.

### What the month engine does not do

No overlap packing — a month cell has no time axis, so nothing can collide and appointments are
simply listed in the order they start. No visible-hours window either: the timeline clips to
`StartHour`..`EndHour`, but a 06:00 lesson appears in a month regardless. And no multi-day spanning,
matching the timeline (section 14) — an appointment is placed on the day it starts.

Ordering is `OrderBy(Start).ThenByDescending(duration)`, and LINQ's sort is stable, so appointments
starting together keep the order the host supplied. That is load-bearing rather than cosmetic, though
not for the reason it first appears: reuse is by key (section 6), so an unstable sort would not cause
a repaint — it would make two appointments that start together *swap places* in the cell between one
reload and the next, because a chip's line within a day is its index in that order.

## 19. The pager is our own scroll view

`PagingScrollView` replaces MAUI's `ScrollView` on the horizontal axis. It exists because MAUI's
cannot do two things this control needs, and cannot be made to.

**It cannot page.** Paging is a platform setting MAUI does not surface, so a week only ever snapped
on iOS, where `UIScrollView.PagingEnabled` was reached for through `Handler.PlatformView`. Android
had nothing, which is what NAV-7 was about.

**Its offset arrives late.** `ScrollToAsync` goes through the handler and lands on a later pass. That
is the gap a frame was drawn in, between rotating the ring buffer and recentring on it (section 3).

Subclassing was not an option on Android. The platform view there is a
`MauiScrollView : NestedScrollView` that grows an internal `MauiHorizontalScrollView` child when a
horizontal orientation is asked for, so paging would have had to be imposed across two nested view
classes participating in nested scrolling, neither of them ours.

### What each half does

The cross-platform half owns the offset, the request plumbing and `PageSettled`. `ScrollTo` is
deliberately a method and deliberately not awaitable: the caller needs the offset *changed* on
return, not merely requested.

On **iOS** the handler is thin — a `UIScrollView` with `PagingEnabled`, `Bounces = false` and
`DelaysContentTouches = false`, all of which the scheduler used to set on it from the outside.

On **Android** the snap is written by hand, following React Native's `ReactHorizontalScrollView`: a
fling is never allowed to run its course, a throwaway `OverScroller` predicts where it *would* have
landed, and that point is rounded to the nearest page. Rounding the predicted landing rather than the
offset at release is what makes a short flick move a page while a slow drag does not.

The snap is animated by a scroller this control owns, **not** `SmoothScrollTo`. That drives the
scroller inside `HorizontalScrollView`, which cannot be reached to stop — so the recentre that
follows a settle was overwritten on the scroller's next frame, and one swipe compounded into
several. Owning it also makes "has it arrived" exact instead of polled for.

### Five things that were only found by running it

**Plain properties do not reach a handler.** `PageWidth` and `IsScrollEnabled` are not bindable —
nothing binds to them — so the mapper ran once at connect time, while `PageWidth` was still 0, and
never again. With no page width, every fling snapped to page 0 and swiping went *backwards*
regardless of direction. Both setters now call `Handler.UpdateValue`.

**An offset set before the first layout is lost.** UIKit accepts it — reading it straight back
reports exactly what was asked for — then resets it to zero once the view has a frame, so the
calendar opened one page early. Detecting that by reading the offset back therefore cannot work; the
absence of a frame is what identifies it. Android loses it the same way against a scroll range that
is still zero. Both handlers hold the request and re-apply it on layout.

**State reset on `ACTION_DOWN` is never reset.** A `ViewGroup` does not intercept the down event —
the drawing surface underneath consumes it, which is how long-press-to-drag gets its touch-down — and
interception only begins once a drag is recognised. So a flag cleared on DOWN inside the scroll view's
`OnTouchEvent` is cleared exactly never. The flag saying "this release already flung" therefore stayed
set after the first fling, and every slow release stopped snapping: the pager just stayed wherever the
finger left it, between two pages. Fast flings kept working throughout, because `Fling` snaps without
consulting it — which is what made the failure look like it depended on velocity. The flag is now
scoped to a single release rather than to a gesture.

**An animated scroll on Android completes nothing by itself.** iOS reports a programmatic animation
finishing through `scrollViewDidEndScrollingAnimation`; Android has no counterpart, so an awaited
`ScrollToAsync(..., animated: true)` never returned. Edge-paging mid-drag clears its "a slide is in
flight" flag only once that await comes back, so the flag stayed set and the drop target was never
resolved again — the appointment followed the finger and refused to snap, for the rest of the drag.
The handler now completes any pending request when its own animator stops.

**Android draws scroll content outside the scroll view.** MAUI leaves `ClipChildren` off on its
Android layout views so shadows can spill, which means the pager is drawn without being clipped to
its own bounds — and the pager's content is three pages wide with an opaque background, so the page
parked left of the viewport painted straight over the hour gutter beside it. Neither asking the
scroll view to clip nor clipping the grid around it puts that back. The gutter is drawn last with an
opaque background instead, which is what fixed chrome over scrolling content should have been doing
anyway.

### Registration

MAUI has no way for a library to register a handler on its own, so a host must call
`builder.UseOwleryScheduler()`. Without it the pager falls back to the handler for its base type and
simply does not scroll. That is the one line this control costs a host, and the reason it is worth
knowing about: everything else here is internal.

## 15. Verification status

`Owlery.Maui.Scheduler.Tests` covers the platform-independent behaviour headlessly on `net10.0`:
overlap layout and clipping, appointment placement, view reuse across a refresh, cell and appointment
taps, long-press-to-drag including refusal and the scroll-not-drag case, and week paging. It runs
without a simulator, and found two bugs that had already shipped.

The month surface of section 18 is covered to the same standard: grid alignment for Monday and Sunday
weeks, the six-row window, per-day stacking and the overflow rule as arithmetic; then, through the
control, chip placement, month paging across a year boundary, the 42-day visible-dates report, whole-
day cell selection, that a held chip is not picked up, and that switching modes leaves nothing of the
old one behind.

Nothing month-shaped has been seen on a device. The colours, the chip and row proportions, and how the
grid reads at a real phone size are all unverified, and the host does not yet offer the mode.

Rendering, paging, week rotation, overlap layout, the current-time line and appointment semantics have
also been checked on the iOS simulator through DevFlow.

Drag-and-drop is confirmed working on the iOS simulator by manual testing. It could not be automated:
DevFlow drives gestures by invoking a MAUI gesture recognizer and reports that "native pan injection
is not available on iOS", and input is deliberately handled through `GraphicsView` touch events rather
than a recognizer. The *failure* of the previous implementation was captured through DevFlow before it
was replaced — that trace is what identified the race described in section 11.

Android has now been exercised, on a `Medium_Phone` emulator through DevFlow: the week and month
surfaces render, the gutter labels correctly, and paging moves exactly one page per fling in both
directions and stays exact over repeated swipes. Three defects were found doing it, all recorded in
section 19; none of them was reachable from the headless suite, and two of them looked like correct
code until the platform disagreed.

Still unverified there: drag-and-drop, which is where `SetScrollingEnabled` takes its
`RequestDisallowInterceptTouchEvent` branch and remains the part most likely to need adjustment, and
whether the neighbouring-page flash is gone — the window it happened in is closed by construction,
but nobody has watched for it.

The animated slide when paging mid-drag is asserted in tests only as a sequence of scroll requests —
the test shim applies them instantly. That it *looks* right, and that a programmatic scroll still runs
while `UIScrollView.ScrollEnabled` is false, both need a device.

Nothing about the platform handlers is covered headlessly: with no handler registered, `PagingScrollView`
holds whatever offset it was last told and raises `PageSettled` when a test says so, which is exactly
what the suite wants and exactly what cannot catch a fling prediction or a clipping quirk.
