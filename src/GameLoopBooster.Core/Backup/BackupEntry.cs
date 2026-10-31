namespace GameLoopBooster.Core.Backup;

public enum BackupKind { Registry, PowerSetting, PowerPlan, Command }

/// <summary>
/// The original state of one setting, written before the first change. It carries everything needed to
/// put the value back, so restoring never depends on the current tweak catalog or hardware.
/// </summary>
public sealed class BackupEntry
{
    public string TweakId { get; set; } = "";
    public string Title { get; set; } = "";
    public BackupKind Kind { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    /// <summary>False when the value did not exist (restore deletes it instead of writing it).</summary>
    public bool Existed { get; set; }
    public string? Value { get; set; }

    // Registry
    public string? Hive { get; set; }
    public string? KeyPath { get; set; }
    public string? ValueName { get; set; }
    public string? ValueKind { get; set; }

    // Power settings
    public string? SchemeGuid { get; set; }
    public string? SubGroupGuid { get; set; }
    public string? SettingGuid { get; set; }
}

public sealed class BackupFile
{
    public int Version { get; set; } = 1;
    public DateTime Created { get; set; } = DateTime.Now;
    public string MachineName { get; set; } = Environment.MachineName;
    public List<BackupEntry> Entries { get; set; } = new();
}
