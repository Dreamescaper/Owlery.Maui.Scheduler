# 20. Appearance is semantic, bindable state

The control exposes the states a scheduler understands — current day, non-working day and
non-working hours — separately from the colours used to paint them. A callback returning a colour
for an arbitrary day or hour was rejected: it mixes calendar policy with presentation, leaves its
invocation granularity and precedence undefined, is awkward in XAML, and gives the control no signal
when data captured by the callback changes.

Working time is deliberately the small recurring case: a collection of working weekdays and one
same-day `TimeOnly` interval. It covers the visual distinction the control owns without turning the
control into a holiday calendar or availability engine. Date exceptions and overnight intervals can
be added when a concrete host needs their semantics rather than guessed here.

A `TimeOnly` property cannot be set from XAML on its own. MAUI's XAML loader does not consult the
converter the framework registers for the type, and fails the load with "mismatching type between
value and property" — so `WorkingHoursStart` and `WorkingHoursEnd` carry a `TypeConverterAttribute`
pointing at a converter of our own. It parses with the invariant culture: markup is not user input,
and a page that read differently on a device with another locale would be a trap. `WorkingDays` needs
nothing, because `x:Array` produces a `DayOfWeek[]`, which is already an `IReadOnlyCollection<DayOfWeek>`.

MAUI's own `TimeSpanTypeConverter` does not help, and not merely because it is unregistered for our
properties: it *always* returns a `TimeSpan`. It parses `TimeOnly`-shaped text, then hands back
`timeOnly.ToTimeSpan()`, because it exists to feed `TimeSpan` properties such as `TimePicker.Time`.
Something that cannot produce a `TimeOnly` can never satisfy a `TimeOnly` property, whatever the
lookup does — and it is `internal`, so a library outside MAUI could not name it anyway.

So the exit condition is narrow: delete ours when MAUI gains a public converter that yields a
`TimeOnly`, and reaches it from a `TimeOnly` property without an attribute. No shipped version does —
not 10.0.20, not 10.0.90, not 11.0 preview. An explicit attribute wins over any later built-in
lookup, so this stays correct in the meantime; it only stops being necessary.

**Open for discussion:** whether an in-place change to `WorkingDays` should be observed, the way
`ItemsSource` honours `INotifyCollectionChanged`. Only replacement is honoured today. That is not a
settled decision — it stands until someone weighs it properly, and the API may change if observation
turns out to read better.

Colours are individual bindable properties rather than a nested palette. That lets ordinary MAUI
styles and `AppThemeBinding` provide themes, keeps each value discoverable, and avoids subscribing to
changes inside another mutable object. `GridBackgroundColor` stays separate from the inherited
`BackgroundColor` because it is not decoration: the drawing surface and the gutter must stay opaque
to receive input, so it defaults to white rather than to the container's colour.

The marks inside the hour are one integer, `MinorGridLineMinutes`, rather than a flag beside an
interval. A flag and an interval can disagree — "subdivisions off, every fifteen minutes" has to mean
something — and the disagreement is the sort a host only discovers on screen. Sixty is the natural way
to spell "none": at that spacing every mark lands on an hour line that is already drawn, so nothing is
added and no sentinel value is needed. Zero and negatives fall out the same way, and are tolerated
rather than thrown on, as reversed working hours are.

It is deliberately independent of the snap intervals. What the grid is ruled into and what a drag lands on
are different questions: a host may want quarter-hour precision without quarter-hour clutter, and the
two have no reason to move together.

All of these properties take a paint-only path: update the two drawables and the handful of native
labels or borders, then invalidate the canvas. They never repopulate a page or rebind an appointment.
The draw order is explicit. A current-day fill wins over a non-working-day fill, while out-of-hours
bands on a working day are painted over the current-day fill so both meanings remain visible.

