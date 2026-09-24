namespace Owlery.Maui.Scheduler.Internal;

// PROBE: throwaway counters for the same-bounds recycling experiment. Read by the sample through
// AppContext so nothing public is added.
internal static class RecycleProbe
{
    public static int Gaps;
    public static int Matchable;

    // [origin * 2 + (sameBounds ? 1 : 0)] -> count, total ms
    private static readonly int[] counts = new int[6];
    private static readonly double[] totals = new double[6];

    static RecycleProbe() => AppContext.SetData("Owlery.Probe.Report", new Func<bool, string>(Report));

    public static void Record(byte origin, bool sameBounds, double ms)
    {
        var bucket = origin * 2 + (sameBounds ? 1 : 0);
        counts[bucket]++;
        totals[bucket] += ms;
    }

    private static string Report(bool reset)
    {
        string Bucket(string name, int b) =>
            counts[b] == 0 ? $"{name} 0" : $"{name} {counts[b]}×{totals[b] / counts[b]:F3}ms";

        var text = $"gaps {Gaps}, matchable {Matchable} | "
            + $"{Bucket("keyed/same", 1)}, {Bucket("keyed/moved", 0)}, "
            + $"{Bucket("surplus/same", 3)}, {Bucket("surplus/moved", 2)}, "
            + $"{Bucket("rented/same", 5)}, {Bucket("rented/moved", 4)}";

        if (reset)
        {
            Gaps = Matchable = 0;
            Array.Clear(counts);
            Array.Clear(totals);
        }

        return text;
    }
}
