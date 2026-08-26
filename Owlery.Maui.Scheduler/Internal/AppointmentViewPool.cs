namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// A pool of appointment views shared by every rendered week.
/// </summary>
/// <remarks>
/// Spare views stay attached to <paramref name="host"/> with <c>IsVisible = false</c>, so renting one
/// for a different week never reparents it and never rebuilds its (potentially deep) subtree.
/// <para>
/// The pool is deliberately unbounded. When the template comes from Blazor, every
/// <see cref="DataTemplate.CreateContent"/> permanently registers a template root with the renderer,
/// which detaching the view would not release — so discarding a spare would leak the Blazor side and
/// force a fresh registration next time. Retaining every view is strictly cheaper.
/// </para>
/// </remarks>
internal sealed class AppointmentViewPool(AbsoluteLayout host)
{
    private readonly Stack<View> spare = new();

    public DataTemplate? Template { get; set; }

    /// <summary>Invoked once per view, right after creation — the place to attach gesture recognizers.</summary>
    public Action<View>? ViewCreated { get; set; }

    public View? Rent()
    {
        if (spare.Count > 0)
        {
            var pooled = spare.Pop();
            pooled.IsVisible = true;
            return pooled;
        }

        if (Template?.CreateContent() is not View created)
            return null;

        host.Add(created);
        ViewCreated?.Invoke(created);
        return created;
    }

    public void Return(View view)
    {
        view.IsVisible = false;
        view.BindingContext = null;
        view.TranslationX = 0;
        view.TranslationY = 0;
        view.Opacity = 1;
        view.ZIndex = 0;

        spare.Push(view);
    }

    /// <summary>Drops every pooled view, e.g. after the template changes.</summary>
    public void Clear()
    {
        while (spare.Count > 0)
            host.Remove(spare.Pop());
    }
}

/// <summary>One of the three rendered pages. Its identity is stable; only the days it shows rotate.</summary>
internal sealed class PageSlot
{
    public DateOnly PageStart { get; set; }

    public List<View> Views { get; } = [];

    /// <summary>Where each of <see cref="Views"/> sits, kept so they can be re-placed without a re-layout.</summary>
    public List<IAppointmentPlacement> Positions { get; } = [];

    /// <summary>What this page could not fit, per cell. Empty on a surface where nothing overflows.</summary>
    public IReadOnlyList<int> Overflow { get; set; } = [];

    /// <summary>The day headers for this page, moved as a unit.</summary>
    public required Grid Header { get; init; }

    public Label[] DayNameLabels { get; set; } = [];

    public Label[] DayNumberLabels { get; set; } = [];
}
