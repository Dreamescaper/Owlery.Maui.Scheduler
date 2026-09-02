# 21. The agenda is one measured, virtualized vertical surface

The page seam now has three shapes:

| Surface | Horizontal shape | Vertical height | Realized content |
|---|---|---|---|
| Timeline | three-page ring | hours × `HourHeight` | every appointment on three pages |
| Month | three-page ring | one viewport | every chip that fits on three grids |
| Agenda | one continuous page | sum of row heights | viewport plus half a screen of overscan each way |

An agenda deliberately does not build a vertical ring buffer. Recycling whole vertical pages would
require inserting above the reader and correcting the platform scroll offset in the middle of a
fling. Immediate offset control is exactly the problem that forced the horizontal axis to own a
platform scroll view in [section 19](pager.md). The simpler surface is one table of rows, one vertical scroll
extent and windowed realization inside the existing `PopulateSlot` reconciliation.

## The flat row table

Month and week headings and appointment rows live in one ordered table. A day marker does not: it is
anchored in the leading gutter beside the first appointment of its day and consumes no height. Empty
days therefore disappear naturally, empty weeks never earn a heading, while month headings are added
while walking the date range whether or not that month has data.

The built-in headings are plain but consistent with the rows they introduce, not decoration. A month
heading shares the rows' left edge (inset past the day gutter the day markers occupy) and sits centred
in its strip; a week heading is a small, muted, tracked range read as a kicker under it. Days are
separated not by a drawn line but by whitespace — `AgendaGeometry.DayGap`, owned by the day that
precedes it — so rows within a day stay contiguous and an empty day costs nothing. Anything more
opinionated is the host's to supply through `AgendaSectionTemplate`.

A week heading is emitted only for a week that lies entirely inside the loaded range. The leading week
of the range often straddles the range's edge — a month starting on a Wednesday leaves that week's
Monday and Tuesday outside the range, not yet loaded. Announcing the week then, and having the content
shift up when those two days arrive, is the "week row appears then disappears" the reader otherwise
sees. Holding the heading until the week is whole means it never describes days the reader cannot see
and appears once, in place.

Each row stores its `Top` and `Height`. That makes placement a field read and finding the first row in
a viewport a binary search. `AgendaSurface.Layout` builds the table only when data or geometry makes
it stale; scrolling only slices the existing rows. The ordinary appointment reconciliation then does
the virtualization: appointments still inside the slice keep their views and binding contexts, ones
leaving it return to the pool, and new ones rent those spares. Sections use a second pool and a
separate `PageSlot.SectionViews` list, because appointment hit-testing walks `PageSlot.Views` and must
never mistake chrome for an appointment.

## Why this surface measures

Every earlier layout is arithmetic. Timeline height comes from duration, and month chips are fixed
lines. Making agenda rows fixed too was rejected because this is the surface where a host needs to
show the information that the grid supplied elsewhere — time, location, conferencing and wrapped
subject text. Cropping that into one universal height would make the new surface less useful than the
two it complements.

Unseen appointment rows begin at `AgendaEstimatedRowHeight`. The first time one is realized its view
is measured at the actual row width with unbounded height, and that result is cached by appointment
`Key`. A table rebuild reuses those measurements, so scrolling away and back never pays twice. A
width or template change clears the cache because the old answer is no longer meaningful.

Only those. `ApplyGeometry` runs for every geometry property and every size reallocation, and it
invalidates the row *table* each time because the table is derived from geometry — but the
measurements are not, and clearing them there would have every pass re-measure the screen, which is
the cost the cache exists to avoid. Worse, it is visible: rows above the viewport that are not
re-realized would sit back at the estimate, and content above the reader changing height is exactly
what `ShiftContentAbove` moves the offset for, so an unrelated property write would shift the list.
The width a row was measured at (`AgendaGeometry.RowWidth`, which the gutter width feeds) is tracked
across applications and is what decides whether the cache survives.

`ActiveHeaderHeight` is the agenda's other easily-missed asymmetry: it is zero, because there is no
day-header strip, so the agenda's viewport height is the whole allocation rather than the allocation
less `HeaderHeight`. Anything comparing a freshly computed viewport height against the stored one
must use `ActiveHeaderHeight` too — `OnSizeAllocated`'s "nothing changed, do not re-apply" guard
subtracted `HeaderHeight` unconditionally, so in an agenda the two could never agree and the guard
never held, putting a full rebuild (and, before the paragraph above, a full re-measure) on every
arrange pass.

Hosts that already know a row's height may set `AgendaRowHeight`. The function is evaluated while the
row table is built, so those rows begin measured and the scroll path avoids both `View.Measure` and
the correction/reconciliation pass that follows it. It is deliberately optional: a function of the
appointment alone cannot correctly size text that wraps differently with width, font scale or
localization, so content-driven templates retain the measured path.

