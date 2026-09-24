# Probe: prefer a surplus view already at the target bounds

**Branch-only handoff note. Nothing on this branch is meant to merge.** Every probe change is marked
`PROBE` in the source. When the work below is done, implement the real change on a fresh branch from
`main` and delete this one.

## The idea

When `PopulateSlot` fills a gap from the page's own surplus (the second pass, see
`docs/design/appointment-views.md` → "Surplus is recycled where it stands"), prefer a surplus view that
already sits at the gap's bounds — most likely the same recurring session a week (or a month) earlier.
Its bounds do not change, so `PositionAppointmentView` skips the layout write, and the template's
property writes are mostly value-equal no-ops. Key matching (first pass) is untouched, so nothing
visible changes: every view reassigned here was going to be rebound to some other appointment anyway.

## What the probe adds

| Where | What |
|---|---|
| `Owlery.Maui.Scheduler/SchedulerView.Rendering.cs` | In `PopulateSlot`, before the arbitrary fill: a lookup of surplus views by `(bounds, Subject)`, used to fill gaps when the `AppContext` switch `Owlery.Probe.PreferSameBounds` is on. Every bind is timed and bucketed by origin (keyed / surplus / rented) and whether its bounds changed. |
| `Owlery.Maui.Scheduler/Internal/RecycleProbe.cs` | The counters, exposed to the sample through `AppContext.GetData("Owlery.Probe.Report")` — no public API. |
| `Owlery.Maui.Scheduler.Sample/Data/SampleDataGenerator.cs` | `Recurring` (one weekly pattern repeated, a new instance per occurrence) and `ExceptionRate` (split evenly into cancelled, moved-within-the-day, and one-off extras). |
| `Owlery.Maui.Scheduler.Sample/Playground/PerfRun.cs` | `RunRecycleProbeAsync`: per case, alternates the switch off/on for two rounds, 24 page changes each, after an untimed warm-up pass. Reports managed ms per page change, the sum of the four frame intervals after each write, and the probe counters. |
| `Owlery.Maui.Scheduler.Sample/Playground/PlaygroundPage.cs` | Runs the probe when `OWLERY_PROBE=1`. |
| `Owlery.Maui.Scheduler.Sample/Platforms/Android/probe.env` + csproj | Sets `OWLERY_PROBE=1` on Android (an `AndroidEnvironment` item), since there is no launch-time env var there. |

The lookup is allocated per call and is built in **both** modes, so its cost is in both sides of every
comparison. The real implementation must not allocate per rotation.

### Reading a report line

```
gaps 557, matchable 288 | keyed/same 0, keyed/moved 0, surplus/same 288×2.490ms, surplus/moved 252×12.780ms, rented/same 5×8.487ms, rented/moved 12×8.690ms
```

- `gaps` — positions that no view matched by key.
- `matchable` — gaps for which a surplus view with the same bounds *and* subject existed.
- `surplus/same` — gaps filled from surplus without the bounds changing (bounds only; the subject may
  differ, which is why it can exceed `matchable`). `N×Tms` is count × mean managed bind time.
- `…/moved` — the bounds were rewritten.

## Results so far

Android emulator (`medium_phone`, x86_64), Release, run on 2026-09-25. Bind time is the sum over 24
page changes, averaged over two rounds.

| Data | Surface | Gaps | Matchable | Same bounds, off | Same bounds, on | Bind time off → on |
|---|---|---|---|---|---|---|
| Pure weekly repeat | Week, 25–250/mo | — | = gaps | = gaps | = gaps | no change |
| Pure weekly repeat | Month, 100/mo | 2,736 | 2,736 | 2,736 | 2,736 | no change |
| Pure weekly repeat | Day, 100/mo | 79 | 0 | 0 | 0 | — |
| Random (existing generator) | Week, 100/mo | 550 | 0 | 0 | 0 | — |
| 10% exceptions | Week, 100/mo | 553 | 440 | 176 | 440 | 2.24 s → 1.35 s (−40%) |
| 30% exceptions | Week, 25/mo | 141 | 92 | 62 | 92 | noise |
| 30% exceptions | Week, 100/mo | 557 | 288 | 100 | 288 | 4.39 s → 3.66 s (−17%) |
| 30% exceptions | Week, 250/mo | 1,400 | 560 | 158 | 560 | 4.03 s → 3.61 s (−10%) |
| 30% exceptions | Month, 100/mo | 2,729 | 1,324 | 430 | 1,504 | 5.26 s → 3.88 s (−26%) |

