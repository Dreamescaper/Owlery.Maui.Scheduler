# SchedulerWeekView — design decisions

A Google-Calendar-style week view for .NET MAUI, built from plain MAUI primitives.

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
`Owlery.Mobile/Elements/Owlery.Scheduler/SchedulerWeekView.generated.cs`:

```csharp
[assembly: GenerateComponent(typeof(SchedulerWeekView),
    MakeItemsGeneric = false,
    PropertyChangedEvents = [nameof(SchedulerWeekView.DisplayDate), nameof(SchedulerWeekView.SelectedSlot)],
    GenericProperties = [$"{nameof(SchedulerWeekView.AppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment"])]
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
  `SchedulerWeekView<T>` with a type parameter nothing uses.

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

MAUI's `ScrollView` exposes neither event. It has no paging or snap support, and `Scrolled` fires for
programmatic scrolling too, so it cannot even be used to tell dragging from deceleration.

**On iOS the platform is asked to do it instead.** `ConfigurePlatformScrolling` sets
`UIScrollView.PagingEnabled`, which commits to a page on release using the release velocity. The
scroll view's frame is exactly one viewport and its content exactly three, so UIKit's page boundaries
already coincide with 0 / W / 2W — no other configuration is needed. This also gives the flick
behaviour a position-based rule cannot: a fast, short flick carries through instead of snapping back.

The 90 ms quiet timer remains, but its job changes. Where the platform pages natively it only answers
"has it arrived yet", so the rotation can happen — and since the scroll is guaranteed to land exactly
on a boundary, `round(scrollX / W)` is exact rather than approximate. Where it does not, the timer is
still what picks the target, with the inertia delay described above.

On settle, the nearest page is computed as `round(scrollX / W)` and:

| Landing page | Action |
|---|---|
| 1 | correct a partial drag back to `W`; nothing else changes |
| 2 | animate to `2W`, then rotate forward and jump to `W` |
| 0 | animate to `0`, then rotate backward and jump to `W` |

Two separate flags guard this:

- `snapping` prevents `SnapAsync` re-entering while its own animated scroll is generating `Scrolled`
  events.
- `recentring` suppresses *snap scheduling* around the rotation and the instant jump, so the recentre
  cannot be mistaken for a new gesture.

Neither flag gates the day-header sync. That is deliberate and was learned the hard way — see section
8.

**Android still waits for inertia.** There is no `PagingEnabled` equivalent on the platform scroll
view, so the timer picks the target there and the delay described at the top of this section applies.
Closing that gap means detecting `ACTION_UP` on the platform view, cancelling the fling, and driving
the settle manually — or moving the horizontal axis off `ScrollView` altogether (section 15).

**Known risk.** The 90 ms threshold is a starting point, not a measured optimum.

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

---

## 7. The grid is drawn, not built from cell views

A week at 15-minute resolution is 7 × 96 = 672 cells; three rendered weeks would be over 2,000 views
whose only job is to draw a line. The entire background — day-column shading, hour lines, half-hour
lines, day separators, the current-time indicator — is instead drawn on a single `GraphicsView`
(`WeekGridDrawable`).

**One `GraphicsView` for all three slots**, not one per week. The grid is nearly identical between
weeks; the drawable simply draws 21 day columns. Week-specific details (weekend shading, today's
highlight, the current-time line) are handled by redrawing on `Invalidate()` when the weeks rotate,
which is one draw call.

Tapping empty space does not need cell views either — and in fact **all** input for the surface is
handled on the `GraphicsView`, including taps on appointments. Appointment views are
`InputTransparent`, and `HitTestAppointment` resolves a touch point against their known bounds; empty
space falls through to `ResolveSlot`, which converts the point to a slot, day and snapped time
arithmetically. Section 11 explains why input is centralised here rather than left to per-appointment
gesture recognizers.

`SchedulerGeometry` is a single mutable object shared by the control and both drawables, so the drawn
background and the positioned views can never disagree about where an hour line sits.

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
   grabbed point under the finger, with a floating label showing the target time. No layout pass runs
   during the drag.
5. `EndInteraction` raises `AppointmentDropped`, or — if the drag never armed and the finger did not
   travel — is what raises the tap events instead.

**This is the one place the control reaches past MAUI**, in `SetScrollingEnabled` and
`ConfigurePlatformScrolling`:

- Freezing scroll cannot use `IsEnabled = false`: that disables interaction for the whole subtree and
  cancels the very touch driving the drag. iOS uses `UIScrollView.ScrollEnabled`; Android uses
  `RequestDisallowInterceptTouchEvent`.
- `UIScrollView.DelaysContentTouches` is turned off, because it otherwise withholds touch-down while
  deciding whether the touch is a scroll — exactly the signal the long press depends on.

### Crossing a week boundary

Moving an appointment into another week means the weeks have to rotate while the drag is still in
progress, which the ring buffer (section 4) and the reconciliation (section 6) would otherwise fight:
rotation recycles a slot, and reconciliation would return the dragged view to the pool underneath the
finger.

Instead the appointment **leaves the weeks entirely for the duration of the drag**. On pick-up it is
excluded from what the slots lay out, and becomes two views that belong to no week:

- a **ghost** — the original view, detached where it stood and faded to half opacity, so the slot
  being vacated stays visible. It is shifted by one viewport whenever the weeks rotate, so it travels
  with the week it came from and slides off screen behind the drag.
- a **lifted view**, rented from the pool, riding the finger.

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

That slide moves the surface, and the lifted view lives in surface coordinates, so it would ride along
and leave the finger. `OnPagerScrolled` therefore pushes it back by each scroll delta while a drag is
armed, keeping it screen-stationary. The same handler stops scheduling snaps while dragging, since the
pager is being driven by the drag rather than by the user.

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

Scope is the week view only. The following are absent by design, not by oversight:

- **Day, Month and Agenda views.** The pager and pooling generalise to them, but only the week surface
  exists today. The app keeps its Syncfusion-based `SchedulePage` for the other views.
- **All-day / multi-day appointments.** There is no all-day row; an appointment is clipped to its
  starting day.
- **Resizing an appointment by dragging its edges.**
- **Dark theme.** Colours are exposed on the drawables and `GridBackgroundColor` on the control, but no
  `AppThemeBinding` wiring is provided.
- **Releasing template roots.** Bounded by BlazorBindings, not by this control — see section 6.

## 15. Verification status

`Owlery.Maui.Scheduler.Tests` covers the platform-independent behaviour headlessly on `net10.0`:
overlap layout and clipping, appointment placement, view reuse across a refresh, cell and appointment
taps, long-press-to-drag including refusal and the scroll-not-drag case, and week paging. It runs
without a simulator, and found two bugs that had already shipped.

Rendering, paging, week rotation, overlap layout, the current-time line and appointment semantics have
also been checked on the iOS simulator through DevFlow.

Drag-and-drop is confirmed working on the iOS simulator by manual testing. It could not be automated:
DevFlow drives gestures by invoking a MAUI gesture recognizer and reports that "native pan injection
is not available on iOS", and input is deliberately handled through `GraphicsView` touch events rather
than a recognizer. The *failure* of the previous implementation was captured through DevFlow before it
was replaced — that trace is what identified the race described in section 11.

Android has not been exercised at all; `SetScrollingEnabled` takes a different branch there
(`RequestDisallowInterceptTouchEvent`) and is the part most likely to need adjustment.

The animated slide when paging mid-drag is asserted in tests only as a sequence of scroll requests —
the test shim applies them instantly. That it *looks* right, and that a programmatic scroll still runs
while `UIScrollView.ScrollEnabled` is false, both need a device.
