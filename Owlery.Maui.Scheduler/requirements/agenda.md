# Agenda view

What the scheduler shows when appointments are presented as a continuous list rather than on a time
axis or month grid. Where this differs from the shared appointment and data requirements, this file
wins for an agenda.

## Structure

**AGD-1** The view can show appointments as one vertically scrolling agenda, in chronological order.

**AGD-2** An agenda does not page horizontally. Its month identity is used for programmatic
navigation and data loading, not as a sideways page boundary.

**AGD-3** Every month covered by the loaded range has a full-width heading, including a month with no
appointments.

**AGD-3a** The built-in heading identifies an empty month with configurable text. A custom section
template receives a zero appointment count and owns how to present it.

**AGD-4** A week heading is shown only for a week that lies entirely within the loaded range and
contains at least one appointment. A week straddling the range's edge — a Monday and Tuesday before the
range begins, say — gets no heading until the range covers it, so a heading never advertises days the
reader cannot yet see, and the content does not shift up when those days arrive.

**AGD-5** A day with no appointments consumes no row. A non-empty day is identified in a leading
gutter aligned with the top of that day's first appointment.

**AGD-6** Later appointments on the same day continue beneath an empty gutter; the day marker is not
repeated for every row.

**AGD-6a** When current-day highlighting is enabled, the rows belonging to today share the current-day
background used by the other surfaces.

## Appointments and sizing

**AGD-7** Every appointment is one row. Rows are ordered by start time, with longer appointments
first when their starts are equal, while otherwise preserving the host's order.

**AGD-8** An appointment belongs to the day it starts. An appointment crossing midnight is not
repeated on later days.

**AGD-9** By default, a row is as tall as its template's content requires. Rows are estimated before
first use, then measured and kept at that measured height when they return to the screen. A host that
can calculate height from the appointment may provide an authoritative height function, in which case
the row is laid out at that height without measuring its view.

**AGD-10** Only rows near the viewport have live views. Scrolling reuses and rebinds pooled views;
the number of views is governed by the viewport and its preparation buffer rather than by the number
of appointments loaded.

**AGD-11** Agenda appointments may use a template of their own. When none is supplied, the ordinary
appointment template is used.

**AGD-12** Month, week and day chrome comes from one section template whose binding context identifies
the kind, date and appointment count. A plain built-in section view is used when no template is set.

**AGD-12a** Headings and day markers belong to the rows they introduce. When there is nothing to lay
out — no items, or no appointment template — no headings are shown either.

## Navigation and interaction

**AGD-13** Entering the agenda and jumping to a date places the first appointment on or after that
date at the top. An empty date advances to the next meaningful row rather than creating an empty day.

**AGD-13a** Vertical scrolling updates `DisplayDate` to the date at the viewport anchor without
turning that internal two-way update into programmatic navigation. Calling `ScrollToDate` still
repositions the agenda when its argument already equals `DisplayDate`.

**AGD-14** Tapping an appointment reports it as on every other surface. Empty agenda space reports a
whole day but draws no selected-cell affordance; an agenda has no time axis on which to create a
finer selection.

**AGD-15** Dragging, the hour gutter and the fixed day-header strip are absent. Their events are not
raised in an agenda.

**AGD-16** The loaded range always spans whole months, so a month heading never appears over a
partial month and then changes when the rest of the month is loaded. The range grows in both
directions as its edge is approached, without moving the content already under the reader. Forward
growth adds content below and does not move the reader; backward growth prepends above and so must
move the offset, and is deferred until the vertical scroll goes quiet — so it does not interrupt a
fling.

**AGD-16d** Growing the range asks the host for more; it does not render the wider range until the
host has answered. Nothing moves, and no heading appears, on the strength of a request alone — so a
week straddling the old edge is not announced before its days are loaded.

**AGD-16c** Content arriving above the viewport does not move what the reader is looking at, whether
it arrives from range growth or from the host answering a widened range. The row under the reader
stays where it is for the whole of the insertion, rather than moving and being corrected back.

**AGD-16b** Range growth responds to the reader's scrolling, not to the platform applying an offset
the control itself asked for. Entering the agenda, or jumping to a date, does not grow the range as a
side effect of the scroll settling.

**AGD-16a** Growth is a request for data, and one request is outstanding at a time. Reaching the end
adds one month; another is added once the host has supplied items or the reader has moved away from
the edge and returned. A month the host has nothing for is therefore added once rather than
repeatedly, however long the reader stays at the end.

## Data and accessibility

**AGD-17** The visible-dates report contains the whole loaded agenda range because every one of those
days is reachable by vertical scrolling without another page transition. Its prefetch boundaries are
the same range.

**AGD-18** Month, week and day sections are readable by assistive technology in addition to the
appointments themselves.
