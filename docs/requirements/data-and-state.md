# Data and state

What the scheduler needs from the host, and how it behaves while waiting.

## Where the data comes from

**DAT-1** The scheduler holds no data of its own and fetches nothing. The host supplies appointments;
the view decides what to show from them.

**DAT-2** The host may supply appointments covering any span of time. The active surface selects the
ones belonging to its rendered dates, so the host is free to keep a wider set loaded.

**DAT-3** The view never modifies what it is given. Any change resulting from an interaction is the
host's to make.

## Asking for what it needs

**DAT-4** Whenever the visible week changes, including the first time the calendar is shown, the view
reports the range of dates it needs.

**DAT-5** The reported range is wider than the visible week: it covers the weeks either side as well.
This is what allows a swipe to land on a week that is already populated rather than visibly filling
in afterwards.

**DAT-6** The report also identifies the seven days actually on screen, so a host that wants to
distinguish "showing" from "ready to show" can.

**DAT-7** Reports are raised for genuine changes of week. Scrolling within a week does not ask for
data again.

**DAT-7a** An agenda is the exception to the visible-week shape in DAT-4–DAT-6. It reports its whole
loaded range as both visible and prefetched, because every loaded day is reachable by vertical
scrolling without a horizontal page transition. Approaching either edge extends that range and raises
another report so the host can load the added month.

## While loading

**DAT-8** The host can tell the view that data is being loaded. The view then shows that work is in
progress.

**DAT-9** The busy indication does not prevent interaction. Whatever is already on screen stays
readable, scrollable and usable.

**DAT-10** Loading is not a blocking state: the calendar is never replaced by a spinner, and previously
loaded appointments stay visible while more arrive.

**DAT-11** Appointments appear as they become available. A host loading several weeks in parallel does
not have to wait for all of them before anything is shown.

## Empty and failure states

**DAT-12** A week with no appointments shows the grid, headers and current-time line as normal. An
empty week is a legitimate result, not an error.

**DAT-13** The view draws no error state of its own. Reporting a failed load is the host's
responsibility, as it is the host that knows a load failed.

**DAT-14** A failed load leaves the calendar as it was rather than clearing it, so a person is not left
with a blank week because of a transient problem.

## Time zone contract

**DAT-15** Every time the host gives the view, and every time the view reports back, is a wall-clock
time in the calendar's declared time zone.

**DAT-16** An appointment is placed according to what its times mean. A time that carries no zone
information is drawn exactly as given, whatever zone the view is showing; a time that identifies a
moment — because it is UTC, because it is this device's, or because the appointment names the zone its
times are written in — is placed where that moment falls on the view's clock.

**DAT-22** A clock reading its own zone never had, or had twice, still resolves. The view does not
refuse to draw a day, and does not fail when asked what moment such a reading names: a reading a
transition skipped is taken as the one the gap moved it to, and a reading that occurs twice is taken
as its first occurrence.

**DAT-23** What the view reports is a position on the calendar it is drawing, together with the zone
that calendar keeps. A host reads it as a clock reading or asks it for the moment it names; neither
host has to convert what the other one wanted.

## Changing the collection

**DAT-17** A host may update an already-assigned appointment collection in place, and the view observes
the change and repaints. The host does not have to assign a new collection instance every time its data
changes.

**DAT-18** Observing in-place changes is optional. A host may also assign a fresh collection instance,
which refreshes the view just the same. Either style is supported; neither is required.

**DAT-19** The host is not expected to trim the collection for periods the view is not showing. The view
selects the visible period itself, so a host may keep the whole loaded range — including the months
either side of the visible one — in the collection at once.

**DAT-20** A host that mutates in bulk should do so with a range operation, so a bulk change costs one
change notification rather than one per item. The view reads the changed collection as a whole, so the
single event is sufficient for it; the event still reports which items were added, removed or replaced,
for any other consumer of the same collection.

**DAT-21** The view observes the collection, not the appointments in it. A host that changes an
appointment supplies it as a new instance keeping the same identity, and the view repaints it in place;
an appointment mutated where it sits is not noticed, and the view goes on showing what it last read.
