# 17. The page surface seam

Month and agenda views need different surfaces, not different day counts (sections [18](month.md) and [21](agenda.md)). What
that means in practice is that roughly half this control does not care which it is drawing.

`ISchedulerSurface` is where the three part company. A surface answers what a page holds and how it is
measured: where a page starts, how to step to the next and previous one, which dates it shows, how to
turn appointments into placements, and where a placement or a selected cell sits. `TimelineSurface`
is the day/three-day/week implementation and simply reads the geometry it is given.

The view pool, reconciliation in `PopulateSlot`, identity by `Key` and public events stay on the other
side of the seam. The horizontal ring buffer and snapping apply only to timeline and month; agenda
keeps the same slot shape but uses its centre slot alone.

Two things are deliberately *not* on the interface:

- **Dragging.** It belongs to the timeline alone, and putting it here would have reproduced exactly
  the interface this document already warns against in [`AGENTS.md`](../../Owlery.Maui.Scheduler/AGENTS.md) — a dozen members relocating the
  coupling rather than reducing it. Every member that survived is arithmetic, which is what makes
  all implementations testable without the MAUI test host.
- **Building the visual tree.** A month has no time gutter and does not scroll vertically; an agenda
  has no horizontal header or pager movement. Structure is settled once when the mode changes rather
  than on every page.

`SchedulerGeometry` was left as the timeline's geometry rather than being split up front. The values
the modes genuinely share — viewport size, `SlotStarts`, `PageSpan` — are visible in
`CellSelectionOverlay`, which now reaches for geometry only for those and goes through the surface for
everything else. Lifting them is worth doing when there is a second implementation to check the guess
against, not before.

One generalisation was taken while passing. `VisibleDatesChanged` derives its prefetch bounds from
`DatesOn` rather than from the page starts, because a page need not begin on the first date it shows:
a month grid opens on the tail of the previous month, and the host has to be told to fetch that far
back. For a timeline the two are the same value; an agenda reports its loaded vertical range.