When a measurement differs, the row is corrected and every stored `Top` below it is reflowed. A row
entirely above the viewport moves what the reader is looking at, so the same delta is applied to the
vertical offset. This is the `ShiftContentAbove` primitive, also used when backward range growth or
newly loaded data inserts rows above the visible anchor. A correction may reveal another row, so the populate/measure pass
may repeat twice and then stops. The cap turns an oscillating template into a visual defect rather
than a hung frame.

A measurement that already matches the estimate marks the row measured but does not reflow or repeat
the population pass: no stored position changed. Scroll-path reconciliation also reuses its temporary
lists and dictionaries and guards binding and transform writes that are already current. These small
fast paths preserve the ordinary reconciliation model without introducing a second incremental-window
state machine.

The accepted platform risk is that MAUI applies `ScrollToAsync` through the handler on a later pass.
That risk was confirmed by watching (and instrumenting) the Android emulator: while scrolling upward,
rows entering the top overscan measured shorter than their estimate, so `ShiftContentAbove` pulled the
offset back a few dozen pixels on each `Scrolled`, and because the write lands after the gesture's own
render, the reader sees the content wobble up and down for a frame or two. It is worst at a slow pace,
where a correction is large next to the small motion between frames, and it is the estimate→measured
correction rather than range growth — `VisibleDatesChanged` only happens to coincide near an edge.
The headless suite can prove the delta, but only the platform shows whether a human sees it.

The fallbacks, reconsidered against a reader who scrolls without ever stopping: correcting once the
fling rests is unworkable — continuous scrolling never rests, so the offset would drift and snap rather
than wobble; a larger pre-measured buffer does not help, because a row is corrected the moment it enters
the window and a bigger window only measures it earlier, not fewer times. The robust fix is to own the
vertical platform scroll view so the offset can be written synchronously with the reflow (as the
horizontal pager already does in [section 19](pager.md)); that is deferred for now rather than taken lightly.

What is done instead is to stop the discrepancy being visible. A compensating shift records the
offset it asked for, and until the platform reports that offset the vertical scroll view's content is
translated by exactly what it still owes — so the content the reader is looking at does not move at all. The translation is
recomputed from each `Scrolled` event as `target - ScrollY`, which means it shrinks to nothing as the
platform catches up and is released in the same frame that reports the offset it stood in for. Both
the transform and the scroll are applied to the platform view, so they compose in one frame instead of
fighting across two.

This matters most where the insertion is large — a month of appointments arriving above the viewport
is around five thousand units of reflow, and unheld the reader is shown the inserted month and snapped
back a frame later.

**Growing is a request, and nothing is laid out until it is answered.** Backward growth used to happen
in two stages: widening the range added an empty month (its heading, 56 units) and asked the host for
it, and the host then answered with the month's appointments. Both stages reflowed, so both had to be
compensated — and worse, the intermediate state was a range the data did not cover yet, which made the
rules that describe a range describe the wrong one. `AGD-4` withholds a week heading until the week is
wholly loaded, and the widened-but-unanswered range made a week straddling the old edge eligible while
its earlier days were still empty. Recording the simulator and stepping the frames caught it exactly:
one frame with `23 FEB – 1 MAR` inserted under the March banner, every row below pushed down by its
height, and the heading gone again a frame later when February's appointments arrived and carried it
up into February where it belonged. That 28-unit flicker, not the offset lag, was what the reported
blinking actually was.

So `GrowForward` and `GrowBackward` now extend a *requested* range only. `DatesOn` reports it, so the
host is asked; the laid-out range catches up in `AdoptRequestedRange`, called when items arrive. The
range on screen and the data in it therefore always agree, the empty intermediate state never exists,
and what used to be two compensated reflows is one. A host that never answers is not wedged: the next
growth adopts what was last asked for before asking again.

**The hold must go on `bodyGrid`, not on `surface`.** The first attempt translated `surface`, which
is the obvious candidate — it is the thing whose children are being reflowed. It did nothing at all,
and did nothing *silently*: the property is set, the logs show the right magnitudes taken and
released at the right moments, and the list still blinks. `surface` is the content view of
`PagingScrollView`, whose handler owns its frame ([section 19](pager.md)), so a transform written there is
discarded on the next layout pass. Establishing this took a deliberately crude experiment — force a
fixed offset and screenshot — because no error, no warning and no log distinguishes "the transform
was applied" from "the transform was thrown away". `bodyGrid`, the content of the ordinary vertical
`ScrollView`, does move, and moves headings, day markers and rows together. The general lesson is the
one [section 19](pager.md) keeps teaching: taking over a handler takes over everything it did, and a view inside
a scroll view whose handler you own is not a view you can transform.

The hold is bounded by the same settle timer the entry navigation uses, because a translation left
behind would be a permanent visual offset — a worse failure than the jump it prevents — and the
platform can clamp a requested offset and then report nothing further. It also does not claim to make
every correction invisible: `ApplyMeasuredHeights` deliberately preserves the row straddling the
viewport rather than the rows below it, so a row that measures shorter than its estimate still moves
what is beneath it. That is the settling the previous paragraphs describe, and `AgendaRowHeight`
remains the way to avoid it.

