# 19. The pager is our own scroll view

`PagingScrollView` replaces MAUI's `ScrollView` on the horizontal axis. It exists because MAUI's
cannot do two things this control needs, and cannot be made to.

**It cannot page.** Paging is a platform setting MAUI does not surface, so a week only ever snapped
on iOS, where `UIScrollView.PagingEnabled` was reached for through `Handler.PlatformView`. Android
had nothing, which is what NAV-7 was about.

**Its offset arrives late.** `ScrollToAsync` goes through the handler and lands on a later pass. That
is the gap a frame was drawn in, between rotating the ring buffer and recentring on it ([section 3](paging.md)).

Subclassing was not an option on Android. The platform view there is a
`MauiScrollView : NestedScrollView` that grows an internal `MauiHorizontalScrollView` child when a
horizontal orientation is asked for, so paging would have had to be imposed across two nested view
classes participating in nested scrolling, neither of them ours.

## What each half does

The cross-platform half owns the offset, the request plumbing and `PageSettled`. `ScrollTo` is
deliberately a method and deliberately not awaitable: the caller needs the offset *changed* on
return, not merely requested.

On **iOS** the handler is thin — a `UIScrollView` with `PagingEnabled`, `Bounces = false` and
`DelaysContentTouches = false`, all of which the scheduler used to set on it from the outside.

On **Android** the snap is written by hand, following React Native's `ReactHorizontalScrollView`: a
fling is never allowed to run its course, a throwaway `OverScroller` predicts where it *would* have
landed, and that point is rounded to the nearest page. Rounding the predicted landing rather than the
offset at release is what makes a short flick move a page while a slow drag does not.

The snap is animated by a scroller this control owns, **not** `SmoothScrollTo`. That drives the
scroller inside `HorizontalScrollView`, which cannot be reached to stop — so the recentre that
follows a settle was overwritten on the scroller's next frame, and one swipe compounded into
several. Owning it also makes "has it arrived" exact instead of polled for.

## Five things that were only found by running it

**Plain properties do not reach a handler.** `PageWidth` and `IsScrollEnabled` are not bindable —
nothing binds to them — so the mapper ran once at connect time, while `PageWidth` was still 0, and
never again. With no page width, every fling snapped to page 0 and swiping went *backwards*
regardless of direction. Both setters now call `Handler.UpdateValue`.

**An offset set before the first layout is lost.** UIKit accepts it — reading it straight back
reports exactly what was asked for — then resets it to zero once the view has a frame, so the
calendar opened one page early. Detecting that by reading the offset back therefore cannot work; the
absence of a frame is what identifies it. Android loses it the same way against a scroll range that
is still zero. Both handlers hold the request and re-apply it on layout.

**State reset on `ACTION_DOWN` is never reset.** A `ViewGroup` does not intercept the down event —
the drawing surface underneath consumes it, which is how long-press-to-drag gets its touch-down — and
interception only begins once a drag is recognised. So a flag cleared on DOWN inside the scroll view's
`OnTouchEvent` is cleared exactly never. The flag saying "this release already flung" therefore stayed
set after the first fling, and every slow release stopped snapping: the pager just stayed wherever the
finger left it, between two pages. Fast flings kept working throughout, because `Fling` snaps without
consulting it — which is what made the failure look like it depended on velocity. The flag is now
scoped to a single release rather than to a gesture.

**An animated scroll on Android completes nothing by itself.** iOS reports a programmatic animation
finishing through `scrollViewDidEndScrollingAnimation`; Android has no counterpart, so an awaited
`ScrollToAsync(..., animated: true)` never returned. Edge-paging mid-drag clears its "a slide is in
flight" flag only once that await comes back, so the flag stayed set and the drop target was never
resolved again — the appointment followed the finger and refused to snap, for the rest of the drag.
The handler now completes any pending request when its own animator stops.

**Android draws scroll content outside the scroll view.** MAUI leaves `ClipChildren` off on its
Android layout views so shadows can spill, which means the pager is drawn without being clipped to
its own bounds — and the pager's content is three pages wide with an opaque background, so the page
parked left of the viewport painted straight over the hour gutter beside it. Neither asking the
scroll view to clip nor clipping the grid around it puts that back. The gutter is drawn last with an
opaque background instead, which is what fixed chrome over scrolling content should have been doing
anyway.

