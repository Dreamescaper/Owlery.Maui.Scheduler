# Accessibility

What assistive technology can reach, and the reasoning behind what is deliberately left out.

**ACC-1** Every appointment on screen is exposed to screen readers as its own element.

**ACC-2** An appointment is announced with enough to identify it without looking: what it is, which
day, and its start and end times.

**ACC-3** Day headers are readable by screen readers, so the structure of the week can be established.

**ACC-4** The selected time slot is exposed as an element and announced with its day and time.

**ACC-5** The empty grid is **not** exposed cell by cell. A week of slots is several
hundred cells; presenting them individually would produce a list nobody can navigate and would make
the calendar harder to use with a screen reader, not easier.

**ACC-6** Because of ACC-5, the host must offer a way to create an appointment that does not depend on
tapping a cell — a button or menu action reachable by assistive technology. Selecting a time by
touching the grid is a convenience for sighted, pointing users, never the only route.

**ACC-7** Grid lines, shading and hour labels are decorative and are not announced. The same
information is available from the appointments themselves and from the day headers.

**ACC-8** Colour is never the only way information is conveyed. When today's highlight is enabled,
its day number is emphasised as well as its background tint; any meaning the host encodes in an
appointment's colour must also be present in its text or icons.

**ACC-9** Text sizes respect the person's system text-size preference.

**ACC-10** Touch targets for appointments are large enough to hit comfortably, including for
appointments that are short in duration or narrowed by overlapping.

## Known limitation

**ACC-11** Rescheduling by dragging has no assistive-technology equivalent. A screen-reader user cannot
currently move an appointment from the calendar; the host must provide another way to change a time —
for example an edit form on the appointment's own detail screen.
