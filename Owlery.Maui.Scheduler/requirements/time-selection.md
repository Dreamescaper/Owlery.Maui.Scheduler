# Selecting a time

Picking an empty point in the week — the starting move for creating something.

## Selecting

**SEL-1** Tapping empty space on the grid selects a time slot at that position.

**SEL-2** The selected time is the day of the column tapped, and the time of the tap rounded **down**
to the snap interval. Tapping anywhere within a slot selects that whole slot, so the result matches
where the person's finger visibly landed.

**SEL-3** Selection never lands outside the day window, however close to the edge the tap is.

**SEL-4** The selected slot is marked on the grid so it is obvious which slot is active and how long
it is.

**SEL-5** Only one slot is selected at a time. Tapping elsewhere moves the selection.

**SEL-6** The marker stays anchored to its slot while the calendar is scrolled, and remains in place
when periods are changed and the selected slot is still on screen.

**SEL-14** When the number of days shown changes, the marker widens or narrows with the column it
marks and travels with it, rather than staying where it was while the grid moves underneath.

**SEL-7** The host can set or clear the selection itself — for example to restore a previous choice,
or to clear it after acting on it.

## Reporting

**SEL-8** Every tap on empty space is reported to the host, together with the selected slot.

**SEL-9** The view does not create anything. What a selection leads to — opening a form immediately,
requiring a confirming second tap, or nothing at all — is the host's decision.

**SEL-10** The selected slot remains available to the host after the tap, so a separate action
elsewhere in the app can act on the time the person picked.

**SEL-15** Tapping the hour gutter reports the time at that point, rounded the same way SEL-2 rounds a
tap on the grid and held inside the day window the same way SEL-3 holds one. It reports a time of day
and no date: the gutter runs alongside every day on the page at once. Nothing is selected and no
marker is drawn — the gutter names an hour, it does not pick a slot.

## Tapping an appointment

**SEL-11** Tapping an existing appointment reports that appointment to the host, and does not change
the current slot selection.

**SEL-12** A tap that lands on an appointment is never also treated as a tap on the grid beneath it.

**SEL-13** A gesture that moves before it is released is a scroll, not a tap, and is not reported as
one.
