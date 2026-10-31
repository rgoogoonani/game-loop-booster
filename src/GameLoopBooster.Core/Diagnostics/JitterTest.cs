using System.Diagnostics;
using GameLoopBooster.Core.Native;

namespace GameLoopBooster.Core.Diagnostics;

public sealed record JitterResult(
    int Samples, double AvgMs, double P99Ms, double MaxMs, int SpikesOver500us, int SpikesOver1ms, double[] Lateness)
{
    /// <summary>Rough verdict for the UI: micro-stutter shows up as rare large spikes, not as the average.</summary>
    public string Verdict => MaxMs switch
    {
        < 0.3 when P99Ms < 0.1 => "عالی",
        < 1.0 when P99Ms < 0.25 => "خوب",
        < 2.5 => "متوسط — احتمال میکرو‌استاتر",
        _ => "ضعیف — مکث‌های سیستمی قابل‌توجه",
    };
}

/// <summary>
/// Measures scheduling jitter: a high-priority thread asks to wake up every 1 ms on a high-resolution
/// waitable timer and records how late each wake-up is. Long DPC/ISR bursts, core parking wake-ups and
/// power-state transitions all show up as spikes — the same things that cause micro-stutter in games.
/// </summary>
public static class JitterTest
{
    public static Task<JitterResult> RunAsync(TimeSpan duration, IProgress<double[]>? live, CancellationToken ct) =>
        Task.Factory.StartNew(() => Run(duration, live, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static JitterResult Run(TimeSpan duration, IProgress<double[]>? live, CancellationToken ct)
    {
        const double periodMs = 1.0;
        var thread = Thread.CurrentThread;
        var oldPriority = thread.Priority;
        thread.Priority = ThreadPriority.Highest;

        var timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
        if (timer == IntPtr.Zero) // pre-1803 Windows: fall back to a regular timer
            timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null, 0, NativeMethods.TIMER_ALL_ACCESS);

        var samples = new List<double>(capacity: (int)duration.TotalMilliseconds + 100);
        var batch = new List<double>(64);
        try
        {
            var sw = Stopwatch.StartNew();
            var tickToMs = 1000.0 / Stopwatch.Frequency;
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                long due = -(long)(periodMs * 10000); // relative, 100 ns units
                var before = sw.ElapsedTicks;
                NativeMethods.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
                NativeMethods.WaitForSingleObject(timer, 1000);
                var late = Math.Max(0, (sw.ElapsedTicks - before) * tickToMs - periodMs);
                samples.Add(late);
                batch.Add(late);
                if (batch.Count >= 50)
                {
                    live?.Report(batch.ToArray());
                    batch.Clear();
                }
            }
        }
        finally
        {
            NativeMethods.CloseHandle(timer);
            thread.Priority = oldPriority;
        }

        if (samples.Count == 0) return new JitterResult(0, 0, 0, 0, 0, 0, Array.Empty<double>());
        // The first few wake-ups include thread start-up noise.
        var data = samples.Skip(Math.Min(20, samples.Count / 10)).ToArray();
        var sorted = data.OrderBy(x => x).ToArray();
        return new JitterResult(
            data.Length,
            data.Average(),
            sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.99) - 1)],
            sorted[^1],
            data.Count(x => x > 0.5),
            data.Count(x => x > 1.0),
            data);
    }
}
