# Plan — `INotifyCollectionChanged` support and a range-capable appointments collection

Status: **implemented**. This was the working plan, not a design decision; the decisions it settled
are now recorded in `docs/design/data-and-layout.md` (§9), `docs/API.md`,
`docs/requirements/data-and-state.md` (DAT-17 … DAT-20) and the invariants in `AGENTS.md`. Those are
the documents to read and to keep current — this file is kept only as the record of how the shape was
arrived at, and nothing should cite it.

> Drafted: 2026-09-02 14:10 EEST. Implemented the same day.

## Goal

Let a host keep **one** appointment collection, mutate it in place, and have the control repaint —
without reassigning `ItemsSource` on every change. Concretely:

- `ItemsSource` changes sourced through `INotifyCollectionChanged` are observed (in-place mutation),
  and plain replacement continued to work.
- A dedicated public collection type, `SchedulerAppointmentCollection`, provides **range** mutations
  (`AddRange`, `RemoveRange`, `ReplaceRange`) that the framework's `ObservableCollection` lacks
  (<https://github.com/dotnet/runtime/issues/18087>), because a range operation raises **one**
  change event instead of one per item.
- The control itself references only the `INotifyCollectionChanged` interface, never the concrete
  collection type. `INotifyCollectionChanged` stays **optional** — a host that does not want to
  hand over a change-notifying collection keeps assigning plain enumerables.
- The host is free to keep the whole loaded range in the collection and never trim it: **the control
  filters out the non-visible months/wk/agenda rows itself.** Already the contract (see below); this
  plan makes it explicit and removes any reason to clean the collection for off-screen periods.

## Non-goals

- Adding fine-grained, per-change incremental layout. The control already treats any
  `CollectionChanged` as "something changed — reload the window" (see *Current state*), and
  `QueueRepopulate` collapses bursts. Range methods therefore fit the existing model exactly; this
  plan does not introduce change-delta reconciliation.
- Automatically converting a host's `ObservableCollection` or `List` into a
  `SchedulerAppointmentCollection`.
- Making `INotifyCollectionChanged` required.

## Current state (verified in the tree)

- `ItemsSource` is `IEnumerable<ISchedulerAppointment>?` (`SchedulerView.Properties.cs:13`, `:211`).
- `OnItemsSourceChanged` (`SchedulerView.Properties.cs:734`) already unsubscribes an old
  `INotifyCollectionChanged`, subscribes a new one, invalidates the agenda surface, and calls
  `QueueRepopulate`.
- `OnItemsCollectionChanged` (`SchedulerView.Properties.cs:748`) ignores `e.Action`/`e.NewItems`
  entirely and just invalidates + `QueueRepopulate`. **Any change event → full reload.**
- `QueueRepopulate` (`SchedulerView.Properties.cs:767`) coalesces a burst of events into one rebuild
  on the next tick.
- The control already filters: `PopulateSlot` → `pageSurface.Layout(LayoutItems(), slot.PageStart)`
  picks the items belonging to the rendered page out of the whole `ItemsSource`
  (`SchedulerView.Rendering.cs:143`, `:155`, `:412`). The agenda surface grows its own loaded range
  (`AgendaSurface`) and the host supplies a wider range than is visible.
- **The `INotifyCollectionChanged` behaviour already works.** What is missing is (a) a public
  range-capable collection for hosts, (b) documentation that tells hosts to keep everything loaded
  and filter in the control, and (c) tests.

> Consequence: this is mostly a public-surface + documentation + test addition. The only control-code
> change likely is a clarifying guard/naming; the subscription logic is already correct.

## Design decisions

1. **`SchedulerAppointmentCollection<T>` is a generic subclass of `ObservableCollection<T>`, with the
   constraint `where T : ISchedulerAppointment`.** Generic lets a host model its own appointment type
   while still satisfying this control's contract; `SchedulerAppointmentCollection<ISchedulerAppointment>`
   is the natural instance for a host that uses the interface directly. It derives from
   `ObservableCollection<T>` so it freely satisfies `INotifyCollectionChanged`, `IList<T>`,
   `INotifyPropertyChanged`, and can be data-bound as usual.

2. **Range operations raise one event, not one per item.** ~~Following `ObservableRangeCollection`
   (MvvmHelpers), each raises a single `NotifyCollectionChangedAction.Reset`, because the control
   re-reads the whole collection and never consumes the payload.~~ **Superseded during
   implementation:** each range operation raises one event *carrying what changed* — `Add`, `Remove`
   or `Replace` with its items and starting index — falling back to `Reset` only for the two changes
   a single `NotifyCollectionChangedEventArgs` cannot state faithfully (a scattered removal, and a
   replacement whose length differs from the range it replaces). The reasoning the original decision
   missed: the collection is public, so the scheduler is not its only consumer, and `Reset` is
   lossless only for a consumer that re-reads everything. `Count` is announced only when it moved;
   `Item[]` always. See `docs/API.md` and `docs/design/data-and-layout.md` (§9) for the settled
   version — this bullet is history.

3. **The control keeps only an interface dependency.** No `using` of the new type, no `is
   SchedulerAppointmentCollection`, nothing that casts `ItemsSource`. The property stays
   `IEnumerable<ISchedulerAppointment>`. `INotifyCollectionChanged` remains an `is`-check.

4. **`INotifyCollectionChanged` is not required.** A host that supplies a plain `List` or array gets
   the same behaviour as today, minus live mutation.

5. **Trimming is the control's job, never the host's.** The control already lays out from the whole
   collection and selects the visible subset. The new documentation must tell hosts to keep the
   entire loaded range and not remove off-screen periods — that is the entire reason a range method
   like `AddRange` is preferable: the host appends newly loaded months rather than rebuilding and
   reassigning.

## Public surface

New public type (namespace `Owlery.Maui.Scheduler`):

```csharp
public class SchedulerAppointmentCollection<T> : ObservableCollection<T>
    where T : ISchedulerAppointment
{
    public SchedulerAppointmentCollection();
    public SchedulerAppointmentCollection(IEnumerable<T> items);

    public void AddRange(IEnumerable<T> items);          // one Reset
    public void RemoveRange(IEnumerable<T> items);       // one Reset
    public void RemoveRange(int index, int count);       // one Reset
    public void ReplaceRange(int index, int count, IEnumerable<T> items); // one Reset
}
```

`SchedulerAppointmentCollection<ISchedulerAppointment>` is what a host that populates it with the
interface directly would hold, and assigning it to `ItemsSource` needs no change — the property takes
`IEnumerable<ISchedulerAppointment>`.

Each range method calls `CheckReentrancy` before mutating; no override of `InsertItem`/`ClearItems` is
needed, because `ObservableCollection.OnCollectionChanged` already raises inside `BlockReentrancy`.
A range that changes nothing raises nothing.

No change to `ItemsSource`'s type, default value, or the existing assignment contract.

No new events. `VisibleDatesChanged`, `AppointmentDropped`, etc. are unchanged.

## Implementation steps

1. Add `SchedulerAppointmentCollection.cs` under `Owlery.Maui.Scheduler/` (public, documented).
   - Generic class `SchedulerAppointmentCollection<T>` with `where T : ISchedulerAppointment`,
     deriving from `ObservableCollection<T>`.
   - Implement `AddRange`, `RemoveRange` (both overloads), `ReplaceRange` with the reference
     notification semantics and single `PropertyChanged` for `Count`/`Item[]`.
   - Guard empty ranges and null sources (throw `ArgumentNullException` on null source, matching
     `List<T>` conventions).
2. Confirm the control's existing `is INotifyCollectionChanged` logic is sufficient and needs no
   change beyond possibly a comment. Add nothing concrete.
3. Update the control project only if a comment clarifies that any change event is treated as a full
   reload and that range methods are the recommended route.

## Documentation updates

Per `AGENTS.md` "Keeping Them In Sync", change code and docs in the same change.

- **`docs/API.md`**
  - Add `SchedulerAppointmentCollection` to the table of contents and to a new
    "Collection type" subsection under **Contracts**.
  - Update the `ItemsSource` row (`docs/API.md:124`) to state that both assignment **and**
    `INotifyCollectionChanged` are honoured, that the collection can hold the whole loaded range
    (the control filters), and to point at `SchedulerAppointmentCollection` for range mutations.
  - Add a "Behaviour worth knowing" paragraph: keep everything loaded; the control filters
    non-visible months; prefer range methods.
  - **Cite `dotnet/runtime#18087`** in the `SchedulerAppointmentCollection` doc entry, explaining
    `ObservableCollection` has no range methods and that the reference workaround is a one-event
    range mutation.
  - Re-read and, if needed, clarify `docs/design/appearance.md:33` — easy to misread as "ItemsSource
    only honours replacement"; it is about `WorkingDays`, but the contrast sentence is confusing
    now that `ItemsSource`'s change-notification support is a settled design point.

- **`docs/requirements/data-and-state.md`** — append new requirement numbers (keep existing IDs
  stable, add new ones):
  - **DAT-17** A host may update a loaded appointment collection in place; the view observes it
    through `INotifyCollectionChanged` and repaints.
  - **DAT-18** Observing changes is optional — a host may also assign a fresh collection instance;
    both update the view.
  - **DAT-19** The host is not expected to trim the collection for periods the view is not showing.
    The view filters the visible period itself, so a host may keep the whole loaded range.
  - **DAT-20** A host that mutates in bulk should do so through a range operation so that
    `ObservableCollection`'s per-item notifications do not cause repeated work; the control coalesces
    changes, but a single event is cheaper.

- **`docs/design/data-and-layout.md` (§9)** — extend the `ItemsSource` bullet: note that
  `INotifyCollectionChanged` is honoured with a full-window reload per change, that range methods are
  the recommended route (link `dotnet/runtime#18087` + `docs/API.md`), and that the control — not the
  host — selects the visible subset, so the host keeps the whole range.

- **`AGENTS.md`**
  - Add an invariant: "The control references `INotifyCollectionChanged`, never a concrete collection
    type." 
  - Note in **Blazor Bindings**/public-surface guidance that `SchedulerAppointmentCollection` is a
    new public member and regeneration is a downstream consumer step.

## Tests

Headless (`net10.0`), one new file plus integration coverage:

- **New `SchedulerAppointmentCollectionTests.cs`** (pure .NET, no MAUI host):
  - `AddRange`/`RemoveRange`/`ReplaceRange` raise exactly one `CollectionChanged` each (assert event
    count), and the expected action type.
  - `Count` and `Item[]` `PropertyChanged` fire once per range op.
  - No notifications for an empty/`null`-no-op range.
  - Items end up in the right order; `RemoveRange(index, count)` clears exactly the window.
  - Mutation-check (AGENTS.md): each test must fail if the range op is reduced to a per-item loop —
    assert single event by *counting* events, not by checking content.

- **Integration in `SchedulerViewTests.cs`** (or a new file using `SchedulerHarness`):
  - Assign a `SchedulerAppointmentCollection<ISchedulerAppointment>`, assert a visible appointment
    renders.
  - Call `AddRange` with an appointment in the visible week → assert it appears without reassigning.
  - Call `AddRange` with only an off-screen month's appointment → assert the visible page stays put
    (proves the control filters; also the test that mutation-checking needs — undo the filter and it
    should fail).

## Sample app (in scope)

- `Owlery.Maui.Scheduler.Sample/Data/AppointmentSource.cs` currently rebuilds a `List` and reassigns
  `ItemsSource` (`Publish` → `scheduler.ItemsSource = appointments.ToList()`, `:114`). Switch the
  sample to hold a `SchedulerAppointmentCollection<ISchedulerAppointment>` and use
  `AddRange`/`ReplaceRange` for month loads and `Move`, demonstrating in-place mutation to the
  reference host. Keep the `Changed` event so chrome counts stay correct.

## Verification

- `dotnet build Owlery.Maui.Scheduler/Owlery.Maui.Scheduler.csproj -f net10.0` and the `-ios`/`-android`
  targets.
- `dotnet test --project Owlery.Maui.Scheduler.Tests/Owlery.Maui.Scheduler.Tests.csproj`.
- Review that no file under `Owlery.Maui.Scheduler/` (outside the new collection file) references
  `SchedulerAppointmentCollection` — only `INotifyCollectionChanged`.

## Open questions

Resolved:

- **Generic vs. concrete.** `SchedulerAppointmentCollection<T>` with `where T : ISchedulerAppointment`
  (design decision 1).
- **Sample demonstration.** In scope for this change, not a follow-up (see *Sample app*).

How range operations report their change was first settled as a `Reset` for all of them, then
reopened and settled the other way during implementation: each raises the precise action with its
payload, and `Reset` only where one event cannot describe the change. The original reasoning — the
control never reads the payload — was true and beside the point, since the collection is public and
the scheduler is not its only consumer. Design decision 2 above records both, and
`docs/design/data-and-layout.md` (§9) is where the settled version lives.
