# Paging

How the calendar moves sideways: the three-slot ring buffer the pages live in, how the end of a
swipe is detected, and what a swipe actually rearranges.

## 2. Infinite paging as a three-slot ring buffer

The horizontal pager is a `ScrollView` containing one surface three viewport-widths wide:

```
Horizontal ScrollView
┌──────────────┬──────────────┬──────────────┐
│    slot 0    │    slot 1    │    slot 2    │
│   previous   │   current    │     next     │
└──────────────┴──────────────┴──────────────┘
       0               W              2W
```

The scroll position always returns to `W`. After the user lands on slot 2, the weeks rotate one step
and the scroll jumps back to `W` without animation. Because the page now at `W` shows exactly the week
that was just at `2W`, the jump is invisible.

**Chosen over `CarouselView`.** Rejected up front: it owns recycling and scroll state, which is
precisely what needs to be controlled here.

**Chosen over a three-item horizontal `CollectionView`** with `SnapPointsType="MandatorySingle"`. That
would provide snapping for free, but its virtualization buys nothing for exactly three heavyweight
surfaces, and it would take back control of recycling. The trade made here is explicit: give up free
snapping (section 3) to keep recycling.

**Three slots, not five.** Five gives a bigger safety buffer during an aggressive fling, at the cost of
~70% more live appointment views. Three is sufficient because paging is one week per gesture: by the
time `next` becomes `current`, everything that can be revealed is already rendered. Data is prefetched
wider than the rendered pages ([section 9](data-and-layout.md)), so recycling a page never has to wait on a network call.

---

## 3. Detecting the end of a swipe

The event that should drive paging is **finger release**, not "scrolling stopped". Those are far
apart: a flick keeps decelerating for anything up to a second or more after the touch ends. Deciding
the week only once motion has died means the user sees the content coast to a halt and *then* slide
again to settle — two animations, with the one that actually chooses the week arriving last.

MAUI's `ScrollView` exposes neither event, and has no paging or snap support. The control used to
approximate one with a 90 ms timer restarted on every `Scrolled` event: when the offset went quiet,
the page was inferred from it with `round(scrollX / W)`. On iOS that was tolerable because
`UIScrollView.PagingEnabled` had already committed to a page on release, so the timer only answered
"has it arrived yet". On Android nothing had committed to anything, so the timer waited out the
inertia and a flick was followed by a visible pause — the conformance gap NAV-7 recorded.

**The pager now decides, and says so.** `PagingScrollView` raises `PageSettled` with the page it has
come to rest on, because only the platform knows the release velocity and where a fling would land.
[Section 19](pager.md) covers what that took on each platform. What is left here is the response, and it is
short:

| Landing page | Action |
|---|---|
| 1 | nothing — already centred |
| 2 | rotate forward, recentre on `W` |
| 0 | rotate backward, recentre on `W` |

`OnPageSettled` is **synchronous**, and that is the point. Rotating moves the appointment views onto
their new pages while the scroll offset still points at the old one, so anything drawn between the
two shows a page the user never swiped to — a flash of the neighbouring week, which is exactly what
Android was reporting. A method that never yields cannot have a frame composited part-way through it,
and `PagingScrollView.ScrollTo` is deliberately not awaitable for the same reason.

One flag survives: `recentring` suppresses handling around the rotation and the jump, so the recentre
cannot be mistaken for a new gesture. `snapping` is gone with the timer that needed it. Neither flag
ever gated the day-header sync — that is deliberate and was learned the hard way, see [section 8](grid-and-scrolling.md).

---

## 4. Rotating positions, not views — and refreshing exactly one week

Three approaches were considered:

| Approach | Rearranges views? | Pages refreshed per swipe | Used |
|---|---|---|---|
| Move week views between positions | yes | 1 | no |
| Fixed slots, swap child views between them | yes | 1 | no |
| Three fixed pages, change what each represents | no | **3** | no |
| Three fixed pages, rotate their *positions* | no | **1** | **yes** |

The third option keeps the visual tree perfectly stable but has to rebind all three weeks after every
swipe. For pages holding tens of complex appointment views that is the wrong optimization.