## Measuring is not free once you own the handler

`PagingScrollView` is a `ContentView`, and MAUI would ordinarily give it `ContentViewHandler`, which
measures such a view **by measuring its content**. Writing our own handler on top of
`ViewHandler<PagingScrollView, MauiPagingScrollView>` quietly gave that up: the base implementation
asks the *platform* view how big it wants to be, and a scroll view — `UIScrollView` inheriting
`UIView.sizeThatFits`, and the Android equivalent — answers with its current bounds.

So the pager reported "as tall as I already am". It could still grow, because the hour gutter's own
`HeightRequest` pulls the surrounding grid row up and the pager fills the row, but nothing ever asked
it to shrink. Switching a timeline for a month, or reducing `HourHeight`, left the vertical scroll
view scrolling over a few hundred pixels of nothing. Measured on the simulator at `HourHeight` 60 → 20:

| | hour gutter | grid surface | pager |
|---|---|---|---|
| before | 900 → 300 | 900 → 300 | 900 → **900** |
| after | 900 → 300 | 900 → 300 | 900 → **300** |

`GetDesiredSize` now measures the cross-platform content, as `ContentViewHandler` would have. The
width is deliberately *not* the measured width: the content is three pages across and the pager is one
page wide, which is the entire point of it, so the constraint is returned instead.

The general lesson is worth more than the fix. **Taking over a handler takes over everything that
handler did**, including the parts that were never the reason for taking it over. The pager was
adopted for paging and offset control (above); its measurement came along silently and broke a month
that had not been written yet.

### And the override is iOS-only, which it was not at first

Applied to Android as well, it blanked the control completely. Every drawn thing — grid lines, day
separators, shading — vanished, and so did every appointment, while the hour gutter and the day
headers still rendered. The dividing line is the pager: everything inside it was unsized, everything
beside it was fine. The day headers also fell one page out of step with the title, because a
`PageSpan` of zero stacks all three of them at the same offset.

Measuring the cross-platform content from a handler satisfies MAUI's measure cache. On iOS nothing is
lost, because `LayoutSubviews` measures again on every pass. On Android it starves the pass that
matters: `MauiPagingContentViewGroup` measures *and then arranges* the same content, so a measure
already considered done leaves the arrange working from nothing. Constraining the width differently
does not help — measuring there at all is the problem. Android needs no counterpart, because its
platform view measures its child for itself.

The reason it survived review is worth recording too. The fix was verified on the emulator by reading
`ScrollView.ScrollY` across an injected swipe, and those numbers were correct: the scroll extents
really were fixed. Nothing in that check asked whether the control still drew anything, and a blank
grid scrolls exactly as well as a full one. It is the same trap as the tests in [`AGENTS.md`](../../Owlery.Maui.Scheduler/AGENTS.md) that passed
with and without the fix they were written for — measure the thing you changed, and you will not see
what you broke beside it.

### The gutter and the pager must be aligned the same way

Removing the override from Android exposed the defect underneath, which had been hidden the whole
time behind the blank grid. Whenever the hours are shorter than the screen — a small `HourHeight`, a
narrow day window — the scroll view leaves its row taller than the content, and the two platforms
distribute that slack differently. Measured on the emulator at `HourHeight` 24, with 360dp of hours in
a 675dp row:

| | height | placed at |
|---|---|---|
| `bodyGrid` (the row) | 675 | 0 |
| the surface, inside the pager | 360 | 0 |
| the gutter | 360 | **157.5** |

157.5 is exactly half the slack: Android stretches the pager and *centres* the gutter, which asks for
an explicit height. iOS centres the pager instead. Either way the two children of the row disagreed,
and the hour labels drifted away from the lines they name — by half the slack, in one direction on one
platform and the other direction on the other.

Both are pinned to `LayoutOptions.Start` for that reason. It is not a preference about where short
content should sit; it is the two of them having to agree, since one draws the hours and the other
labels them. Matching whichever alignment a platform happens to pick would only work on that platform.

## Registration

MAUI has no way for a library to register a handler on its own, so a host must call
`builder.UseOwleryScheduler()`. Without it the pager falls back to the handler for its base type and
simply does not scroll. That is the one line this control costs a host, and the reason it is worth
knowing about: everything else here is internal.

