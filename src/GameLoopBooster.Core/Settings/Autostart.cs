using GameLoopBooster.Core.Util;

namespace GameLoopBooster.Core.Settings;

/// <summary>
/// Starts the app at logon through Task Scheduler with highest privileges, because the app needs admin
/// rights and a Run-key entry would trigger a UAC prompt on every boot.
/// </summary>
public static class Autostart
{
    private const string TaskName = "GameLoopBooster";

    public static bool IsEnabled() =>
        Shell.Run(Shell.SystemTool("schtasks.exe"), $"/query /tn {TaskName}").Success;

    public static bool Set(bool enable, string exePath)
    {
        var schtasks = Shell.SystemTool("schtasks.exe");
        var r = enable
            ? Shell.Run(schtasks, $"/create /f /tn {TaskName} /sc onlogon /rl highest /tr \"\\\"{exePath}\\\" --tray\"")
            : Shell.Run(schtasks, $"/delete /f /tn {TaskName}");
        if (!r.Success) Log.Error($"Autostart: {(r.Error + r.Output).Trim()}");
        return r.Success;
    }
}