The practical resolution for a consumer is to avoid the correction entirely by supplying
`AgendaRowHeight` when the row's height can be calculated from its appointment — which is why the
sample wires `AgendaRow.RowHeight`. The cost is size, not smoothness: the returned value is
authoritative and fixed, so a row whose height is estimated consistently is a fixed amount off (a
little extra room reads fine; clipping does not), and it must not be used for content whose height
depends on width, wrapping, font scale or localization. Content-driven templates that cannot state a
height in advance keep the measured default and accept the correction.


## Navigation and the current range

The agenda still identifies itself by the first of the month containing `DisplayDate`, so the public
date contract stays compatible with the other surfaces. Its initial range always covers whole months —
the month of `DisplayDate` plus the months its lead and trail days reach into, rounded out to whole
months. A partial month would show a month's tail under a heading and then, when the host answered,
the whole month under the same heading: the same month header with different events. Rounding the
edges to whole months is what keeps every month whole.

The page follows the month of `DisplayDate` for *navigation* only, not for incidental re-application.
`ApplyGeometry` derives the page from `DisplayDate` once, on first layout; afterwards it keeps the
page navigation set. Re-anchoring on every geometry or size change is wrong for the agenda, because
`DisplayDate` tracks the viewport while the reader scrolls — each re-application would collapse the
range the reader just grew back to `DisplayDate`'s month, shifting every heading. The range therefore
grows as the reader scrolls and resets only when navigation (a `DisplayDate` change via
`ScrollToDate` or the step buttons) rebuilds around a new month.

Approaching the bottom extends the existing range by one month and rebuilds the row table; because
all new content is below the reader, the offset does not change and the fling is untouched.

Once, though. Growth is really a request — it widens the range and asks the host, through
`VisibleDatesChanged`, to fetch it — and the answer arrives on a later tick. The trigger is a
position, and the position is satisfied throughout the last screen of travel, so every `Scrolled`
event of a fling into the end would ask again; and a month the host has nothing for adds only its own
heading, so the position keeps being satisfied and the range runs away, rebuilding the whole row
table and re-reporting the range per frame. One request is outstanding at a time. It is released when
items arrive, and also when the viewport leaves the end — so a host with nothing more to send costs
one empty month per visit rather than one per frame, and a host that answers keeps growing smoothly.

Which offsets count as the reader's is its own question, and getting it wrong is how the range grew
without anyone asking. `ScrollToAsync` is applied by the handler on a later pass, and iOS reports
every offset it passes through on the way to the requested one. Recognising only the exact offset
asked for leaves each intermediate one indistinguishable from a gesture — and an agenda entered from
a scrolled timeline settles *upward*, because the vertical scroll view is shared between the modes
and arrives holding the timeline's offset against a content size the platform has not resized yet.
Instrumenting the simulator showed it plainly: a request for 5416, then reported offsets of 135 and
83, the second above the first and both inside the overscan. Read as a gesture that is a fling into
the top, and the range grew a month backwards before the reader had touched the screen. Android did
not reproduce it, which is exactly why it needed the device.

So a navigation stays in progress until one of the reported offsets *is* the one requested. The
window is timed (a quarter second) rather than held open until it arrives, because it may never:
the platform can clamp the request against the stale content size, stop short, and report nothing
more. An open-ended window would leave the agenda ignoring the reader for the rest of the session —
a far worse failure than the one it fixes. When the window closes, the last observed offset is
resynchronised to where the scroll actually stopped, so the next event's direction is measured
against somewhere the list really went.

Approaching the top prepends a month, which does move the offset — and writing that offset mid-fling is what stops
the reader: `ScrollToAsync` cancels the platform's in-flight fling. So backward growth is deferred
until the vertical scroll goes quiet (it naturally does so at the loaded top) and completes then, when
no fling is left to cancel. Only a viewport that has genuinely left the top cancels a deferred growth:
a fling into the top settles by springing back, which arrives as an upward delta at an offset still
inside the overscan, and treating that as the reader moving away would drop the growth the fling had
just asked for. Before the rebuild the surface records the identity and `Top` of the row
anchoring the viewport, then finds the same row afterwards and applies the difference through
`ShiftContentAbove`. The same anchor
calculation runs when the host responds to the widened `VisibleDatesChanged` range, so appointments
that arrive asynchronously above the viewport do not cause a second jump.

Bound the loaded range and the two conflict: trimming content above the viewport must move the offset,
and MAUI applies that move through `ScrollToAsync` on a later pass, which cancels the platform's in-
flight fling and can clamp the scroll when the freed height shrinks the range. A reader flinging
forward therefore reads it as the list stopping. It is possible only by owning the vertical platform
scroll view so the offset can be written synchronously with the reflow (as the horizontal pager does
in [section 19](pager.md)); that is why the range is deliberately left unbounded rather than trimmed.

