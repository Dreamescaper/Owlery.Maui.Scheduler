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
`ItemsSource` honours `INotifyCollectionChanged` (§9). `WorkingDays` honours only replacement today —
mutating the assigned collection changes nothing until it is assigned again. That is not a settled
decision; it stands until someone weighs it properly, and the API may change if observation turns out
to read better.

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

Today's two treatments are separate properties, not one. The cell background and the day-number
circle answer different questions — "this whole column is today" and "this is today's number" — and a
host that wants a Google-Calendar-style marker usually wants the circle without the column wash. The
old `ShowCurrentDayHighlight` bundled them, so it was renamed to `ShowCurrentDayBackground` and a
`ShowCurrentDayCircle` added; a host that wants both sets both. The circle is *filled*, not stroked,
because that is the marker people recognise, and the day number is drawn on top in
`CurrentDayTextColor` — a second colour is unavoidable, since a number cannot derive a legible
contrast from the fill it sits on. Its default is white to match the default accent fill.

The old bold on today's number is gone. It existed because weight and colour were the only way to mark
the number before there was a circle; with a filled circle behind it, the weight is noise. The timeline
header keeps one `Border` per column, hidden by an empty background, so a header built once can mark
whichever column becomes today after the weeks rotate without rebuilding the strip.

The circle is sized per surface because each draws its number differently. The timeline's is a `Border`
just larger than the 16pt number, with the header's spacing widened to make room; the agenda's day
marker does the same around its 20pt number. The month's is painted on the canvas and sits in a
day-number row widened to 24 points so the circle clears the cell's top line and the first appointment
beneath it. That row is one of the cell's lines, so on a tall window a cell now shows one fewer
appointment; on a phone, where a cell already held three, the count is unchanged. Its centre is offset
by the cell's own `y` — the day-number row is drawn at the top of every cell, and a circle that forgets
the cell's row lands on the first week instead of today's.

The month's day number is dropped by a couple of points inside its row. A font's descent sits below the
digits, so centring the font box leaves the ink above centre and the circle looks low against it; the
nudge lands the ink on the row's centre, where the circle is drawn, and reads as better spacing on a
day without a circle too.

The agenda's gutter cell is floored rather than taking its row's height. A day marker hangs beside the
first appointment of its day and was given exactly that row's rectangle, which was fine while it was
two labels — but the circle is a fixed size, so a host whose `RowHeightResolver` or template makes a
compact row could hand the marker a cell shorter than the circle it has to hold. Android clips a child
to its frame and iOS does not, so the same row would have lost the bottom of the circle on one platform
and spilled it over the next day on the other. `AgendaGeometry.MinimumDayMarkerHeight` is the floor; the
gutter is its own column, so a cell taller than its row overlaps nothing beside it.

