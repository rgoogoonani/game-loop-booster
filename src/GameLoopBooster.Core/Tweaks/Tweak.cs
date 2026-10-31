using GameLoopBooster.Core.Backup;
using GameLoopBooster.Core.Hardware;

namespace GameLoopBooster.Core.Tweaks;

/// <summary>Safe = everyone, Aggressive = "max" profile, Expert = never auto-applied, only by hand in advanced mode.</summary>
public enum TweakLevel { Safe = 0, Aggressive = 1, Expert = 2 }

public enum TweakCategory { Scheduler, Power, Timer, Gpu, Memory, GameBar, Network, Emulator }

/// <summary>
/// One OS-level setting. Every tweak can capture the system's original state into a self-describing
/// <see cref="BackupEntry"/> (so it can be restored even if the tweak no longer exists in the catalog),
/// apply a target value, and read the live value back.
/// </summary>
public abstract class Tweak
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public TweakCategory Category { get; init; }
    public bool RequiresReboot { get; init; }

    /// <summary>The value this app recommends; the user can override it in advanced mode.</summary>
    public required string RecommendedValue { get; init; }

    /// <summary>Hint for advanced mode about which values are valid.</summary>
    public string ValueHint { get; init; } = "";

    /// <summary>Returns the profile level this tweak belongs to on the given hardware, or null if it does not fit this PC.</summary>
    public Func<HardwareInfo, TweakLevel?> Recommend { get; init; } = _ => TweakLevel.Safe;

    /// <summary>Human readable location (registry path, powercfg setting, command) for advanced mode.</summary>
    public abstract string Location { get; }

    public abstract string? ReadCurrent();
    public abstract BackupEntry Capture();
    public abstract void Apply(string target);

    public virtual string? Normalize(string? value) => value?.Trim();

    /// <summary>Whether a value read from the system already satisfies the target.</summary>
    public virtual bool Matches(string? current, string target) =>
        string.Equals(Normalize(current), Normalize(target), StringComparison.OrdinalIgnoreCase);

    public bool IsAt(string target)
    {
        try { return Matches(ReadCurrent(), target); }
        catch { return false; }
    }

    public virtual string Display(string? value) => value ?? "پیش‌فرض ویندوز (تعریف نشده)";
}
