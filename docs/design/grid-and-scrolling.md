# The drawn grid, and where scrolling lives

Why the background is one canvas rather than two thousand cell views, and why the vertical scroll
wraps the pager instead of sitting inside it.


A week at 15-minute resolution is 7 × 96 = 672 cells; three rendered weeks would be over 2,000 views
whose only job is to draw a line. The entire background — day-column shading, hour lines, half-hour
lines, day separators, the current-time indicator — is instead drawn on a single `GraphicsView`
(`SchedulerGridDrawable`).

**One `GraphicsView` for all three slots**, not one per week. The grid is nearly identical between
weeks; the drawable simply draws 21 day columns. Week-specific details (weekend shading, today's
highlight, the current-time line) are handled by redrawing on `Invalidate()` when the weeks rotate,
which is one draw call.

The **hour gutter is not drawn**, though it once was. It is a dozen or so `Label`s, positioned from
the same `YFromMinutes` the grid lines use so they cannot drift apart. The reasoning above does not
reach it: fourteen views are not two thousand, and drawing them cost real things — text that would
not scale with the system font size (the ACC-9 gap), and an obscured-label calculation to keep the
drag-time chip readable. The chip is opaque and the same height as a label, so it simply covers one.

Tapping empty space does not need cell views either — and in fact **all** input for the surface is
handled on the `GraphicsView`, including taps on appointments. Appointment views are
`InputTransparent`, and `HitTestAppointment` resolves a touch point against their known bounds; empty
space falls through to the surface's `SlotAt`, which converts the point to a slot, day and snapped
time arithmetically. [Section 11](interaction.md) explains why input is centralised here rather than left to per-appointment
gesture recognizers.

`SchedulerGeometry` is a single mutable object shared by the control, the drawable and the gutter
labels, so the drawn background and the positioned views can never disagree about where an hour line
sits.

---

## 8. Vertical scrolling lives outside the pager

```
Vertical ScrollView
└── time gutter │ Horizontal pager
                └── slot 0 │ slot 1 │ slot 2
```

Because one vertical scroll wraps all three weeks, **the vertical position does not change when the
week changes**. Looking at 14:00 and swiping to next week leaves the user at 14:00. Giving each page
its own vertical scroll would have required synchronising three scroll positions and would still drift.

The left time gutter is a sibling inside that same vertical scroll, so hour labels stay aligned with
the grid for free.

The day-header strip is the awkward one. It must not scroll vertically, so it cannot live inside the
vertical scroll — which means it also cannot live inside the pager, and therefore does not move when
the pager scrolls. It is the **only element in the control whose position is mirrored by hand**, via
`headerSurface.TranslationX = -e.ScrollX` on every `Scrolled` event. Everything else moves because the
platform scrolls it.

That makes the header the single point where a missed or unexpected scroll event becomes visible, and
two rules follow from it:

1. **The mirror runs unconditionally**, including while `recentring` is set. An earlier version
   returned early from the whole handler when recentring, which meant a programmatic scroll that did
   not settle exactly as expected left the strip parked at a stale offset — far enough off-screen to
   read as a missing header — until some later rebuild happened to reset it. Position mirroring and
   gesture detection are separate concerns and are now gated separately.
2. **The strip is `LayoutOptions.Start`**, not the default `Fill`. It is three viewports wide inside a
   one-viewport cell; with `Fill` the parent grid arranges it to the cell width rather than its
   requested width, which puts the second and third weeks' headers outside the arranged box.

### The viewport is measured off the body, not off the control

The width the columns divide is read from that vertical `ScrollView` — `ContentWidth` — rather than
from the width `OnSizeAllocated` reports. The two are not the same number, and the control's own
width is the wrong one.

Two things inset a control's content without narrowing the control. A host's `Padding` is the
obvious one. A display cutout is the other, and it is the one that was reported: on Android the
platform's inset is consumed by the **first layout inside the control** whenever nothing above it
did — which is exactly what happens when the control is a page's whole content, because then the
first layout is the control's own root `Grid`. That grid keeps its full width and places its
children past the inset.

So a control measuring from its own width laid seven columns across a strip wider than the one they
are drawn in, and the trailing column was clipped by exactly the inset. Two things about that were
initially misleading:

- **The clipping is at the trailing edge whichever edge the cutout is on.** It is a width error, not
  an offset one, and the columns are anchored at the content box's leading edge, so the overflow can
  only leave at the other end. Measured on an Android emulator with the cutout on the left and again
  on the right: the loss stayed on the right both times.
- **The amount lost equals the inset**, which makes it look positional. It is not — it is
  `control width − content width`, which is the inset by definition.

Opting the root grid out with `SafeAreaEdges.None` does not help: an unconsumed inset propagates to
the next layout down and is consumed there instead, still below the level `OnSizeAllocated` sees.
Verified on the sample — see [§15](verification.md).

The body reports `-1` until a platform has arranged it, and the headless tests have no handler to do
so, so `ContentWidth` falls back to the control's width less its own `Padding`. That fallback is not
a stub: padding is the half of this the control can work out for itself, and it is what the headless
test covers.

**The height is the same question down the other axis**, and `BodyHeight` answers it the same way.
The body *is* the viewport height — it is the control's content less the header row — so it is read
directly rather than derived from the control's height. Padding takes height from the content
without taking it from the control, and so does a bottom inset a page left unconsumed, which is then
taken below the level `OnSizeAllocated` sees. The fallback subtracts the padding and the *nominal*
header height; the body's own height accounts for the height the header row actually measured, which
is the more accurate of the two once there is one.

