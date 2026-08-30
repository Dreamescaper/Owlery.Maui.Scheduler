# Month view

What the scheduler shows when it is displaying a calendar month rather than a run of days. Everything
here is in addition to the timeline behaviour described in the other documents; where the two differ,
this document wins for a month.

## The grid

**MON-1** The view can show a whole calendar month instead of a run of days.

**MON-2** A month is a fixed grid of six week rows by seven day columns, sized so the whole month is
on screen at once. It does not scroll.

**MON-3** The columns are headed by abbreviated weekday names in the person's language, beginning on
the configured first day of the week. The heading does not change from month to month, but each page
carries its own and moves with it — a heading that stayed put while the months slid under it would
read as though the calendar had come apart from it.

**MON-4** The grid opens on the first day of the week falling on or before the 1st, so the leading
cells hold the closing days of the previous month and the trailing cells the opening days of the next.

**MON-5** Days belonging to the neighbouring months are visibly muted, but behave like any other day:
they show their appointments and can be selected.

**MON-6** Because the row count is fixed rather than fitted, a short month that begins on the first
day of the week shows two whole rows of the following month.

**MON-7** Today's cell is distinguished from the rest when current-day highlighting is enabled.

**MON-8** Days outside the configured working week are distinguished when non-working-day shading is
enabled. Working hours do not apply to a month, whose cells have no time axis.

## Appointments in a month

**MON-9** Each day lists its appointments in the order they start, one line each. Nothing is placed by
the time of day, and appointments occupying the same hours do not divide a cell between them.

**MON-10** Every appointment on a day is listed whatever time it starts at. The day window that
governs the timeline does not apply.

**MON-11** An appointment appears on the day it starts. One running across several days is not drawn
across them.

**MON-12** When a day holds more appointments than its cell can list, the last line reports how many
are not shown instead of an appointment. That count includes the appointment whose line it took, so
the cell never looks complete when it is not.

**MON-13** Appointment content comes from a template of its own, because a line in a month cell is a
fraction of the size of an appointment box on the timeline.

## Moving around

**MON-14** Swiping horizontally moves to the previous or next month, without limit in either
direction.

**MON-15** One swipe moves exactly one month, whatever the lengths of the two months involved.

**MON-16** Data is prepared for the months either side, so swiping to one does not show a blank or
partially drawn calendar.

**MON-16a** A host jumping to the month either side slides across to it, as NAV-11a describes for a
week.

**MON-17** Switching between a month and the timeline keeps the person on the day they were looking
at, rather than moving them to the start of the period.

## Interaction

**MON-18** Tapping empty space in a day selects that whole day and reports it. A month has no finer
target to offer, and the reported length says so.

**MON-19** No selection affordance is drawn on a month. The **+** offered on the timeline means "tap
again to create something here", and a month cell has no time to create anything at — tapping one
opens that day instead. The day is still reported as selected.

**MON-20** Tapping an appointment reports it, as on the timeline.

**MON-21** Appointments cannot be picked up and moved in a month. Rescheduling is a timeline
affordance, because a month has no time of day to drop onto.
