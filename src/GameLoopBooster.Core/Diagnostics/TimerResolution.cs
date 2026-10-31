using GameLoopBooster.Core.Native;

namespace GameLoopBooster.Core.Diagnostics;

/// <summary>Kernel timer resolution. Values from ntdll are in 100 ns units.</summary>
public static class TimerResolution
{
    private static uint? _requested;

    public static (double MinMs, double MaxMs, double CurrentMs) Query()
    {
        NativeMethods.NtQueryTimerResolution(out var min, out var max, out var cur);
        // "minimum" in ntdll naming is the coarsest (15.6 ms), "maximum" the finest (0.5 ms).
        return (min / 10000.0, max / 10000.0, cur / 10000.0);
    }

    public static bool IsHeld => _requested is not null;

    /// <summary>Requests a finer timer for as long as this process lives (or until <see cref="Release"/>).</summary>
    public static double Request(double ms)
    {
        var (_, finest, _) = Query();
        var units = (uint)Math.Round(Math.Max(ms, finest) * 10000);
        if (_requested is not null && _requested != units) Release();
        NativeMethods.NtSetTimerResolution(units, true, out var actual);
        _requested = units;
        return actual / 10000.0;
    }

    public static void Release()
    {
        if (_requested is null) return;
        NativeMethods.NtSetTimerResolution(_requested.Value, false, out _);
        _requested = null;
    }
}
