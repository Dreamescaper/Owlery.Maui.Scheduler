# Interaction

Everything a finger does: arming a drag with a long press, carrying an appointment across weeks and
hours, and the taps that select a slot or land on the chrome.

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
4. `DragInteraction` applies movement via `TranslationX/Y` only, snapped to `DragSnapMinutes`, keeping the
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
The pager answers for itself now ([section 19](pager.md)), so the settings that used to be listed here for it —
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
progress, which the ring buffer ([section 4](paging.md)) and the reconciliation ([section 6](appointment-views.md)) would otherwise fight:
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

Edge paging makes this the sharpest test of the identity rule in [section 6](appointment-views.md). Paging raises
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

**Known risk.** The Android branch of `SetScrollingEnabled` is untested — see [section 15](verification.md).

---

## 12. Cell selection

Tapping empty space sets `SelectedSlot` and raises `CellTapped`; the control renders one reusable
overlay view at that position. The **arm-then-confirm** behaviour (first tap selects, second tap on the
same cell opens the create flow) lives in the host page, not the control — it is product behaviour, and
keeping it out means the control does not have to guess what a second tap should do.

### Taps on the chrome, which do not go through the drawing surface

`HeaderTapped` and `TimeGutterTapped` report the day headers and the hour gutter. They take their
input differently, and the difference was not a choice — it was a device correcting one.

The **header** uses a `TapGestureRecognizer`. It sits outside the vertical scroll view and nothing
scrolls by dragging on it, so a recognizer claiming the gesture costs nothing, and a tap is exactly
what a `TapGestureRecognizer` is for.

The **gutter** started the same way and had to change. The hours are *inside* the vertical scroll
view, and a recognizer on the container claims the gesture outright: a drag begun on the hours stopped
scrolling the timeline at all. Both were verified by injecting real touches with `adb shell input` —
a swipe starting on the grid scrolled, the identical swipe starting on the gutter did nothing. So the
gutter takes its input from a `GraphicsView` behind the labels, the way section 11 says everything
else does, which receives the touch and still lets the scroll view have the drag. That the grid has
always worked this way is what made it the obvious answer once the symptom was clear.

The `GraphicsView` is opaque for the reason recorded in [section 7](grid-and-scrolling.md) — a transparent drawing surface does
not reliably receive taps — which also preserves the gutter's other job of painting over the pager
beside it ([section 19](pager.md)).

What they do share with the grid is the arithmetic. `TimelineSurface.DateAt` resolves a column and
`Snap` rounds a minute, and both the grid and the chrome go through them, so a header cannot name a
different day than the column beneath it and the gutter cannot name a different slot than the cell
beside it. `SlotAt` was split into exactly those two pieces rather than having them written twice.

Two details that are easy to get wrong:

- **The header strip is already in page space.** It is three pages wide and translated by the pager
  offset, so a position taken against the strip needs no correction for the scroll — it is the same
  space the columns were laid out in.
- **A month raises neither.** Its header names weekdays that recur down six rows, so no column stands
  for one date, and it has no gutter at all. Guarding the mode matters more than it looks: the
  timeline's geometry is still populated after a switch, so without the check a header tap in a month
  would report a date computed by counting days across a grid that is not laid out in days. A test
  that opens *in* a month cannot catch that — there is no timeline geometry yet, so it stays silent
  either way — which is why the one covering it switches into a month from a week.

