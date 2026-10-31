using System.Diagnostics;
using GameLoopBooster.Core.Native;

namespace GameLoopBooster.Core.Diagnostics;

/// <summary>A live batch of jitter values (ms above the baseline) and how far the test has run (0..1).</summary>
public sealed record JitterBatch(double[] Values, double Fraction);

public sealed record JitterResult(
    int Samples, double BaselineMs, double AvgMs, double P99Ms, double MaxMs,
    int StallsOver1ms, int StallsOver4ms, double DurationSec, double[] Jitter)
{
    public double StallsOver1msPerMinute => DurationSec <= 0 ? 0 : StallsOver1ms * 60 / DurationSec;
    public double StallsOver4msPerMinute => DurationSec <= 0 ? 0 : StallsOver4ms * 60 / DurationSec;

    /// <summary>
    /// Micro-stutter is about rare long stalls, so the verdict looks at p99 and at how often wake-ups were
    /// held up by more than 1 ms and 4 ms (a 4 ms stall is a whole frame at 240 FPS), not at the average.
    /// </summary>
    public string Verdict
    {
        get
        {
            var big = StallsOver4msPerMinute;
            var mid = StallsOver1msPerMinute;
            var grade = (P99Ms, big, mid) switch
            {
                (< 0.25, 0, < 10) => "عالی",
                (< 0.5, <= 1, < 120) => "خوب",
                (< 1.0, <= 5, _) => "متوسط — احتمال میکرو‌استاتر",
                _ => "ضعیف — مکث‌های سیستمی زیاد",
            };
            return StallsOver4ms > 0 && grade is "عالی" or "خوب"
                ? $"{grade} — ولی {StallsOver4ms} مکث بزرگ دیده شد"
                : grade;
        }
    }
}

/// <summary>
/// Measures scheduling jitter: a high-priority thread asks to wake up every 1 ms on a high-resolution
/// waitable timer and records how late each wake-up is. Every wake-up is a little late by a constant
/// amount that depends on the timer tick (≈0.5 ms with a 0.5 ms timer), so that median lateness is
/// taken as the baseline and only the part above it counts as jitter. Long DPC/ISR bursts, core parking
/// wake-ups and power-state transitions show up as spikes above the baseline, the same things that cause
/// micro-stutter in games.
/// </summary>
public static class JitterTest
{
    private const int BaselineSamples = 300;

    /// <param name="realtime">
    /// Runs the probe thread in the realtime priority class so that no ordinary thread (including the game,
    /// which this app raises to High) can delay it. What is left is time stolen by drivers (DPC/ISR) and
    /// firmware. With <c>false</c> the probe runs at a normal high priority and also shows how long ordinary
    /// threads wait for a CPU while the game is busy.
    /// </param>
    public static Task<JitterResult> RunAsync(TimeSpan duration, bool realtime, IProgress<JitterBatch>? live, CancellationToken ct) =>
        Task.Factory.StartNew(() => Run(duration, realtime, live, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static JitterResult Run(TimeSpan duration, bool realtime, IProgress<JitterBatch>? live, CancellationToken ct)
    {
        const double periodMs = 1.0;
        var thread = Thread.CurrentThread;
        var oldPriority = thread.Priority;
        using var process = Process.GetCurrentProcess();
        var oldClass = process.PriorityClass;
        // The probe sleeps almost all the time, so a realtime class for the length of the test is harmless.
        if (realtime) process.PriorityClass = ProcessPriorityClass.RealTime;
        thread.Priority = ThreadPriority.Highest;

        var timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
        if (timer == IntPtr.Zero) // pre-1803 Windows: fall back to a regular timer
            timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null, 0, NativeMethods.TIMER_ALL_ACCESS);

        var samples = new List<double>(capacity: (int)duration.TotalMilliseconds + 100);
        var batch = new List<double>(64);
        double? liveBaseline = null;
        var sw = Stopwatch.StartNew();
        try
        {
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

                if (liveBaseline is null && samples.Count >= BaselineSamples)
                    liveBaseline = Median(samples);
                if (batch.Count >= 50 && liveBaseline is { } b)
                {
                    live?.Report(new JitterBatch(batch.Select(x => Math.Max(0, x - b)).ToArray(), sw.Elapsed / duration));
                    batch.Clear();
                }
            }
        }
        finally
        {
            NativeMethods.CloseHandle(timer);
            thread.Priority = oldPriority;
            if (realtime) process.PriorityClass = oldClass;
        }

        var seconds = sw.Elapsed.TotalSeconds;
        if (samples.Count < 20) return new JitterResult(0, 0, 0, 0, 0, 0, 0, seconds, Array.Empty<double>());

        // The first few wake-ups include thread start-up noise.
        var data = samples.Skip(Math.Min(20, samples.Count / 10)).ToArray();
        var baseline = Median(data);
        var jitter = data.Select(x => Math.Max(0, x - baseline)).ToArray();
        var sorted = jitter.OrderBy(x => x).ToArray();
        return new JitterResult(
            jitter.Length,
            baseline,
            jitter.Average(),
            sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.99) - 1)],
            sorted[^1],
            jitter.Count(x => x > 1.0),
            jitter.Count(x => x > 4.0),
            seconds,
            jitter);
    }

    private static double Median(IReadOnlyCollection<double> values)
    {
        var s = values.OrderBy(x => x).ToArray();
        return s[s.Length / 2];
    }
}
