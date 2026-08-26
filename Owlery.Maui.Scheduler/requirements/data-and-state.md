# Data and state

What the scheduler needs from the host, and how it behaves while waiting.

## Where the data comes from

**DAT-1** The scheduler holds no data of its own and fetches nothing. The host supplies appointments;
the view decides what to show from them.

**DAT-2** The host may supply appointments covering any span of time. The view selects the ones
belonging to the week on screen, so the host is free to keep a wider set loaded rather than one week
at a time.

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

**DAT-16** The view performs no time-zone conversion. Converting to and from whatever the host stores
happens at the host's boundary, in one place.
