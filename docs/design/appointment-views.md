# Appointment views and the pool

Where appointment views live, why spares are kept rather than discarded, and the reconciliation
that makes a refresh cost nothing when nothing changed.


Appointment views are **not** children of the week pages. All of them — for all three weeks, plus the
spares — are siblings in a single `AbsoluteLayout`:

```
surface (AbsoluteLayout, 3 × viewport wide)
├── GraphicsView            ← the whole grid background
├── selection affordance    ← one reusable view
├── AppointmentView × n     ← the three weeks' appointments
├── AppointmentView (hidden) ← spares
└── drag time label
```

**Why one parent.** A per-page pool means one page can hoard 15 idle views while another allocates 10
new ones. A scheduler-level pool with a scheduler-level parent means renting a spare for a different
week involves no reparenting at all — the parent is already correct. An appointment's x-coordinate
simply encodes which slot it belongs to.

## 6. Spares stay in the visual tree, hidden with `IsVisible = false`

Appointment views can be expensive: icons, images, nested layouts. Keeping them *instantiated* is
valuable; keeping them *rendered* is not.

`IsVisible = false` maps to `Collapsed` in MAUI: the element stays attached with its native view and
gesture recognizers intact, but is skipped during measure, arrange and rendering, and receives no
input. That is exactly the wanted trade.

Rejected alternatives:

- `Opacity = 0` — still measured, arranged, and potentially hit-tested. Strictly worse.
- `TranslationX = -10000` — fights the layout system for no benefit.
- Removing from the tree on every recycle — throws away the expensive part.

**Without a cap, deliberately.** The instinct is to bound the pool so one unusually busy week cannot
make the control retain those views forever. That would be wrong here. Every `CreateContent()` call
permanently registers a template root inside BlazorBindings' `DataTemplateItemsComponent`, and that
registration is not released when the view is detached from the layout. Discarding a spare would
therefore free nothing on the Blazor side *and* force a second registration the next time the view
count rises. Retaining every view is strictly cheaper, so `AppointmentViewPool` keeps them all.

The pool consequently settles at the high-water mark of appointments visible across three weeks at
once — tens of views for this workload, all idle and collapsed.

Gesture recognizers are attached **once**, at view creation, and read whatever appointment is currently
bound. Pooling therefore never adds or removes event handlers.

### Reconciling, not rebuilding

Pooling only pays off if the pool is left alone when nothing has changed. `PopulateSlot` originally
released every view for a week and rented them straight back. That is wrong twice over: each view is
hidden and re-shown, and because the pool is a stack the views return in reverse order, so every one
ends up bound to a *different* appointment than it had. A refresh returning byte-identical data still
repainted every appointment on screen — visible as a flash across the whole week.

It now reconciles by identity instead. Each week indexes the views it already has by the `Key` of the
appointment they are showing, and every appointment in the new layout reclaims its own view. An
unchanged reload rebinds each view to an equivalent item in the same place — identical output, nothing
visibly happens — and only a genuine surplus or shortfall touches the pool. Bounds are written only
when they actually differ, since an unchanged write still costs a layout pass.

An earlier version matched positionally, relying on the layout engine's deterministic order. That is
correct for an unchanged reload but degrades quietly: two appointments tying on start and duration can
swap views, and inserting one early shunts every later appointment onto a different view, repainting
the rest of the week for a one-item change. Keys make reuse mean what it says.

This matters more than it looks, because the host re-emits `ItemsSource` freely — cached results
followed by fresh ones, one assignment per loaded month — so a single refresh can run this path
several times.

Two guards keep the pool honest, both of them cases where a view ends up owned by no page and
therefore never handed back — left visible, drifting over whatever scrolls past:

- **Duplicate keys within one page.** Indexing views by key means a second view claiming a key
  displaces the first, which then reaches neither the new layout nor the surplus sweep. [Section 9](data-and-layout.md)
  tells hosts not to do this, but the punishment should not be a permanent ghost, so the displaced
  view is returned there and then.
- **Changing the template while a drag is in flight.** A dragged view has been taken out of every
  page, so none of the template handler's cleanup reaches it: not in a slot, not among the spares,
  and `RepopulateAllSlots` declines to run while a drag is armed. Left alone it survives the change
  and is later handed into a pool that no longer matches it, to be rented out as though it did. The
  drag is therefore cancelled first, which is what the view-mode switch had always done.

### Identity is the change signal

MAUI compares one property by reference and everything else by value, and it happens to be the one
that matters here:

```csharp
bool sameValue = ReferenceEquals(context.Property, BindingContextProperty)
  ? ReferenceEquals(value, original)
  : Equals(value, original);
```

So handing a view the *same* appointment object it already holds costs nothing — no property change,
no re-render. Handing it an equal-but-new one costs a full render of the template. Value equality
never enters into it, which is why making an appointment a record buys nothing.

