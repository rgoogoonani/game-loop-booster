namespace GameLoopBooster.Core.Processes;

public enum AffinityMode
{
    /// <summary>Leave affinity alone.</summary>
    Unchanged,
    /// <summary>P-cores on Intel hybrid, the V-Cache / first CCD on AMD, all cores otherwise.</summary>
    Preferred,
    /// <summary>Preferred cores without SMT/Hyper-Threading siblings.</summary>
    PreferredNoSmt,
    /// <summary>E-cores on Intel hybrid, the other CCD on AMD.</summary>
    Secondary,
    All,
    /// <summary>Hex bit mask typed by the user.</summary>
    Custom,
}

public enum PriorityLevel { Unchanged, Idle, BelowNormal, Normal, AboveNormal, High }

public sealed class ProcessRule
{
    public string ProcessName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public AffinityMode Affinity { get; set; } = AffinityMode.Preferred;
    public string CustomMask { get; set; } = "";
    public PriorityLevel Priority { get; set; } = PriorityLevel.High;

    /// <summary>Opt the process out of EcoQoS / power throttling.</summary>
    public bool DisableThrottling { get; set; } = true;

    /// <summary>Marks the processes whose presence means "GameLoop is running".</summary>
    public bool IsGame { get; set; }

    public static List<ProcessRule> Defaults() => new()
    {
        // The emulator engine: heaviest work, renders the game.
        new() { ProcessName = "aow_exe.exe", Affinity = AffinityMode.Preferred, Priority = PriorityLevel.High, IsGame = true },
        // UI shells that host the game window.
        new() { ProcessName = "AndroidEmulatorEx.exe", Affinity = AffinityMode.Preferred, Priority = PriorityLevel.AboveNormal, IsGame = true },
        new() { ProcessName = "AndroidEmulatorEn.exe", Affinity = AffinityMode.Preferred, Priority = PriorityLevel.AboveNormal, IsGame = true },
        new() { ProcessName = "AndroidEmulator.exe", Affinity = AffinityMode.Preferred, Priority = PriorityLevel.AboveNormal, IsGame = true },
        // Launcher, store, web renderer and services: push them out of the way.
        new() { ProcessName = "AppMarket.exe", Affinity = AffinityMode.Secondary, Priority = PriorityLevel.BelowNormal, DisableThrottling = false },
        new() { ProcessName = "QMEmuSvc.exe", Affinity = AffinityMode.Secondary, Priority = PriorityLevel.BelowNormal, DisableThrottling = false },
        new() { ProcessName = "TBSWebRenderer.exe", Affinity = AffinityMode.Secondary, Priority = PriorityLevel.BelowNormal, DisableThrottling = false },
        new() { ProcessName = "QMEmulatorService.exe", Affinity = AffinityMode.Secondary, Priority = PriorityLevel.BelowNormal, DisableThrottling = false },
        new() { ProcessName = "syzs_dl_svr.exe", Affinity = AffinityMode.Secondary, Priority = PriorityLevel.BelowNormal, DisableThrottling = false },
    };
}
