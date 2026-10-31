using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using GameLoopBooster.Core.Diagnostics;
using GameLoopBooster.Core.Hardware;
using GameLoopBooster.Core.Native;
using GameLoopBooster.Core.Settings;
using GameLoopBooster.Core.Tweaks;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.Core.Processes;

public sealed record ManagedProcess(
    string Name, int Pid, string Priority, string Affinity, bool Optimized, string Status);

/// <summary>
/// Watches for GameLoop's processes and applies affinity, priority and power-throttling opt-out from the
/// outside, exactly like Task Manager or Process Lasso would. Nothing is injected into or read from the
/// processes; if a process is protected the change is skipped and reported.
/// </summary>
public sealed class ProcessOptimizer : IDisposable
{
    private readonly HardwareInfo _hw;
    private readonly Func<AppSettings> _settings;
    private readonly Dictionary<int, string> _seen = new();
    private System.Threading.Timer? _timer;
    private string? _planBeforeSession;
    private int _busy;

    public ProcessOptimizer(HardwareInfo hw, Func<AppSettings> settings)
    {
        _hw = hw;
        _settings = settings;
    }

    public bool GameRunning { get; private set; }
    public IReadOnlyList<ManagedProcess> Processes { get; private set; } = Array.Empty<ManagedProcess>();
    public event Action? Updated;
    public event Action<bool>? GameStateChanged;

    public bool Watching => _timer is not null;

    public void Start()
    {
        _timer ??= new System.Threading.Timer(_ => Tick(), null, 0, 2000);
        Log.Info("مانیتور GameLoop فعال شد");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        EndSession();
        Log.Info("مانیتور GameLoop متوقف شد");
    }

    public void ApplyNow() => Task.Run(Tick);

    public ulong MaskFor(ProcessRule rule) => rule.Affinity switch
    {
        AffinityMode.Preferred => _hw.PreferredMask,
        AffinityMode.PreferredNoSmt => _hw.PreferredMaskNoSmt,
        AffinityMode.Secondary => _hw.SecondaryMask,
        AffinityMode.All => _hw.AllMask,
        AffinityMode.Custom when TryParseMask(rule.CustomMask, out var m) => m & _hw.AllMask,
        _ => 0,
    };

