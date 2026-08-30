# Scheduler — functional requirements

What the scheduler does, described as behaviour. No implementation detail: how any of it is achieved
is in [../DESIGN.md](../DESIGN.md), and the properties and events that expose it are in
[../API.md](../API.md).

## Purpose

A calendar surface that shows either a run of days as a vertical timeline — a week, three days or one
— or a whole month at a glance, so someone can see how their time is laid out, find a free slot, and
move things around. The shape of interaction people already know from Google Calendar and the iOS
Calendar.

## Documents

| Area | Requirements |
|---|---|
| [Navigation and the time axis](navigation.md) | `NAV-*` — which week is shown, moving between weeks, the timeline, today |
| [Month view](month.md) | `MON-*` — the month grid, what a cell holds, moving between months |
| [Appointments](appointments.md) | `APT-*` — how existing items are placed and drawn |
| [Selecting a time](time-selection.md) | `SEL-*` — picking an empty slot |
| [Rescheduling](rescheduling.md) | `DND-*` — moving an appointment by dragging |
| [Data and state](data-and-state.md) | `DAT-*` — what must be loaded and when, busy and empty states |
| [Accessibility](accessibility.md) | `ACC-*` — what assistive technology can reach |

## Who does what

The scheduler **displays and reports**. It does not own the data and never changes it.

- The **host app** supplies appointments, decides what a tap means, performs any change, and reports
  the result back.
- The **scheduler** decides what is visible, where things sit on the grid, and how gestures are
  interpreted, then tells the host what the person did.

This split matters for reading these requirements: where one says "the view reports", the visible
outcome depends on the host acting on it.

## Vocabulary

| Term | Meaning |
|---|---|
| **Appointment** | One item occupying a span of time on the grid. |
| **Slot** | A stretch of time on the grid a person can select. Its length is the slot length below, and a tap anywhere inside one selects the whole of it. |
| **Slot length** | How long a selectable slot is. 15 minutes by default. |
| **Snap interval** | The granularity a *dragged* appointment lands on, rounded to the nearest boundary. 15 minutes by default, and independent of the slot length — the two answer different questions. |
| **Page** | What is currently on screen and moves as a unit — a week, three days, one day, or a month. |
| **Day window** | The range of hours shown vertically, 08:00–23:00 by default. |
| **Working time** | Recurring working days and one same-day working interval, used only for optional shading. |

## Where the build does not yet meet these

Requirements state what is intended. These are the known gaps as things stand:

| Requirement | Gap |
|---|---|
| ACC-10 — comfortable touch targets | A short appointment sharing its column with two or three others can end up narrower than the recommended minimum target. |
| ACC-11 — rescheduling without dragging | No equivalent exists in the view; the host must provide one. |
| MON-17 — switching keeps the day | Holds, but the switch is a hard cut. The two layouts do not grow into one another the way NAV-28 describes for a change of day count. |

## Out of scope

Deliberately not provided, and not planned as part of this component:

- Agenda and multi-week views. Day, three-day, week and month views are supported; see `NAV-1a` and
  `MON-1`.
- All-day appointments, and appointments spanning more than one day.
- Changing an appointment's duration by dragging its edges.
- Creating, editing or deleting appointments — the view reports intent; the host acts.
- Recurrence, reminders, invitations, availability or free/busy lookup.
