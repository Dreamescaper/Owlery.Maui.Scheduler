# Data and layout

What the control asks of a host, and how a day's overlapping appointments are packed into columns.

## 9. Data contract

The control does not fetch anything. It exposes:

- `ItemsSource` — a flat collection of `ISchedulerAppointment`, covering as wide a range as the host
  likes. The control picks out what belongs to the active surface. `INotifyCollectionChanged` is
  honoured; assigning a new collection instance also works, and is what the page does.
- `VisibleDatesChanged` — raised whenever the active period changes, carrying the dates the surface
  exposes plus its prefetch range. A timeline reports its day columns and three pages, a month its 42
  cells and neighbouring grids, and an agenda its whole loaded vertical range.

This is what keeps recycling free of network latency: the host is told about a range wider than the
visible period, so horizontal rotation binds data that is already in memory.

**Time zones are the host's problem, deliberately.** Every `DateTime` crossing the API is wall-clock in
`TimeZone`. The control never converts. The mobile adapter (`SessionAppointment`) converts the UTC
timestamps once, in one place. This is a direct reaction to the timezone workarounds the Syncfusion
implementation needed.

---

## 10. Overlapping appointments

`AppointmentLayoutEngine` groups a day's appointments into clusters of transitively overlapping items,
then greedily assigns each item the first column free at its start time. Width is `1 / columnCount` of
the day, so a cluster of three renders as three equal columns.

Positions are produced as **fractions** of a day column rather than pixels, so a viewport resize
re-positions views without recomputing overlaps.

Appointments shorter than 15 minutes are still given a 15-minute box so they stay tappable, and an
appointment running past midnight is clipped to the visible window of its own day.

