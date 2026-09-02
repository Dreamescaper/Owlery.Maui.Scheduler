# 18. The month surface

## Six rows, always

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

## Overflow, and why the marker eats a line

A cell fits `LinesPerCell` chips beneath its day number. When a day has more, the last line is given
over to a "+N more" marker, so one *fewer* appointment is shown than would physically fit and `N`
counts the appointment whose line the marker took. Showing five and hiding the rest silently would be
worse: the cell would look complete when it was not.

The marker is drawn on the canvas rather than rented from the pool. It carries no data and needs no
interaction of its own — a touch anywhere in the cell already resolves to that day — so making it a
view would put something in the pool that is not an appointment for no gain.

Dots instead of chips were considered and rejected. The host's appointments are lessons, and the
useful content is a person's name; a dot says only that the day is not empty.

## The chrome, and what a mode switch costs

The timeline and month modes are one visual tree, not two. `pagerScroll`, the surface and the pool are
shared, and what changes is settled by `ApplyMonthChrome` / `ApplyTimelineChrome`: the gutter and the header
corner collapse to zero width, the timeline's scrolling day-header strip hides and a static weekday
row takes its place, and `gridView.Drawable` swaps. Reparenting the pager between two containers was
the alternative and it buys nothing — a month's content is exactly the viewport height, so the
vertical `ScrollView` it already sits in simply has nothing to scroll.

The weekday row is built per page, through the same three-slot header strip the timeline uses. The
columns *do* mean the same thing on every month — every page starts on `FirstDayOfWeek` — so a single
fixed row would have been correct and cheaper. It also looked wrong: a heading that stays put while
the pages slide under it reads as though the calendar has come apart from it. Three label rows is a
small price, and it means the header-mirroring of [section 8](grid-and-scrolling.md) applies unchanged in both paging modes.

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
is already there: every surface can identify the same appointment.

`DisplayDate` is deliberately *not* rewritten on a mode change. It means "some day on the page" and is
only written back by a swipe, so switching to a month and back leaves the user on the day they were
looking at rather than collapsing them to the first of the month.

## A month is the only surface whose height depends on the viewport

`SchedulerGeometry.ContentHeight` is hours times `HourHeight`; `MonthGeometry.ContentHeight` is simply
`ViewportHeight`. That difference makes the month the only surface that has to be re-laid-out when the
control's *height* changes, and `OnSizeAllocated` was deciding whether to re-apply the geometry by
comparing the width alone. A height-only reallocation therefore updated `ViewportHeight` and returned
without applying it, so the surface kept the height request derived from whatever the control was
allocated first.

It showed as a month that could still be scrolled by a dozen pixels or so — small enough to look like
the same defect as the measurement bug above, and it survived fixing that one. Both axes are compared
now. The timeline never noticed because its height has nothing to do with the viewport.

## What the month engine does not do

No overlap packing — a month cell has no time axis, so nothing can collide and appointments are
simply listed in the order they start. No visible-hours window either: the timeline clips to
`StartHour`..`EndHour`, but a 06:00 lesson appears in a month regardless. And no multi-day spanning,
matching the timeline ([section 14](README.md)) — an appointment is placed on the day it starts.

Ordering is `OrderBy(Start).ThenByDescending(duration)`, and LINQ's sort is stable, so appointments
starting together keep the order the host supplied. That is load-bearing rather than cosmetic, though
not for the reason it first appears: reuse is by key ([section 6](appointment-views.md)), so an unstable sort would not cause
a repaint — it would make two appointments that start together *swap places* in the cell between one
reload and the next, because a chip's line within a day is its index in that order.

