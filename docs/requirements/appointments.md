# Appointments

How existing items are placed and presented on the grid.

## Placement

**APT-1** An appointment appears in the column for its day, with its top edge at its start time and
its bottom edge at its end time, so its position and length can be read directly off the timeline.

**APT-2** An appointment shorter than a slot is still drawn large enough to be seen and
comfortably tapped.

**APT-3** An appointment that starts before the day window opens, or ends after it closes, is trimmed
to the visible hours. Its remaining portion stays in the correct position.

**APT-4** An appointment continuing past midnight is shown only on the day it starts.

**APT-5** Appointments outside the visible week are not drawn.

## Overlapping appointments

**APT-6** Appointments that overlap in time on the same day are shown side by side within that day's
column. None is hidden behind another.

**APT-7** Overlapping appointments share the width of the day column equally.

**APT-8** Appointments that do not overlap each use the full width of the column, even when other
appointments elsewhere on the same day are sharing.

**APT-9** A group of overlapping appointments is laid out independently of the rest of the day: a
crowded morning does not narrow an uncontested afternoon.

## Appearance

**APT-10** The appearance of an appointment — colour, text, icons, layout — is supplied by the host
app, not fixed by the view. This is what allows different kinds of item to be told apart at a glance:
cancelled, unpaid, imported from elsewhere, and so on.

**APT-11** The view positions and sizes appointments; it does not alter what the host draws inside
them.

**APT-12** When an appointment's details change, its appearance updates in place without the person
losing their scroll position or the calendar flickering.

**APT-13** Appointments are drawn above the grid and below anything being dragged.

## Reacting to changes

**APT-14** When the host supplies a different set of appointments, the calendar reflects it without
requiring the person to navigate away and back.

**APT-15** Adding, removing or moving appointments does not disturb the visible week or the scroll
position.