    public static bool TryParseMask(string text, out ulong mask)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask) && mask != 0;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var settings = _settings();
            var list = new List<ManagedProcess>();
            var anyGame = false;
            var alive = new HashSet<int>();

            foreach (var rule in settings.ProcessRules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.ProcessName)))
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(rule.ProcessName.Trim())))
                {
                    using (p)
                    {
                        alive.Add(p.Id);
                        anyGame |= rule.IsGame;
                        list.Add(settings.AutoOptimizeProcesses ? Optimize(p, rule) : Describe(p, rule, false, "مانیتور خودکار خاموش است"));
                    }
                }
            }

            foreach (var gone in _seen.Keys.Where(k => !alive.Contains(k)).ToList()) _seen.Remove(gone);

            if (anyGame != GameRunning)
            {
                GameRunning = anyGame;
                if (anyGame) BeginSession(settings); else EndSession();
                GameStateChanged?.Invoke(anyGame);
            }

            Processes = list;
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"مانیتور پردازه‌ها: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private ManagedProcess Optimize(Process p, ProcessRule rule)
    {
        var problems = new List<string>();
        try
        {
            var mask = MaskFor(rule);
            if (mask != 0 && (ulong)p.ProcessorAffinity.ToInt64() != mask)
                p.ProcessorAffinity = new IntPtr(unchecked((long)mask));
        }
        catch (Exception ex) { problems.Add("Affinity: " + Short(ex)); }

        try
        {
            var prio = ToClass(rule.Priority);
            if (prio is not null && p.PriorityClass != prio) p.PriorityClass = prio.Value;
        }
        catch (Exception ex) { problems.Add("Priority: " + Short(ex)); }

        if (rule.DisableThrottling && !_seen.ContainsKey(p.Id) && !DisableThrottling(p.Id))
            problems.Add("EcoQoS: دسترسی نشد");

        var first = !_seen.ContainsKey(p.Id);
        _seen[p.Id] = rule.ProcessName;
        if (first)
        {
            if (problems.Count == 0) Log.Success($"{rule.ProcessName} (PID {p.Id}) بهینه شد");
            else Log.Warn($"{rule.ProcessName} (PID {p.Id}): {string.Join(" · ", problems)}");
        }

        return Describe(p, rule, problems.Count == 0, problems.Count == 0 ? "بهینه" : string.Join(" · ", problems));
    }

    private ManagedProcess Describe(Process p, ProcessRule rule, bool ok, string status)
    {
        string prio, aff;
        try { prio = p.PriorityClass.ToString(); } catch { prio = "?"; }
        try { aff = DescribeMask((ulong)p.ProcessorAffinity.ToInt64()); } catch { aff = "?"; }
        return new ManagedProcess(rule.ProcessName, p.Id, prio, aff, ok, status);
    }

    public string DescribeMask(ulong mask)
    {
        if (mask == _hw.AllMask) return $"همه ({_hw.LogicalProcessors})";
        var count = BitOperations.PopCount(mask);
        if (mask == _hw.PreferredMask)
            return _hw.Layout == CpuLayout.IntelHybrid ? $"P-Cores ({count})" : $"CCD اصلی ({count})";
        if (mask == _hw.PreferredMaskNoSmt) return $"هسته اصلی بدون HT ({count})";
        if (mask == _hw.SecondaryMask)
            return _hw.Layout == CpuLayout.IntelHybrid ? $"E-Cores ({count})" : $"CCD دوم ({count})";
        return $"0x{mask:X} ({count})";
    }

    private static bool DisableThrottling(int pid)
    {
        var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
            {
                Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | NativeMethods.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                StateMask = 0,
            };
            var size = (uint)Marshal.SizeOf(state);
            if (NativeMethods.SetProcessInformation(h, NativeMethods.ProcessPowerThrottling, ref state, size)) return true;
            // IGNORE_TIMER_RESOLUTION only exists on Windows 11; retry with execution speed alone.
            state.ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
            return NativeMethods.SetProcessInformation(h, NativeMethods.ProcessPowerThrottling, ref state, size);
        }
        finally { NativeMethods.CloseHandle(h); }
    }

    private void BeginSession(AppSettings s)
    {
        Log.Info("GameLoop اجرا شد — جلسه بهینه‌سازی شروع شد");
        if (s.HighResolutionTimer)
        {
            try
            {
                var actual = TimerResolution.Request(s.TimerResolutionMs);
                Log.Success($"دقت تایمر ویندوز روی {actual:0.###} ms تنظیم شد");
            }
            catch (Exception ex) { Log.Warn($"تایمر: {ex.Message}"); }
        }
        if (s.SessionPowerPlan)
        {
            try
            {
                _planBeforeSession = PowerCfg.ActiveScheme();
                var target = PowerCfg.EnsureUltimate();
                if (target != _planBeforeSession)
                {
                    PowerCfg.SetActive(target);
                    Log.Success("پاورپلن Ultimate برای مدت بازی فعال شد");
                }
                else _planBeforeSession = null;
            }
            catch (Exception ex) { Log.Warn($"پاورپلن: {ex.Message}"); _planBeforeSession = null; }
        }
    }

    private void EndSession()
    {
        if (TimerResolution.IsHeld)
        {
            TimerResolution.Release();
            Log.Info("دقت تایمر به حالت عادی برگشت");
        }
        if (_planBeforeSession is not null)
        {
            try { PowerCfg.SetActive(_planBeforeSession); Log.Info("پاورپلن قبلی برگردانده شد"); }
            catch (Exception ex) { Log.Warn($"پاورپلن: {ex.Message}"); }
            _planBeforeSession = null;
        }
    }

    private static ProcessPriorityClass? ToClass(PriorityLevel p) => p switch
    {
        PriorityLevel.Idle => ProcessPriorityClass.Idle,
        PriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
        PriorityLevel.Normal => ProcessPriorityClass.Normal,
        PriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
        PriorityLevel.High => ProcessPriorityClass.High,
        _ => null,
    };

    private static string Short(Exception ex) => ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
        ? "دسترسی رد شد (پردازه محافظت‌شده)"
        : ex.Message;

    public void Dispose() => Stop();
}