That fixes the contract for hosts: **an appointment that has not changed should be the same instance,
and one that has changed must be a new instance.** Immutable snapshots, with identity standing in for
"something happened". It also rules out the other shape — a mutable, observable appointment that the
host updates in place. Nothing here subscribes to property changes; a template is a Blazor component
that reads its parameter, so an in-place mutation would update nothing at all and the view would go
quietly stale.

Note what this means for a move: a rescheduled appointment cannot be edited where it lies. It has to
be replaced, or the view stays bound to something that no longer describes it — including its
accessibility text, which is skipped precisely when the appointment appears unchanged.

### Coalescing a burst

A host that loads in chunks assigns `ItemsSource` once per chunk — cached results, then fresh ones,
per period — and each assignment used to rebuild all three pages immediately. One change of view was
measured doing that eight times over, at roughly 60ms each.

The data paths therefore post a single rebuild to the next tick rather than running one on the spot;
the same measurement afterwards shows three, one per switch. Nothing is lost by waiting: the data is
read when the rebuild runs, not when it was announced. Only the data paths go through it — everything
a drag does still repopulates synchronously, because those calls are how a gesture puts the pages
back and cannot be deferred.

### What is left, and where it is

With those in place the remaining cost is concentrated in one spot: **binding a view to an appointment
it has never shown**. Measured at roughly 6ms against 0.25ms for a rebind of the same instance — the
difference between building a template's subtree and skipping the write entirely. A page rotation does
that for every appointment on the incoming page, so a swipe onto a busy week is a single frame of tens
of milliseconds. The pool is what keeps that from being worse.

Beyond the pool the cost belongs to the host's template, and **how the template is written matters
more than it looks**. The same appointment box, built once from MAUI primitives and once as a Blazor
component through generated bindings, was measured over identical page rotations:

| Fresh binds | Blazor component | MAUI primitives |
|---|---|---|
| 10 | 66.94ms | 45.21ms |
| 13 | 51.82ms | 33.58ms |
| 11 | 41.97ms | 27.70ms |

That is 4.73ms against 3.13ms per bind — **about a third off** — for a template rendering the same
thing. Worth knowing, and worth keeping in proportion: the remaining 3.13ms is MAUI constructing a
`Border`, a `Grid`, a `Label` and an `Image` and laying them out for the first time, so a Blazor
templating layer is a third of the cost rather than the bulk of it. Only the fresh-bind path moves;
rebinding a reused view never touches template construction and stays at ~0.25ms either way.

(Debug build on an emulator. A release build would likely widen the gap, the managed work being the
side that AOT and the JIT treat differently.)

### One template, not a selector

Every view comes from a single `DataTemplate`, which is what makes any view reusable for any
appointment. A `DataTemplateSelector` is refused where it is assigned rather than accepted and failed
on later — it derives from `DataTemplate`, so one *can* be set, and would otherwise surface deep in
the pool as a selector having no `LoadTemplate` for `CreateContent` to call.

Supporting selectors is possible and was considered. It means a pool per resolved template, plus a
second clause in the reconciliation: a view matched by key is only reusable if it came from the same
template, so an appointment that changes kind has to give its view up and rent from another pool.
Two things argue against doing it before something needs it.

The pool is unbounded by design (above), so *N* kinds means *N* unbounded pools whose peaks do not
coincide — twenty external and twenty ordinary appointments never on screen together would retain
forty views where one pool retains twenty. And the extra clause lands in `PopulateSlot`, which has
produced every stranded-view defect this control has had.

Against that, the thing selectors would buy is already available more cheaply. A template that builds
each variant once and toggles which is visible switches kind for the price of a rebind, ~0.25ms,
because nothing is constructed; swapping templates costs a fresh build at ~3ms. The recommended
pattern is therefore also the faster one, which is why the refusal names it.

### Every drawn view belongs to a page

A view that is visible and in none of the three pages is the worst defect this area produces, because
nothing about it looks broken. It reads as an ordinary appointment; it refuses to be pressed, since
`HitTestAppointment` walks the pages and the press falls through to the cell underneath and behaves
like an empty one; and only a page's views are translated when the weeks rotate, so it holds the
column it was last left at while every other week scrolls past it. Nothing recovers it either — once
the last reference is gone it outlives the gesture that stranded it and every swipe after.

The one legitimate exception is the drag's own view. It leaves its page on pick-up so the weeks can
rotate underneath it, and stays out while an accepted drop waits for the host to feed the change
back; `OnPageSettled` gives up waiting at the next change of period so a host that never answers
cannot strand it.

This is asserted rather than argued. `PlacedViews` reports what the pages hold plus that one
exception, and `AppointmentOwnershipTests` checks after each way a drag can end that nothing else is
drawn. The reason for checking instead of reasoning is that the state is reachable from several
directions at once — a view handed back but left in a page, a view in a page bound to nothing, a
pick-up that fails after the view has already been detached — and each of those is a different line
in a different file. Enumerating them again after every change is not something anyone will keep
doing; the invariant is one line and holds regardless of how the next path is added.