What the control does instead: `WeekSlot` objects hold a week plus its views, and the array of slots is
rotated. Two of the three slots survive untouched and only *move*; one is rebuilt.

```
before:   [ week-1 ][ week 0 ][ week+1 ]
                                   ^ visible
after:    [ week 0 ][ week+1 ][ week+2 ]
                        ^ visible
          └── moved ──┘   └─ rebuilt ─┘
```

Moving is deliberately cheap: an appointment view's `AbsoluteLayout` bounds hold its position *within*
its week, and the slot offset lives in `TranslationX`. Shifting a whole week is therefore a translation
update per view and triggers no layout pass. Rebuilding is confined to `PopulateSlot`, which touches
only the recycled slot.

### A rotation is not only for swipes

Three things move the calendar by one period, and all three go through the same rotation:

| Trigger | Entry point |
|---|---|
| A swipe settling on a page either side | `OnPageSettled` |
| Holding a dragged appointment against an edge | `PageDuringDragAsync` |
| The host setting `DisplayDate` to any other period | `TrySlideToPage` |

The last of those used to call `RebuildAll` — which repopulates all three pages, and lands on the new
period with no motion at all. That is wrong twice over for a destination *already rendered one page
away*: its appointment views exist and are bound, and rebuilding discards them only to rent
replacements for the same data; and a calendar that changes contents where it stands gives no clue
which direction it moved in. A host's "next week" button is one page forward exactly as much as a
swipe is.

Both paths rotate, then jump to where the outgoing page has landed — visually identical to the frame
before — and animate across to the centre. The jump is unanimated and the slide is not, which is what
turns a rotation into a swipe the user did not make. Edge paging uses `SlideToAdjacentPageAsync`,
which lays out the recycled page as it rotates, as a swipe does. A host's navigation uses
`SlideToPageAsync`, which differs in two ways.

**It slides onto any page, not only a neighbour.** The direction cue matters as much for a jump of a
year as for one of a week, and a cut to a far date left a host's "Today" button feeling unlike every
other way of moving. It is still one page of motion: the destination is laid out in the slot on the
side it lies — unless that slot already holds it — and slid onto. The pages in between are not slid
through; they were never rendered, and animating across them would only be a delay.

**The neighbours are laid out after the slide, not before it.** Until the slide finishes, the page
behind the centre is the one just left and the page beyond it is whatever the rotation recycled.
Laying them out first would put two pages of work between the request and the first frame of motion,
for pages that are off screen the whole time. `FinishNavigationSlide` lays them out once the animated
scroll completes, skipping any slot that already holds the right page — which is every slot, for an
adjacent destination.

That leaves the ring buffer briefly out of order, which has three consequences:

- *The pager takes no swipe during the slide.* A swipe back mid-slide would land on the page just
  left while the control believed it was the week before the destination. Repairing it on touch was
  rejected: the outgoing page is partly on screen at that moment, and swapping its contents shows as
  a jump. The slide lasts a few hundred milliseconds, so waiting it out is cheaper than anything
  that could be seen. A drag ending mid-slide respects this through `SetScrollingEnabled`.
- *`VisibleDatesChanged` derives the range from the centre page*, not from the slots either side.
  Reading them mid-slide after a jump of a year would ask the host for the whole year in between.
- *Only the latest slide finishes.* A second navigation mid-slide cuts the first one's scroll short,
  and the first one resuming would re-lay the page the second is sliding away from while it is on
  screen. A counter lets only the latest put the neighbours back. A slide cut off by unloading is
  finished there, because its scroll may never report back and the pager would stay locked.
- *A rebuild ends the slide.* `RebuildAll` lays out all three pages and recentres on its own, so a
  slide still waiting to animate — the jump has completed, the animated scroll is posted behind it —
  is stopped rather than allowed to resume. It captured the page width from before the rebuild, and
  a host setting the date and switching to the month in one handler would otherwise have the pager
  slide to the timeline's page width across the month's pages.

Note the asymmetry with `OnPageSettled`, which must stay synchronous and recentres with
`PagingScrollView.ScrollTo`: there the user has *already* moved the pages, so a frame drawn between
the rotation and the recentre shows a page nobody swiped to. Here the pages have not moved yet, and
the animation is the point.

