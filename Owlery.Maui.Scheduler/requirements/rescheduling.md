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

**DND-10** The appointment keeps its duration throughout. Dragging moves it; it never resizes.

**DND-11** An appointment cannot be dragged outside the day window, and cannot be positioned so that
part of it would fall outside it.

**DND-12** Neither the calendar nor the timeline scrolls while an appointment is being dragged, so the
grid stays still under it.

**DND-13** The appointment being dragged is drawn above everything else on the grid.

## Dropping

**DND-14** Releasing requests the move, reporting the appointment and the new start time to the host.

**DND-15** The new time is always a snap-interval boundary.

**DND-16** Releasing an appointment on the time it already occupied results in no change.

**DND-17** The host can reject a drop, in which case the appointment returns to where it was.

**DND-18** On acceptance the appointment stays where it was dropped while the host does its work. It
does not snap back to the old time and then jump forward again.

**DND-19** If the host's work fails, the appointment returns to its original time.

**DND-20** A drag interrupted before release — by a call, by the app being backgrounded — leaves the
appointment unchanged and requests nothing.

## What is not supported

**DND-21** Dragging into a week other than the visible one.

**DND-22** Changing duration by dragging an edge.

**DND-23** Dragging more than one appointment at once.
