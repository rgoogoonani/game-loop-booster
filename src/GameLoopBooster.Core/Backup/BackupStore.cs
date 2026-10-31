using GameLoopBooster.Core.Tweaks;
using GameLoopBooster.Core.Util;
using Microsoft.Win32;

namespace GameLoopBooster.Core.Backup;

/// <summary>
/// Holds the ORIGINAL value of everything this app has changed. An entry is written once, right before the
/// first change of that setting, and is never overwritten by later applies, so "restore" always means
/// "the way Windows was before GameLoop Booster".
/// </summary>
public sealed class BackupStore
{
    private readonly object _gate = new();
    private BackupFile _file;

    public BackupStore()
    {
        try { _file = Json.Load<BackupFile>(AppPaths.BackupFile) ?? new BackupFile(); }
        catch (Exception ex)
        {
            // Never silently drop a backup we cannot read: keep a copy and start fresh.
            Log.Error($"فایل بکاپ خراب است، یک نسخه از آن نگه داشته شد: {ex.Message}");
            try { File.Copy(AppPaths.BackupFile, AppPaths.BackupFile + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            _file = new BackupFile();
        }
    }

    public event Action? Changed;

    public IReadOnlyList<BackupEntry> Entries
    {
        get { lock (_gate) return _file.Entries.ToList(); }
    }

    public DateTime? Created => Entries.Count == 0 ? null : _file.Created;

    public bool Has(string tweakId)
    {
        lock (_gate) return _file.Entries.Any(e => e.TweakId == tweakId);
    }

    public BackupEntry? Get(string tweakId)
    {
        lock (_gate) return _file.Entries.FirstOrDefault(e => e.TweakId == tweakId);
    }

    /// <summary>Captures the original state of a tweak if it has not been captured before.</summary>
    public void EnsureCaptured(Tweak tweak)
    {
        lock (_gate)
        {
            if (_file.Entries.Any(e => e.TweakId == tweak.Id)) return;
            if (_file.Entries.Count == 0) _file.Created = DateTime.Now;
            _file.Entries.Add(tweak.Capture());
            Json.Save(AppPaths.BackupFile, _file);
        }
        if (tweak is RegistryTweak rt) ExportKey(rt.Hive, rt.KeyPath);
        Changed?.Invoke();
    }

    public void Remove(string tweakId)
    {
        lock (_gate)
        {
            _file.Entries.RemoveAll(e => e.TweakId == tweakId);
            Json.Save(AppPaths.BackupFile, _file);
        }
        Changed?.Invoke();
    }

    /// <summary>Restores one entry to the original value.</summary>
    public static void RestoreEntry(BackupEntry e)
    {
        switch (e.Kind)
        {
            case BackupKind.Registry: RegistryIO.Restore(e); break;
            case BackupKind.PowerSetting: PowerSettingTweak.Restore(e); break;
            case BackupKind.PowerPlan: PowerPlanTweak.Restore(e); break;
            case BackupKind.Command: CommandTweak.Restore(e); break;
        }
    }

    /// <summary>
    /// Extra safety net besides backup.json: a .reg export of each key before it is first touched,
    /// which can be double-clicked to restore by hand even without this app.
    /// </summary>
    private static void ExportKey(RegistryHive hive, string keyPath)
    {
        try
        {
            using var existing = RegistryIO.Open(hive, keyPath, writable: false);
            if (existing is null) return;
            Directory.CreateDirectory(AppPaths.RegExportDir);
            var safe = string.Concat((RegistryTweak.HiveName(hive) + "_" + keyPath).Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '\\' ? '_' : c));
            var file = Path.Combine(AppPaths.RegExportDir, safe.Length > 150 ? safe[^150..] : safe) + ".reg";
            if (File.Exists(file)) return;
            Shell.Run(Shell.SystemTool("reg.exe"), $"export \"{RegistryTweak.HiveName(hive)}\\{keyPath}\" \"{file}\" /y");
        }
        catch (Exception ex)
        {
            Log.Warn($"Export .reg ناموفق برای {keyPath}: {ex.Message}");
        }
    }

    /// <summary>Creates a Windows System Restore point (needs System Protection enabled on C:).</summary>
    public static ShellResult CreateRestorePoint()
    {
        // Windows rate-limits restore points to one per 24h unless this key is lowered.
        const string script = """
            $k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            $old = (Get-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
            Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
            try { Checkpoint-Computer -Description 'GameLoop Booster' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' }
            finally {
              if ($null -eq $old) { Remove-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
              else { Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value $old -Type DWord }
            }
            """;
        return Shell.PowerShell(script, 180000);
    }
}
