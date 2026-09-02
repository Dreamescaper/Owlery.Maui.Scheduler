# Data and layout

What the control asks of a host, and how a day's overlapping appointments are packed into columns.

## 9. Data contract

The control does not fetch anything. It exposes:

- `ItemsSource` — a flat collection of `ISchedulerAppointment`, covering as wide a range as the host
  likes. The control picks out what belongs to the active surface. `INotifyCollectionChanged` is
  honoured, so the host can keep one collection and change it in place; assigning a new collection
  instance also works, and is what the page does today. The control filters the visible period itself,
  so the host keeps the whole loaded range and never trims it for periods that scrolled off screen.
  A bulk change is best made through a range operation — `SchedulerAppointmentCollection<T>` adds
  them, because the framework's `ObservableCollection` deliberately does not
  (<https://github.com/dotnet/runtime/issues/18087>) — so a month of appointments changed costs one
  notification instead of one per item. The control treats any change notification as "re-read the
  collection and repopulate the window", coalescing bursts into one rebuild, so a range method's
  single event is all it needs.

  Those range events carry their payload — `Add`, `Remove` or `Replace` with the items and the index
  — even though the control does not read it. The collection is public, so it is not only the
  scheduler's consumer: the same instance may be bound to a list elsewhere, and a notification that
  says what changed is worth more there than one that says only "something did". Where a single
  notification cannot describe the change faithfully the action is `Reset`, and there are exactly two
  such cases, both a limit of `NotifyCollectionChangedEventArgs` rather than a choice: a multi-item
  `Add`/`Remove` states one starting index for a run, so a scattered removal cannot be expressed, and
  neither can a replacement of a different length than the range it replaces, which is a removal and
  an addition at once. Reporting `Reset` there is the honest answer; a payload with an index the
  items were never at is not.

  Per-item incremental *rendering* remains out of scope, and the payload does not open it up.
  Appointments in a day are packed into overlap clusters (§10), so one added item changes the width
  and position of everything it overlaps, transitively — and the agenda is a measured row table where
  an inserted row shifts every row below it. There is no such thing as redrawing only the item that
  arrived. What would look like the saving is already taken: `PopulateSlot` reconciles by
  `ISchedulerAppointment.Key`, reusing views rather than rebuilding them, and rebinding writes only
  the values that actually differ, so an untouched appointment costs a lookup and two comparisons.
  Deriving the delta from identity rather than trusting an event's payload is also what lets it
  survive a host rebuilding its collection mid-gesture.

  The subscription lasts as long as the view is loaded, not as long as the property is set: it is
  dropped on `Unloaded` and taken again on `Loaded`. Telling hosts to keep one long-lived collection
  means the collection routinely outlives the view drawing it, and a subscription is a reference from
  the collection back to the view — so a view that never let go would be held alive by data it no
  longer draws, for as long as the host holds it. Nothing is lost by letting go: the collection is
  read afresh on every repopulate, so a change made while the view was away arrives with the rebuild
  that follows its reload.
- `ISchedulerAppointment` — implemented by the host's own type, and expected to be immutable. The
  control observes the collection, not its items: no member of the contract is a bindable property,
  nothing in it derives from `BindableObject`, and it does not require `INotifyPropertyChanged`. That
  is deliberate. Watching every appointment would mean a subscription per item — thousands of them at
  the range hosts are told to keep loaded — to catch a change the control already learns about from
  the collection, and it would put the host's model on a second notification path whose ordering
  against the first nobody would want to reason about. The cost of the choice is that a host mutating
  an appointment in place sees nothing happen, and then sees a repaint later when something unrelated
  reloads the window; the contract's documentation says so plainly, and a `record` with `with` makes
  the alternative a single line. Identity is `Key`, so a replaced instance lands on the view already
  showing it (§6).
- **Time zones.** `TimeZone` is the zone the calendar is displayed in: the axis appointments are
  placed on, and the clock behind *today* and the current-time line. It is not a claim about what the
  host's data means — that is the appointment's own business, and it says so two ways. `DateTimeKind`
  decides first: `Utc` and `Local` already carry a complete instant, so they are converted and the
  appointment's own zone is ignored beside them. `Unspecified` carries no instant, so
  `ISchedulerAppointment.TimeZone` speaks: `null` means floating and the time is drawn exactly as
  given, and a zone means the time is wall-clock there and is converted. `AppointmentTime.In` is the
  only place any of this is decided.

  The appointment's zone says what its times *mean*, never where to draw them. The grid has one time
  axis: if a per-item zone moved placement, an itinerary departing 14:00 Tokyo and arriving 18:00
  London would draw the departure four hours before the arrival on a London grid, when it is seven.
  Placement is always by resolved position. A host wanting to label an appointment in its own zone
  renders that in its template, which binds to the host's own type and can already say anything.

  Two readings have no honest answer and are given one anyway rather than throwing: a reading its
  zone skipped is moved forward by the gap, and a reading that happens twice is taken as its first
  occurrence. Throwing inside a layout pass over tens of thousands of items is not an option, and the
  same rule resolves `SchedulerMoment.ToDateTimeUtc`, so the question is answered identically from
  either direction.

  The floating and same-zone short-circuits are a design point, not an optimisation to be tidied away.
  §15 has the numbers: reading appointments as instants costs about 4× the layout, and as wall-clock
  in another zone about 16×, per slot, three slots per rebuild, over a range hosts are told to keep
  wholly loaded.

  There is no DST-aware geometry: every day is a uniform `StartHour`–`EndHour` band. Four things
  follow, all confined to two days a year in the view's zone, and all accepted. On the fall-back day
  01:00–02:00 occurs twice and is drawn once, so two appointments an hour apart stack. On the
  spring-forward day the grid draws an hour that does not exist, so a tap there names a reading its
  zone never had — hence the resolution rule above. An appointment spanning a transition is drawn an
  hour short or long. And one unusable band appears in spring. Making the geometry DST-aware is
  additive to all of this if it ever earns its place.
- **What the control reports back** is `SchedulerMoment`: a wall-clock reading plus the zone the
  calendar keeps. A fixed representation could not serve both kinds of host — wall-clock taxes a host
  whose backend stores instants, an instant taxes a host that never wanted to know what a zone is, and
  the control cannot infer which it is talking to, because a tap on empty space has no appointment to
  take a hint from. Carrying both and letting the caller ask needs no configuration and has no default
  that is wrong for half of them. Deliberately no implicit conversion to `DateTime`: the type exists
  so that "which of the two is this?" cannot be answered by accident.
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

