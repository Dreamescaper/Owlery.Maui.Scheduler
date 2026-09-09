namespace Owlery.Maui.Scheduler.Sample.Playground;

/// <summary>
/// What the display actually delivered while something was happening.
/// </summary>
/// <remarks>
/// A stopwatch around managed work cannot see a dropped frame: laying a page out only queues the
/// draw, and the frame that misses its deadline misses it afterwards. This listens to the display
/// itself — <c>CADisplayLink</c> on iOS, a frame callback on Android — and reports the intervals
/// between the frames that were presented, which is the number a reader perceives as smooth or not.
/// </remarks>
public sealed class FrameMeter : IDisposable
{
    private readonly List<double> intervals = [];
    private double last;

#if IOS
    private CoreAnimation.CADisplayLink? link;
#elif ANDROID
    private FrameCallback? callback;
#endif

    /// <summary>The refresh interval the display is running at, in milliseconds.</summary>
    public double FrameBudgetMs { get; private set; } = 1000.0 / 60;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;

        intervals.Clear();
        last = 0;
        IsRunning = true;

#if IOS
        link = CoreAnimation.CADisplayLink.Create(OnFrame);
        link.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);

        if (UIKit.UIScreen.MainScreen.MaximumFramesPerSecond > 0)
            FrameBudgetMs = 1000.0 / UIKit.UIScreen.MainScreen.MaximumFramesPerSecond;
#elif ANDROID
        callback = new FrameCallback(OnAndroidFrame);
        Android.Views.Choreographer.Instance!.PostFrameCallback(callback);
#endif
    }

    public FrameReport Stop()
    {
        IsRunning = false;

#if IOS
        link?.Invalidate();
        link?.Dispose();
        link = null;
#elif ANDROID
        if (callback is not null)
            Android.Views.Choreographer.Instance!.RemoveFrameCallback(callback);

        callback = null;
#endif

        return FrameReport.From(intervals, FrameBudgetMs);
    }

    public void Dispose() => Stop();

    private void Record(double timestampSeconds)
    {
        if (last > 0)
            intervals.Add((timestampSeconds - last) * 1000);

        last = timestampSeconds;
    }

#if IOS
    private void OnFrame()
    {
        if (link is not null)
            Record(link.Timestamp);
    }
#elif ANDROID
    private void OnAndroidFrame(long frameTimeNanos)
    {
        Record(frameTimeNanos / 1_000_000_000.0);

        if (IsRunning && callback is not null)
            Android.Views.Choreographer.Instance!.PostFrameCallback(callback);
    }

    private sealed class FrameCallback(Action<long> onFrame)
        : Java.Lang.Object, Android.Views.Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) => onFrame(frameTimeNanos);
    }
#endif
}

/// <summary>What a run of frames came out at, in the terms a jank question is asked in.</summary>
public readonly record struct FrameReport(
    int Frames,
    double MedianMs,
    double P95Ms,
    double P99Ms,
    double WorstMs,
    int Dropped,
    double BudgetMs)
{
    /// <summary>A frame is late once it has cost more than one and a half refresh intervals.</summary>
    private const double LateFactor = 1.5;

    public static FrameReport From(IReadOnlyList<double> intervals, double budgetMs)
    {
        if (intervals.Count == 0)
            return new FrameReport(0, 0, 0, 0, 0, 0, budgetMs);

        var sorted = intervals.OrderBy(interval => interval).ToArray();

        return new FrameReport(
            sorted.Length,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            intervals.Count(interval => interval > budgetMs * LateFactor),
            budgetMs);
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)(sorted.Length * fraction), 0, sorted.Length - 1)];

    public override string ToString() =>
        $"{Frames} frames, p50 {MedianMs:F1} p95 {P95Ms:F1} p99 {P99Ms:F1} worst {WorstMs:F1} ms, "
        + $"{Dropped} late ({(Frames == 0 ? 0 : 100.0 * Dropped / Frames):F1}%), budget {BudgetMs:F1} ms";
}
