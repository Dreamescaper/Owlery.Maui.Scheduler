# Plan — a real time-zone model: `DateTimeKind`, a per-appointment zone, and a moment type on events

Status: **implemented.** The decisions are recorded in `docs/design/data-and-layout.md` (§9),
`docs/API.md`, `docs/requirements/data-and-state.md` (DAT-16 rewritten, DAT-22 and DAT-23 added),
`docs/design/verification.md` (§15) and the invariants in `AGENTS.md`. Those are the documents to read
and to keep current — this file is the record of how the shape was arrived at, and nothing should cite
it.

> Drafted and implemented: 2026-09-03.

## Goal

Replace "the control performs no time-zone conversion" with a model that says what it *does* do, and
make the two host archetypes both work without either of them converting at every boundary:

- **The floating host.** Does not care about zones. Supplies `2:00` and expects the grid to draw
  `2:00` and hand `2:00` back. Works today, and must keep working with no code changes and no new
  concepts on screen.
- **The instant host.** Backend stores UTC, endpoint returns UTC, `TimeZone` is expected to be
  presentation only. Supplies UTC and expects UTC back.

Neither should have to know which one the API was designed for.

## Non-goals

- **DST-aware geometry.** Days stay a uniform `StartHour`→`EndHour` band. The consequences are
  enumerated under *Known limitations* and accepted, not hidden.
- **All-day and multi-day appointments.** Already out of scope (`docs/API.md:679`,
  `docs/design/README.md:39`). Worth noting because all-day is the case where a UTC instant is
  actively wrong — a birthday is not an instant, and converting one moves it across midnight for
  some viewer. If all-day is ever added, this model needs revisiting.
- **Recurrence.** Not supported, and the reason wall-clock+zone storage matters is mostly recurrence.
- **Making the control a time library.** It renders. It does not persist, and it must not legislate
  how a host stores its data.
- **Per-appointment *display* zones.** See design decision 3 — they cannot work on one time axis.

## Current state (verified in the tree)

- `TimeZone` is `BindableProperty` of `TimeZoneInfo`, default `TimeZoneInfo.Local`
  (`SchedulerView.Properties.cs:81`), documented as "the zone every `ISchedulerAppointment` value is
  expressed in" (`:317`).
- It is wired to `OnGeometryChanged` → `ApplyGeometry()` (`SchedulerView.cs:535`, `:632`), which ends
  in `RebuildAll(page)` (`:689`). **A `TimeZone` change already rebuilds everything.** No new
  invalidation wiring is needed.
- `TimeZone` currently feeds exactly two things:
  - `NowInZone()` (`SchedulerView.cs:501`) → `PageGeometry.Now` (`Internal/PageGeometry.cs:34`),
    which decides which day is *today* and where the current-time line sits.
  - `TimeZoneAbbreviation()` (`SchedulerView.cs:1171`) → the header corner label.
- No conversion of appointment times happens anywhere. `GetUtcOffset` appears once, for the label.
- Appointment `Start`/`End` are read raw in five places: `AppointmentLayoutEngine.Layout`
  (`:37`, `:49`), `MonthLayoutEngine` (`:67`), `AgendaLayoutEngine` (`:270`, `:287`),
  `SetAppointmentSemantics` (`SchedulerView.Rendering.cs:476`) and `DescribesTheSame` (`:469`).
- Outbound values are wall-clock with `Kind = Unspecified` **by accident of arithmetic**, not by
  design: `DropTargetResolver.cs:51` builds `pageStart.AddDays(n).ToDateTime(...).AddMinutes(snapped)`.
- The statements this plan makes false:
  - `AGENTS.md:130` — "Every `DateTime` crossing the public API is wall-clock in `TimeZone`. The
    control performs no time-zone conversion. Do not add any."
  - `docs/requirements/data-and-state.md:66` — DAT-16, "The view performs no time-zone conversion."
  - `SchedulerModel.cs:64` — "The control never converts between time zones."

## Design decisions

### 1. Inbound: `DateTimeKind` says what a value means; an optional zone speaks when `Kind` doesn't

`ISchedulerAppointment` gains one member, as a **default interface member** so no existing
implementor breaks:

```csharp
TimeZoneInfo? TimeZone => null;
```

