using System.Diagnostics;
using Owlery.Maui.Scheduler.Internal;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// What one whole-collection refresh costs the control, per surface and per loaded volume.
/// </summary>
/// <remarks>
/// Explicit: it measures rather than asserts. A host that replaces <c>ItemsSource</c> once per
/// answered period pays this once per answer, so the per-refresh figure is the one that multiplies.
/// Run with <c>dotnet run --project Owlery.Maui.Scheduler.Tests -c Release -- --filter
/// "FullyQualifiedName~SchedulerProfile"</c>.
/// </remarks>
[Explicit("Measurement, not a test. Run deliberately.")]
internal class SchedulerProfile
{
    private const int Warmup = 5;
    private const int Runs = 40;

    [TestCase(SchedulerViewMode.Timeline, 7, 300)]
    [TestCase(SchedulerViewMode.Timeline, 7, 1200)]
    [TestCase(SchedulerViewMode.Timeline, 7, 3000)]
    [TestCase(SchedulerViewMode.Month, 7, 300)]
    [TestCase(SchedulerViewMode.Month, 7, 750)]
    [TestCase(SchedulerViewMode.Month, 7, 3000)]
    public void RefreshCost(SchedulerViewMode mode, int visibleDays, int loaded)
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var items = Generate(start, loaded);

        var harness = new SchedulerHarness(start, items, visibleDays, mode);

        var views = harness.VisibleAppointments.Count;

        // An identical collection, which is the ordinary case: a period answers with what the page
        // already had, and every view should be matched by key and left where it is.
        var same = items.ToArray();

        // A rebuilt one, where every appointment is a new instance — what a host that does not reuse
        // its adapters hands over.
        var rebuilt = Generate(start, loaded);

        TestContext.Out.WriteLine(
            $"""

            === {mode}, {visibleDays}d, {loaded} loaded, {views} views realized ===
            refresh, same instances   : {Measure(harness, same),8:F2} ms
            refresh, new instances    : {Measure(harness, rebuilt),8:F2} ms
            swipe (rotate + populate) : {MeasureSwipe(harness),8:F2} ms
            """);
    }

    /// <summary>The fastest of several whole-collection replacements, in milliseconds.</summary>
    private static double Measure(SchedulerHarness harness, ISchedulerAppointment[] items)
    {
        // Alternated, because assigning the identical reference twice is not a change at all.
        var other = items.ToArray();
        var best = double.MaxValue;

        for (var run = 0; run < Warmup + Runs; run++)
        {
            var next = run % 2 == 0 ? items : other;

            var watch = Stopwatch.StartNew();
            harness.Scheduler.ItemsSource = next;
            watch.Stop();

            if (run >= Warmup)
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    private static double MeasureSwipe(SchedulerHarness harness)
    {
        var best = double.MaxValue;

        for (var run = 0; run < Warmup + Runs; run++)
        {
            var watch = Stopwatch.StartNew();
            harness.SwipeToPage(run % 2 == 0 ? 2 : 0);
            watch.Stop();

            if (run >= Warmup)
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    /// <summary>A year of appointments around <paramref name="start"/>, spread through working days.</summary>
    private static ISchedulerAppointment[] Generate(DateTime start, int count)
    {
        var items = new ISchedulerAppointment[count];
        var origin = start.AddMonths(-6);

        for (var i = 0; i < count; i++)
        {
            var day = i % 365;
            var slot = i / 365;
            var begin = origin.AddDays(day).AddHours(9 + slot % 10).AddMinutes(slot % 2 * 30);

            items[i] = new TestAppointment(begin, begin.AddHours(1), $"Lesson {i}", Identity: $"k{i}");
        }

        return items;
    }
}
