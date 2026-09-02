# Rescheduling

Moving an appointment to a different time by dragging it.

## Picking an appointment up

**DND-1** Pressing and holding an appointment picks it up. A brief hold is required; a tap does not
move anything.

**DND-2** Scrolling the calendar never picks an appointment up, in any direction, however the gesture
starts. Moving the finger before the hold is complete always scrolls.

**DND-3** The person is told an appointment has been picked up before they move it: the appointment
changes appearance, and the host is notified in time to add haptic feedback where the device supports
it.

**DND-4** The host can refuse a pick-up for a particular appointment. Refusal is silent: the gesture
continues as an ordinary scroll, and the appointment does not move.

**DND-5** Rescheduling can be switched off entirely, in which case appointments never lift and no
attempt is reported.

## Dragging

**DND-6** Once picked up, the appointment follows the finger.

**DND-7** The appointment can be moved to a different time, a different day, or both, within the
visible week.

**DND-8** While dragging, the appointment shows at the time it would take if released now, rounded to
the **nearest** snap interval — not where the finger literally is. What is on screen is a preview of
the result.

**DND-9** The proposed new time is displayed as text during the drag, so the person is not left to
read it off the grid.

**DND-36** That text appears in the hour gutter, level with the time the appointment would start at.
It is never placed next to the appointment being dragged, where the hand doing the dragging covers it.

**DND-37** An hour label is hidden only when the indicator covers enough of it to leave it unreadable.
A label the indicator merely touches stays, so the gutter does not appear to lose entries at random.

**DND-38** The readout can be switched off without affecting rescheduling itself.

**DND-10** The appointment keeps its duration throughout. Dragging moves it; it never resizes.

**DND-11** An appointment cannot be dragged outside the day window, and cannot be positioned so that
part of it would fall outside it.

**DND-12** Moving a dragged appointment never scrolls the calendar as a side effect: the grid stays
still under it, except where it is deliberately held against an edge.

**DND-13** The appointment being dragged is drawn above everything else on the grid.

## Moving to another week

**DND-24** While an appointment is being dragged, a faded copy stays in the place it came from, so
the slot it is leaving remains visible.

**DND-25** Holding a dragged appointment against the leading or trailing edge of the week moves the
calendar to the adjacent week.

**DND-26** The move happens after a short pause at the edge, not on contact. The edges are exactly
where someone drags to reach the first and last day of the week, and paging immediately would make
those two columns unusable.

**DND-27** Continuing to hold at the edge keeps moving through consecutive weeks.

**DND-34** The calendar slides to the new week rather than changing contents in place, so the change
is as visible as it is when swiping between weeks by hand.

**DND-39** While that slide is running the appointment does not jump to a new day or time. The day and
time it would take are settled once the calendar has come to rest on the new period.

**DND-35** The appointment being dragged stays under the finger while the weeks slide behind it, with
no visible lag or drift.

**DND-28** Moving away from the edge stops it, and the calendar stays on the week it reached.

**DND-29** The faded copy belongs to the week it came from: it travels with that week and leaves the
screen once the drag has moved past it.

**DND-30** Releasing after the week has changed schedules the appointment into the week now on
screen, at the day and time it was released on.

**DND-31** The change of week is reported as it happens, so a title tracking the visible week and any
data loading keep up with the drag.

**DND-32** Moving between weeks this way can be switched off independently of rescheduling itself,
leaving appointments movable only within the week they are already in.

**DND-33** The faded copy disappears as soon as the finger lifts, whatever the outcome.

## Reaching hours that are off screen

**DND-40** Holding a dragged appointment against the top or bottom of the timeline scrolls it, so a
time that is off screen can be reached without putting the appointment down first.

**DND-41** This begins as soon as the edge is reached, without the pause that changing period
requires. Dragging towards an hour that is off screen means only one thing, whereas the first and last
columns are somewhere a person legitimately wants to drop.

**DND-42** The appointment stays under the finger while the hours move beneath it, so the time it
would take follows the scrolling rather than the finger having to chase it.

**DND-43** Scrolling stops at the first and last hour of the day; there is nothing beyond them to
reach.

**DND-44** Moving away from the edge stops it, and the timeline stays where it got to.

## Dropping

**DND-14** Releasing requests the move, reporting the appointment and the new start time to the host.

**DND-15** The new time is always a snap-interval boundary.

**DND-15a** Each time the preview comes to rest on a different boundary, the host is told, so it can
mark the step in whatever way suits the platform — a haptic tick, most obviously. It is told once per
boundary, not once per movement, and not when the appointment is first picked up.

**DND-16** Releasing an appointment on the time it already occupied results in no change.

**DND-17** The host can reject a drop, in which case the appointment returns to where it was.

**DND-18** On acceptance the appointment stays where it was dropped while the host does its work. It
does not snap back to the old time and then jump forward again.

**DND-19** If the host's work fails, the appointment returns to its original time.

**DND-20** A drag interrupted before release — by a call, by the app being backgrounded — leaves the
appointment unchanged and requests nothing.

## What is not supported

**DND-22** Changing duration by dragging an edge.

**DND-23** Dragging more than one appointment at once.
