# 16. Showing fewer than seven days

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

## Animating the change

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

### The transition is aimed, not fixed

Opening a day is two property writes — `DisplayDate` and `VisibleDays` — and a bindable property is
written one at a time. Whichever lands second finds the calendar already moved for the other, and
which one that is depends on the host: the sample writes the date first, while a Blazor host writes
whatever order the parameters appear in its markup.

Date first is harmless when the day being opened is on the page already showing — a header tap, which
is the overwhelming case — because the page does not change and only the day count moves anything. Day
count first is not: the page is rebuilt from the *previous* date, so the columns expand onto whichever
day was last opened, and the date arriving a moment later is then an ordinary navigation — one day away
from where the transition just landed, so `TrySlideToPage` slides across to it. What the user asked to
be one zoom onto Friday came out as a zoom onto Thursday followed by a slide, and repeating the same
day looked fine because the second write changed nothing.

`DayCountTransition` therefore holds the shift as something it is *aimed* at a page rather than a
number worked out once in `ChangeVisibleDays`. A `DisplayDate` change arriving in the same tick as a
day-count change rebuilds onto the page asked for and re-aims the transition at it, instead of
navigating on top of it. `Commit` applies its first frame immediately and both writes land in the same
batch, so the correction reaches the geometry before a frame is composited — the zoom simply happens to
be onto the right day.

The gate is *a day-count change in this tick*, not *an animation in flight*, and the difference is
load-bearing. The transition is skipped altogether when there is no handler, when the control is not
loaded, or when the column width barely moves; gating on it would have made a host's two writes behave
one way on a live control and another on one still attaching. It also bounds the window. An animation
lasts 220ms of wall clock, so gating on it would quietly absorb any *unrelated* date a host set inside
that window — a "Today" button, a binding firing late — and turn the slide it deserves into a cut.
`ChangeVisibleDays` marks the flag before its own skip guards and clears it on the next tick, the way
`QueueRepopulate` scopes a rebuild, which is also what makes the decision reachable from the headless
suite through `TestDispatcher.DeferDispatch`.

What this does not do is make the two orders equivalent for a day on *another* page. Setting the date
first there is a genuine change of period, and it travels before the day count zooms; only the count-
first order collapses to a single movement. `docs/requirements/navigation.md` (NAV-28a) states the
guarantee at that width rather than more broadly.

Re-aiming rather than restarting is the point: restarting would snap the columns back to the width
they began at, which is the one thing the transition exists to avoid. Progress lives on the transition
and the animation only advances it, so the shift can be recomputed underneath a run already in flight.

A transition is only held still against a page it shares a day with. When the two do not overlap —
a host that opens a single day a month away — there is nothing on screen to hold, and compensating
anyway would fly a month of columns past on the way in. The shift is zero there and the columns
simply resize in place.

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

