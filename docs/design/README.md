# Scheduler — design decisions

Why the control is put together the way it is. Each section states the decision, what it was chosen
over, and what it costs. For the behaviour itself, see the
[functional requirements](../requirements/README.md); for the properties and events that expose it,
see [API.md](../API.md).

The sections keep the numbers they were written with, so an older reference to "DESIGN.md section 19"
still resolves. They are grouped by subject rather than renumbered.

## Documents

| Area | Sections |
|---|---|
| [Project boundary](project-boundary.md) | §1 — a separate library, and why no Blazor is in it |
| [Paging](paging.md) | §2–4 — the three-slot ring buffer, detecting the end of a swipe, rotating positions |
| [Appointment views and the pool](appointment-views.md) | §5–6 — one shared layer, pooling, reconciliation, identity, one template |
| [The drawn grid, and where scrolling lives](grid-and-scrolling.md) | §7–8, §22 — the canvas instead of cell views, the vertical scroll outside the pager, re-asking for an offset the platform clamped |
| [Data and layout](data-and-layout.md) | §9–10 — the data contract, overlap packing |
| [Interaction](interaction.md) | §11–12 — long-press-to-drag, edge paging, cell and chrome taps |
| [Accessibility](accessibility.md) | §13 — what a screen reader can reach, and what it deliberately cannot |
| [The page surface seam](surface-seam.md) | §17 — what the three surfaces share and where they part company |
| [The timeline surface](timeline.md) | §16 — fewer than seven days, and animating the change |
| [The month surface](month.md) | §18 — six rows, overflow, chrome, the height that depends on the viewport |
| [The agenda surface](agenda.md) | §21 — the measured, virtualized vertical list |
| [The pager](pager.md) | §19 — our own scroll view, and what each platform took |
| [Appearance](appearance.md) | §20 — semantic state, bindable colours |
| [Verification status](verification.md) | §15 — what has been checked, where, and what has not |

Section 14 is below: it is the list of things deliberately absent, which belongs with the index of
what is present.

## 14. Not implemented

The following are absent by design, not by oversight:

- **Multi-week grid view.** Day, three-day, week, month and agenda surfaces are supported; a grid
  spanning an arbitrary number of weeks is not.
- **All-day / multi-day appointments.** There is no all-day row; an appointment is clipped to its
  starting day.
- **Resizing an appointment by dragging its edges.**
- **Releasing template roots.** Bounded by BlazorBindings, not by this control — see
  [section 6](appointment-views.md).

