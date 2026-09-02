# 1. A separate project, with no Blazor in it

`Owlery.Maui.Scheduler` is a MAUI class library. It references `Microsoft.Maui.Controls` and nothing
else — no BlazorBindings, no app types, no `Owlery.*` contracts.

**Why.** Keeping the control free of the app's rendering stack makes it reusable and keeps its public
surface ordinary MAUI: bindable properties, CLR events, and a `DataTemplate` for the appointment box.

This is not only tidiness. The control is a public library and is intended to move to a repository of
its own, so every dependency on this solution is something that would have to be unpicked later.
`Owlery.Maui.Scheduler.Sample` exists to keep that honest: it is a bare MAUI app with no Blazor in it,
and if the control ever stops working there, it has grown a dependency on the host it should not
have.

The Blazor wrapper below is therefore a fact about *a consuming app*, not about the control. A plain
MAUI host is the baseline consumer; a Blazor one is a host that happens to generate its own wrapper,
in its own repository, from the published package.

Such a host consumes it through `BlazorBindings.Maui.ComponentGenerator`, declaring the wrapper on its
own side with an assembly attribute and generating it into its own tree:

```csharp
[assembly: GenerateComponent(typeof(SchedulerView),
    MakeItemsGeneric = false,
    PropertyChangedEvents = [nameof(SchedulerView.DisplayDate), nameof(SchedulerView.SelectedSlot)],
    GenericProperties = [$"{nameof(SchedulerView.AppointmentTemplate)}:Owlery.Maui.Scheduler.ISchedulerAppointment"])]
```

Two options are doing real work there:

- `GenericProperties` with a constraint type turns `AppointmentTemplate` into
  `RenderFragment<ISchedulerAppointment>`, so the Razor template gets a typed `context` instead of an
  untyped fragment. `CellSelectionTemplate` is deliberately left alone and becomes a plain
  `RenderFragment`, because one component can carry only one such context and the appointment is the
  one that matters.
- `MakeItemsGeneric = false` opts out of the generator's automatic handling of `ItemsSource`. That
  heuristic exists for `object`/`IList` collections; here `ItemsSource` is already
  `IEnumerable<ISchedulerAppointment>`, so without the opt-out the component becomes
  `SchedulerView<T>` with a type parameter nothing uses.

`PropertyChangedEvents` generates `DisplayDateChanged`, `SelectedSlotChanged` and
`VisibleDaysChanged` from `INotifyPropertyChanged`, which is what makes `@bind-DisplayDate` and its
siblings work — the control has no dedicated changed events.

`VisibleDays` is on that list for the reason any of them is: a host keeps its own copy to render
chrome from — a "Day / 3 days / Week" selector reads it — and a copy that only ever writes will
eventually disagree with the control. Two-way binding is what stops the label describing a view that
is not on screen. The same lesson, in its one-way form, is what made the sample's `VisibleDays` knob
go on reading 7 after a day header dropped the page to one day.

**A `RenderFragment` template is safe to pool**, which is not obvious. BlazorBindings' bridge
(`DataTemplateItemComponent`) creates one `ContentView` root per `CreateContent()` call and then
subscribes to its `BindingContextChanged`, calling `StateHasChanged` when the bound item changes. So
reassigning `BindingContext` on a pooled view re-renders that view's fragment through Blazor's normal
diff rather than rebuilding it. Rebinding and Blazor's rendering model agree here; they do not have to
be traded off against each other.

The practical consequence is that the app reuses its existing
`Pages/Schedule/Templates/WeekViewAppointment.razor` unchanged — the same appointment box the
Syncfusion schedule renders.

