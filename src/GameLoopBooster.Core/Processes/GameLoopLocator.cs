using System.Diagnostics;
using System.Text;
using GameLoopBooster.Core.Native;
using Microsoft.Win32;

namespace GameLoopBooster.Core.Processes;

/// <summary>Finds where GameLoop is installed. Read-only: GameLoop's files are never touched.</summary>
public static class GameLoopLocator
{
    /// <summary>The emulator engine and UI shells: these do the rendering work.</summary>
    public static readonly string[] GameExecutables =
    {
        "aow_exe.exe", "AndroidEmulatorEx.exe", "AndroidEmulatorEn.exe", "AndroidEmulator.exe",
    };

    public static string? FindInstallDir(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath)) return overridePath;
        return FromRunningProcess() ?? FromUninstallKeys() ?? FromDefaultFolders();
    }

    public static List<string> FindExecutables(string? installDir)
    {
        var found = new List<string>();
        if (installDir is null || !Directory.Exists(installDir)) return found;
        foreach (var name in GameExecutables)
        {
            try
            {
                var hit = Directory.EnumerateFiles(installDir, name, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 3,
                    IgnoreInaccessible = true,
                }).FirstOrDefault();
                if (hit is not null) found.Add(hit);
            }
            catch { /* inaccessible folder */ }
        }
        return found;
    }

    public static string? GetProcessPath(int pid)
    {
        var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return NativeMethods.QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { NativeMethods.CloseHandle(h); }
    }

    private static string? FromRunningProcess()
    {
        foreach (var name in GameExecutables.Append("AppMarket.exe"))
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
            {
                using (p)
                {
                    var path = GetProcessPath(p.Id);
                    if (path is null) continue;
                    // ...\TxGameAssistant\ui\AndroidEmulatorEx.exe -> ...\TxGameAssistant
                    var dir = Path.GetDirectoryName(path);
                    var parent = dir is null ? null : Path.GetDirectoryName(dir);
                    return parent is not null && Directory.Exists(parent) ? parent : dir;
                }
            }
        }
        return null;
    }

    private static string? FromUninstallKeys()
    {
        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            foreach (var root in roots)
            {
                using var key = baseKey.OpenSubKey(root);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(sub);
                    var name = app?.GetValue("DisplayName") as string ?? "";
                    if (!name.Contains("GameLoop", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("Tencent Gaming Buddy", StringComparison.OrdinalIgnoreCase)) continue;

                    var loc = (app?.GetValue("InstallLocation") as string)?.Trim('"', ' ');
                    if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) return loc;

                    var icon = (app?.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"', ' ');
                    var dir = string.IsNullOrEmpty(icon) ? null : Path.GetDirectoryName(icon);
                    if (dir is not null && Directory.Exists(dir))
                        return Path.GetFileName(dir).Equals("ui", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dir) : dir;
                }
            }
        }
        return null;
    }

    private static string? FromDefaultFolders()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            foreach (var pf in new[] { "Program Files", "Program Files (x86)", "" })
            {
                var candidate = Path.Combine(drive.RootDirectory.FullName, pf, "TxGameAssistant");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
