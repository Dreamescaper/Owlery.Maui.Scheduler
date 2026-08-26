# Navigation and the time axis

How the week view decides what is on screen, and how someone moves around it.

## The visible week

**NAV-1** The view shows exactly seven consecutive days, side by side, one column each.

**NAV-2** Which day the week starts on is configurable. Monday is the default.

**NAV-3** Each day column is headed by an abbreviated day name and the day number, both in the
person's language and regional format.

**NAV-4** Day headers stay in place while the timeline is scrolled vertically. They move with the days
when weeks are changed.

## Moving between weeks

**NAV-5** Swiping horizontally moves to the previous or next week. There is no limit in either
direction — any week, past or future, can be reached by continuing to swipe.

**NAV-6** One swipe moves exactly one week.

**NAV-7** The view commits to a week when the finger is lifted, not after the movement has come to
rest. A flick is enough; the person does not have to wait for the calendar to stop moving to find out
which week they landed on.

**NAV-8** A drag that is released without travelling far enough returns to the week it started on.

**NAV-9** Weeks that have not been swiped to are still prepared, so moving to an adjacent week does
not show a blank or partially drawn calendar.

**NAV-10** The view reports the week currently shown, so the host can keep a title or date indicator
in step with it.

**NAV-11** The host can jump to any date. The view then shows the week containing that date. Jumping
to a date inside the week already shown changes nothing.

## The vertical timeline

**NAV-12** Time runs from top to bottom over a configurable range of hours — 08:00 to 23:00 by
default. Times outside that range are not shown.

**NAV-13** Hour boundaries are marked, with a lighter mark at each half hour, so a position can be
read off the grid without counting.

**NAV-14** Hour labels run down a fixed column on the leading edge and stay aligned with the grid as
it scrolls.

**NAV-15** Hour labels use the person's regional time format, 12- or 24-hour.

**NAV-16** The timeline scrolls vertically as one surface across all seven days: the days cannot get
out of vertical alignment with each other.

**NAV-17** Changing week does not change the vertical position. Somebody looking at 14:00 who swipes
to next week is still looking at 14:00.

**NAV-18** On opening, the view scrolls to shortly before the current time rather than to the top of
the day, so what is relevant now is on screen. If the current time falls before the day window, the
view opens at the start of it.

## Today and the current time

**NAV-19** Today's column is visually distinguished from the rest of the week whenever it is on
screen, and its day number is emphasised in the header.

**NAV-20** A line across today's column marks the current time.

**NAV-21** The current-time line keeps up as time passes, without the person interacting with the
calendar.

**NAV-22** The current-time line is not drawn when the current time falls outside the day window.

**NAV-23** When the date changes while the calendar is open, both the highlighted column and the
emphasised day number move to the new day.

**NAV-24** Saturday and Sunday are shaded differently from weekdays, so the shape of the week is
readable at a glance.

## Time zone

**NAV-25** The whole calendar — appointment positions, today, the current-time line — is drawn in one
declared time zone, which need not be the device's own.

**NAV-26** The time zone in use is indicated on screen, so a person working in a zone other than their
device's is not misled about what the times mean.