What is established:

- With a pure repeat the existing fill already lands every view on its old bounds, by accident of
  ordering: surplus and positions both come out in start order. With any exceptions that alignment
  breaks — one cancelled, moved or added event shifts the rest of the day — and 10% exceptions already
  leave 60% of the achievable matches on the table.
- A rebind that keeps its bounds costs ~1–2 ms; one that moves ~4–13 ms (month chips: ~1.1 vs ~2 ms).

What is **not** established:

- **Whether a page change actually gets faster.** This emulator is erratic — 500–1,100 ms across the
  four frames after a write, with round-to-round drift as large as the effects. Median managed ms per
  page change improved clearly only in the 10% case (~100 → ~65 ms, both rounds). The frames after a
  write improved in both rounds for 250/mo and Month, got worse for 25/mo, and were mixed for 100/mo.
- The bind timings are managed-only, so they miss the layout pass this is meant to save — which is
  where most of the benefit should show.

## What to do next

1. **Re-measure on a steadier target** — the iOS simulator on a Mac, or a real Android phone. The
   probe needs no changes:
   - iOS: `dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-ios -c Release`,
     install, then `SIMCTL_CHILD_OWLERY_PROBE=1 xcrun simctl launch --console-pty booted com.owlery.scheduler.playground`
     and read the `[perf]` lines.
   - Android: `dotnet build … -f net10.0-android -c Release -t:Install`, launch the app, and read
     `adb logcat -s DOTNET:*` for the `[perf]` lines. A full run takes about 25 minutes.
   - Watch the page-level columns (`managed p50/mean`, `4 frames after write`), not only the bind
     buckets. If the alternation still drifts, change `RunRecycleProbeAsync` to flip the switch **per
     page change** instead of per round, so drift hits both sides equally.
2. **Decide.** If page changes do not get measurably faster on a steady target, stop, and record the
   finding in `docs/design/appointment-views.md` so the idea is not re-litigated. The downsides it has
   to beat: a lookup on the hottest path that is pure cost for irregular data, day view and after any
   resize or `HourHeight` change; one more ordering invariant in a method whose history is leaked
   views; and a benefit that depends on the host's template doing little work per rebind.
3. **If it is worth it, implement on a fresh branch from `main`:**
   - Match on **bounds first**, `Subject` only as a tie-break — the Month result shows bounds-only
     matches still help, and bounds are what skip the layout write.
   - Run it after key matching and before the arbitrary fill. Key matching stays exactly as it is.
   - No per-call allocation: reuse collections the way `reusableByKey`/`reusableSurplus` are reused, and
     skip the lookup entirely when there are no gaps or no surplus.
   - The final discard loop must honour what the new pass claimed, or a view ends up owned by no page
     (see `PlacedViews` and `AGENTS.md`).
   - Add a headless test: rotate onto a recurring page with exceptions and assert each view that can keep
     its bounds does. **Mutation-check it** — it must fail against the current in-order fill.
   - Update `docs/design/appointment-views.md` ("Surplus is recycled where it stands") with the rule
     and the steady-target numbers, and `Owlery.Maui.Scheduler/AGENTS.md`'s Performance section if the
     rule belongs there. Build every target framework, including `net10.0-ios`.
   - PR title in Conventional Commits form with the `control` scope, e.g.
     `perf(control): keep recurring sessions on the view already at their bounds`.
4. Delete this branch.