With `D` = the scheduler's `TimeZone`, `A` = the appointment's:

| `Kind` | Zone | Means | Placed at |
|---|---|---|---|
| `Utc` | — | an instant | `ConvertTimeFromUtc(dt, D)` |
| `Utc` | set | an instant; zone adds nothing | same — **zone ignored** |
| `Unspecified` | — | floating | `dt` as-is, **no conversion** |
| `Unspecified` | set | wall-clock in `A` | `ConvertTime(dt, A, D)` |
| `Local` | — | an instant on this device | `ConvertTime(dt, TimeZoneInfo.Local, D)` |
| `Local` | set | contradiction | **zone ignored**, treated as a device instant |

The two ignoring cells are deliberate: `Utc` and `Local` already carry a complete instant, so a zone
beside them is redundant or contradictory. Ignoring beats guessing, but must be *documented* as
ignored rather than left to be discovered.

The floating row is the important one. It is the current behaviour, it is the zero-configuration
case, and it costs no conversion at all.

### 2. Outbound: one value type carrying the moment, with named accessors

A fixed outbound convention cannot serve both archetypes — wall-clock out taxes the UTC host, UTC out
taxes the floating host, and the control cannot infer which it is talking to (a cell tap on empty
space has no appointment to take a hint from). A type that carries the moment and lets the caller ask
for the representation it wants settles it with no configuration and no default that is wrong for
half the users.

Every outbound `DateTime` becomes it: `SchedulerTimeSlot.Start`, both `DropStart`s,
`SchedulerHeaderTappedEventArgs.Date`, and `VisibleDatesChanged`'s `VisibleDates` / `PrefetchFrom` /
`PrefetchTo`.

Shape, subject to the open questions below:

```csharp
public readonly struct SchedulerMoment
{
    public DateTime WallClock { get; }        // in the view's zone; Kind = Unspecified
    public TimeZoneInfo Zone { get; }         // the view's zone, so the caller can resolve

    public DateTime ToDateTimeUtc();
    public DateTime ToDateTimeLocal();        // device zone
}
```

This is the cheap side of the line the library already sits on: `SchedulerTimeSlot` is a type the
control *hands out* and nobody implements. A type the control *demands you implement* — a custom time
type on `ISchedulerAppointment.Start` — is a different proposition and is rejected below.

**No implicit conversion to `DateTime`.** The type's whole value is that "which representation is
this?" cannot be answered by accident. An implicit conversion means every host writes
`DateTime start = e.DropStart;`, it compiles, and the ambiguity is back — silently, behind a type that
looked like it had solved it.

### 3. The appointment zone is an input meaning, never a display hint

The grid has one time axis, in `D`. If a per-item zone affected *placement*, an itinerary with a
flight departing 14:00 Tokyo and arriving 18:00 London would draw the departure four hours before the
arrival on a London grid, when it is really seven. Placement must be by instant.

So `A` says what the appointment's own `DateTime`s mean. It does not say where to draw them. A host
wanting itinerary-style labels renders them itself: the template's binding context is the host's own
appointment type, so it can already show "14:00 JST" from whatever fields it likes with no API
change. The only thing the control generates that would want `A` is the accessibility description in
`SetAppointmentSemantics`.

**Consequence worth being honest about:** adding `TimeZoneInfo?` to the contract is a *convenience*,
not a capability. A host can already convert its TZID-form times to UTC in one line before handing
them over. It goes into a public contract that cannot easily be taken back.

### 4. Inbound stays `DateTime`; the moment type is outbound only

`Kind` plus the optional zone already encodes the meaning. Changing `ISchedulerAppointment.Start`'s
type would make every implementor edit their model to gain nothing, and an implicit conversion cannot
rescue them: an implicit operator is `static`, so it has no access to the scheduler instance and
therefore cannot resolve a floating `Unspecified` value, which is the common case.

The asymmetry is justified — inbound must accept what hosts already store; outbound is the control's
own product and can afford to be richer.

### 5. Invalid and ambiguous times are normalized, never thrown

`Unspecified` + zone is the one cell a host can fill with nonsense: `2026-03-08 02:30`
`America/New_York` never happens, and `2026-11-01 01:30` happens twice. `TimeZoneInfo.ConvertTime`
throws on the first and silently picks standard time on the second. Throwing inside a layout pass
over sixty thousand items is not acceptable.

