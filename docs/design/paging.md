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
| The host setting `DisplayDate` to the adjacent period | `TrySlideToPage` |

The last of those used to call `RebuildAll` — which repopulates all three pages, and lands on the new
period with no motion at all. Both halves of that are wrong for a destination that is *already
rendered one page away*: its appointment views exist and are bound, and rebuilding discards them only
to rent replacements for the same data; and a calendar that changes contents where it stands gives no
clue which direction it moved in. A host's "next week" button is one page forward exactly as much as a
swipe is.

`SlideToAdjacentPageAsync` is the shared implementation: rotate, then jump to where the outgoing page
has landed — visually identical to the frame before — and animate across to the centre. The jump is
unanimated and the slide is not, which is what turns a rotation into a swipe the user did not make.
Sliding through pages that were never rendered would be a lie, so anything further off than the two
neighbours is still `RebuildAll`.

Note the asymmetry with `OnPageSettled`, which must stay synchronous and recentres with
`PagingScrollView.ScrollTo`: there the user has *already* moved the pages, so a frame drawn between
the rotation and the recentre shows a page nobody swiped to. Here the pages have not moved yet, and
the animation is the point.

