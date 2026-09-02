# 13. Accessibility

Everything drawn on the `GraphicsView` is pixels; screen readers see one element. This is a genuine
cost of [section 7](grid-and-scrolling.md), and it is accepted deliberately: exposing 672 empty 15-minute cells as individually
focusable elements would make the calendar *worse* for screen-reader users, not better.

The accessible surface is therefore the meaningful elements only:

| Element | Implementation | Exposed |
|---|---|---|
| Grid lines, shading, hour labels | drawn | no |
| Day headers | real `Label`s | yes |
| Appointments | real views with `SemanticProperties.Description` | yes |
| Agenda month, week and day sections | real pooled views with `SemanticProperties.Description` | yes |
| Selected time slot | one real overlay view, described | yes |
| Add action | host toolbar | yes |