- **Invalid** (inside a spring-forward gap): shift forward by the gap. `02:30` → `03:30`.
- **Ambiguous** (inside a fall-back overlap): take the first occurrence, i.e. the daylight one.
- Neither throws, and the same policy applies to outbound resolution in `ToDateTimeUtc`.

### 6. The fast path is load-bearing, not a special case

`docs/design/verification.md` measured the layout at 0.306 ms for 60,000 items on the timeline. A
`TimeZoneInfo.ConvertTime` per appointment, per slot, three slots per repopulate, over a collection
hosts are now told to keep entirely loaded, is a different order of cost.

Skip conversion entirely when the appointment is floating (`Unspecified`, no zone) or when its
resolved source zone equals `D`. Both reduce to today's arithmetic. This must be written down as a
deliberate design point so nobody removes it as an ugly special case, and the measurement must be
repeated after implementation.

### 7. `TimeZone` keeps its name and gains a real job

It stops being "the zone every value is expressed in" — that is now per-appointment — and becomes
**the zone the calendar is displayed in**: the axis every appointment is placed on, plus the clock
that decides today and the current-time line. `ApplyGeometry` already rebuilds on change (see
*Current state*), so no new wiring.

### 8. `DisplayDate` does not participate

It names which day to show, not an instant. Its default is `DateTime.Today`, which is `Kind = Local`;
converting it would silently open the control on the wrong day whenever `TimeZone` is not the device
zone. It stays a plain date.

## Known limitations (to be documented, not fixed)

Accepted consequences of no DST-aware geometry, all confined to two days a year in `D`:

1. **Fall-back day.** `01:00`–`02:00` occurs twice; the grid draws it once. Two appointments an hour
   apart in real time both land at `01:30` and overlap-stack. Real and visible.
2. **A tap or drop inside a spring-forward gap** reports a wall-clock that does not exist in `D`.
   `ToDateTimeUtc` applies the decision-5 normalization rather than throwing, so the host gets a
   usable instant an hour from where the finger was.
3. **Duration across a transition.** A `01:00`–`04:00` appointment on fall-back night is four real
   hours drawn as three. Box height is off.
4. **An empty, unusable hour band** on spring-forward day. Cosmetic.

Also: UTC in means the control cannot help with tzdb drift on a future booking — the intent was
"09:00 local", the instant was derived, and rule changes move it (Lebanon delayed DST in March 2023
with two days' notice; Egypt reinstated it in 2023 on about a week's). That is the host's storage
problem, not the renderer's, and the plan declines it deliberately.

## Public surface

Added:

- `TimeZoneInfo? ISchedulerAppointment.TimeZone => null` — default interface member.
- `SchedulerMoment` (name open) — outbound value type.

Changed:

- Every outbound `DateTime` listed in decision 2 becomes `SchedulerMoment`. **Breaking**, and
  intentionally so.
- `SchedulerView.TimeZone` keeps its type and name; its documented meaning changes.

Unchanged: `ItemsSource`, `DisplayDate`, every appearance property, `SchedulerTimeGutterTappedEventArgs`
(carries a `TimeSpan`, not a date).

## Implementation steps

1. Add `TimeZoneInfo? TimeZone => null` to `ISchedulerAppointment` with the six-cell table in its
   XML docs.
2. Add an internal normalizer — one place, one function — turning `(DateTime, TimeZoneInfo?, D)` into
   a wall-clock in `D`, with the decision-5 policy and the decision-6 fast path.
3. Apply it at the five read sites listed in *Current state*. There is no wrapper type and no
   per-repopulate projection: the engines call the normalizer.
4. Add `SchedulerMoment` and change the outbound surfaces to it. Set `Kind` deliberately with
   `DateTime.SpecifyKind` at construction rather than relying on arithmetic.
5. Re-run the layout measurement from `docs/design/verification.md` and record the result, including
   the floating and same-zone fast paths against a mixed-zone collection.

## Documentation updates

Per `AGENTS.md` "Keeping Them In Sync", in the same change:

- **`AGENTS.md:130`** — replace the invariant. New wording along the lines of: the control converts
  every appointment into the view's zone for placement, using `Kind` and the optional per-appointment
  zone; it has no DST-aware geometry; the fast path is deliberate.
- **`docs/requirements/data-and-state.md`** — DAT-16 becomes wrong and must be *rewritten*, not
  appended to (AGENTS.md: "If a change makes an existing statement wrong, fix the statement"). New
  numbers for the per-appointment zone and the outbound type.
- **`docs/API.md`** — the `ISchedulerAppointment` contract table, the six-cell table, `SchedulerMoment`
  under Contracts and in the TOC, the `TimeZone` row, and the "Time zones are yours to handle"
  paragraph under *Behaviour worth knowing*, which is now false as written.
- **`docs/design/data-and-layout.md` (§9)** — the decisions and their costs: why the zone is an input
  meaning and not a display hint (the itinerary geometry argument), why inbound stays `DateTime`, why
  no implicit conversion, the DST normalization policy, and the fast path.
- **`docs/design/verification.md` (§15)** — the re-measurement.
- **`SchedulerModel.cs:64`** — "The control never converts between time zones" is now false.

## Tests

- Normalizer, as arithmetic, no MAUI host: one case per cell of the six-cell table; the two ignoring
  cells asserted to ignore; invalid and ambiguous inputs asserted against the decision-5 policy and
  asserted **not to throw**.
- Through `SchedulerHarness`: an appointment supplied as UTC lands on the grid position its instant
  implies in a non-device `TimeZone`; a floating one lands where its wall-clock says regardless of
  `TimeZone`; changing `TimeZone` moves the first and not the second.
- `SchedulerMoment`: `ToDateTimeUtc` / `ToDateTimeLocal` round-trip; the floating-value case behaves
  per whatever open question 2 resolves to.
- Mutation-check per AGENTS.md: the fast path must be provably a *fast path* and not a behaviour
  change — the same collection laid out with and without it must produce identical placements.

## Sample app

The playground has one zone and floating times, so it demonstrates none of this. Add a `TimeZone`
knob and a second appointment set carrying `Kind = Utc`, so the drawer can show the same data placed
two ways. `SampleAppointment` overrides the new member to return null, which is also the
documentation example for opting out.

## Verification

- `dotnet build` for `net10.0`, `-ios`, `-android`; sample for `-ios`, `-android`.
- `dotnet test`.
- Re-run the layout benchmark; compare against `docs/design/verification.md`.

## Open questions, as resolved

1. **The type's name.** `SchedulerMoment`.
2. **What `ToDateTimeUtc()` does for a floating value.** The question dissolved: the type always
   carries a zone, because every moment the control produces is in the view's. There is no floating
   state to answer for, so no nullable and no throw.
3. **Equality.** Left as the record struct's own — `WallClock` and `Zone` both. Two readings on
   different clocks are different positions, which is what the type describes; a host comparing
   instants compares `ToDateTimeUtc()`.
4. **Struct cost.** Accepted.
5. **Is the per-appointment zone worth its permanence?** Kept, and the measurement (§15) is the
   caveat: a zoned appointment costs about 16× a floating one to lay out, and 4× as UTC. At 60,000
   loaded appointments all carrying a zone, a repopulate is about 20 ms of layout on a desktop before
   any phone multiplier. It stays because the cost is opt-in and the default is untouched, but a host
   should reach for it knowing that. If it is ever withdrawn, the default interface member means
   nothing breaks that had not opted in.

## What differed from the plan

- **`TimeZone` needed no new invalidation wiring.** The plan's *Current state* had it right and an
  earlier claim of mine wrong: `TimeZoneProperty` is already wired to `OnGeometryChanged`, and
  `ApplyGeometry` ends in `RebuildAll`.
- **The engines carry resolved times rather than re-reading.** Each engine reads an appointment's ends
  several times per pass, so both timeline and month bucket a small `Resolved` record instead, and the
  agenda keeps a side table. Converting per read would have multiplied the cost the fast path exists
  to avoid.
- **The accessibility guard had to change shape.** It compared the previously bound appointment
  instance; resolved times depend on `TimeZone`, which an instance comparison cannot see, so the
  described values are cached per view instead.
