using System.Diagnostics;
using Owlery.Maui.Scheduler.Sample.Data;

namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// A scripted workload the playground can run on a device, so paging and refreshing are measured
/// rather than guessed at.
/// </summary>
/// <remarks>
/// Two numbers per step, because they answer different questions. The stopwatch says what the
/// managed pass cost; the <see cref="FrameMeter"/> says what the display did around it, which is
/// where the layout and the native arranging the pass only *queued* actually land.
/// </remarks>
public static class PerfRun
{
    private static readonly List<double> hostCallbacks = [];

    /// <summary>What the host spent inside a <c>VisibleDatesChanged</c> the control raised.</summary>
    public static void RecordHostCallback(double milliseconds) => hostCallbacks.Add(milliseconds);

    /// <summary>Pages forward and back, the way a reader flicking through a calendar does.</summary>
    public static async Task<string> PageAsync(SchedulerView scheduler, int steps = 12)
    {
        var meter = new FrameMeter();
        var managed = new List<double>();

        hostCallbacks.Clear();

        // A settled starting point: the first page of a mode is built on the layout pass, and
        // measuring across it would report the build rather than the paging.
        await Task.Delay(400);

        meter.Start();

        for (var i = 0; i < steps; i++)
        {
            var direction = i < steps / 2 ? 1 : -1;

            var watch = Stopwatch.StartNew();

            scheduler.DisplayDate = scheduler.ViewMode is SchedulerViewMode.Month
                ? scheduler.DisplayDate.AddMonths(direction)
                : scheduler.DisplayDate.AddDays(direction * scheduler.VisibleDays);

            watch.Stop();
            managed.Add(watch.Elapsed.TotalMilliseconds);

            // Long enough for the frame the write asked for to be drawn and presented.
            await Task.Delay(120);
        }

        var frames = meter.Stop();

        var host = hostCallbacks.Count == 0 ? 0 : Median(hostCallbacks);

        return $"page ×{steps}: managed {Median(managed):F2} ms (host {host:F2} of it, "
            + $"{hostCallbacks.Count} callbacks) worst {managed.Max():F2} | {frames}";
    }

    /// <summary>
    /// Replaces the whole collection repeatedly, which is what a host that publishes per answered
    /// period makes the control do.
    /// </summary>
    public static async Task<string> RefreshAsync(SchedulerView scheduler, AppointmentSource source, int times = 12)
    {
        if (scheduler.ItemsSource is not IEnumerable<ISchedulerAppointment> loaded)
            return "refresh: nothing loaded";

        var a = loaded.ToArray();
        var b = loaded.ToArray();
        var meter = new FrameMeter();
        var managed = new List<double>();

        await Task.Delay(400);
        meter.Start();

        for (var i = 0; i < times; i++)
        {
            var watch = Stopwatch.StartNew();
            scheduler.ItemsSource = i % 2 == 0 ? a : b;
            watch.Stop();
            managed.Add(watch.Elapsed.TotalMilliseconds);

            await Task.Delay(120);
        }

        var frames = meter.Stop();

        // Put the host's own collection back, so the run leaves the playground as it found it.
        scheduler.ItemsSource = null;
        source.Republish();

        return $"refresh ×{times}: managed {Median(managed):F2} ms (worst {managed.Max():F2}) | {frames}";
    }

    /// <summary>
    /// The burst a host publishing one collection per answered month produces: several whole
    /// replacements inside one run-loop turn, then a pause.
    /// </summary>
    public static async Task<string> BurstAsync(SchedulerView scheduler, AppointmentSource source, int bursts = 8, int perBurst = 5)
    {
        if (scheduler.ItemsSource is not IEnumerable<ISchedulerAppointment> loaded)
            return "burst: nothing loaded";

        var copies = Enumerable.Range(0, perBurst).Select(_ => loaded.ToArray()).ToArray();
        var meter = new FrameMeter();

        await Task.Delay(400);
        meter.Start();

        var watch = Stopwatch.StartNew();

        for (var burst = 0; burst < bursts; burst++)
        {
            for (var i = 0; i < perBurst; i++)
            {
                scheduler.ItemsSource = copies[i];

                // Each answered period lands on its own turn of the run loop, which is what stops the
                // control collapsing the burst into one rebuild.
                await Task.Yield();
            }

            await Task.Delay(200);
        }

        watch.Stop();
        var frames = meter.Stop();

        scheduler.ItemsSource = null;
        source.Republish();

        return $"burst {bursts}×{perBurst}: {watch.Elapsed.TotalMilliseconds:F0} ms total | {frames}";
    }

    /// <summary>
    /// The whole matrix, unattended: every surface at every volume worth knowing about.
    /// </summary>
    /// <remarks>
    /// Driven from a launch argument rather than the button because a swipe cannot be injected on
    /// iOS — DevFlow reports native pan injection as unavailable there — so a run that needed a
    /// finger could not be repeated. Paging by <c>DisplayDate</c> exercises the same rotation and
    /// repopulate a swipe settles into.
    /// </remarks>
    public static async Task RunMatrixAsync(
        SchedulerView scheduler, AppointmentSource source, Action<string> report)
    {
        (SchedulerViewMode Mode, int Days, int PerMonth)[] cases =
        [
            (SchedulerViewMode.Timeline, 7, 25),
            (SchedulerViewMode.Timeline, 7, 100),
            (SchedulerViewMode.Timeline, 7, 500),
            (SchedulerViewMode.Timeline, 1, 100),
            (SchedulerViewMode.Month, 7, 25),
            (SchedulerViewMode.Month, 7, 100),
            (SchedulerViewMode.Month, 7, 500),
        ];

        foreach (var (mode, days, perMonth) in cases)
        {
            scheduler.ViewMode = mode;
            scheduler.VisibleDays = days;
            source.SetCount(perMonth);

            // The mode change rebuilds every page; measuring across it would report the build.
            await Task.Delay(900);

            report($"--- {mode} {days}d, {perMonth}/mo, {source.LoadedAppointmentCount} loaded ---");
            report(await PageAsync(scheduler));
            report(await RefreshAsync(scheduler, source));
            report(await BurstAsync(scheduler, source));
        }

        report("--- matrix done ---");
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted[sorted.Length / 2];
    }
}
