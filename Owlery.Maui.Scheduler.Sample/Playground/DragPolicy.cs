namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// The two host decisions the cancellable drag events exist for. Kept out of the control on purpose:
/// it asks, the host answers.
/// </summary>
public sealed class DragPolicy
{
    /// <summary>Refuse to pick up appointments the sample generator marked locked.</summary>
    public bool BlockLocked { get; set; } = true;

    /// <summary>Refuse every drop, sending appointments back where they came from.</summary>
    public bool RejectDrops { get; set; }
}
