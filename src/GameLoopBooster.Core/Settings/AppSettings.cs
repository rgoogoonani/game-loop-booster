using GameLoopBooster.Core.Processes;
using GameLoopBooster.Core.Tweaks;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.Core.Settings;

public sealed class AppSettings
{
    public TweakLevel ProfileLevel { get; set; } = TweakLevel.Safe;

    /// <summary>Per-tweak on/off chosen in advanced mode. Missing = follow the profile.</summary>
    public Dictionary<string, bool> TweakSelection { get; set; } = new();

    /// <summary>Per-tweak value typed in advanced mode. Missing = recommended value.</summary>
    public Dictionary<string, string> TweakValues { get; set; } = new();

    public List<ProcessRule> ProcessRules { get; set; } = ProcessRule.Defaults();

    public bool AutoOptimizeProcesses { get; set; } = true;
    public bool HighResolutionTimer { get; set; } = true;
    public double TimerResolutionMs { get; set; } = 0.5;
    public bool SessionPowerPlan { get; set; }
    public string? GameLoopPathOverride { get; set; }

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AdvancedMode { get; set; }

    public static AppSettings Load()
    {
        try { return Json.Load<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings(); }
        catch (Exception ex)
        {
            Log.Warn($"تنظیمات خوانده نشد، پیش‌فرض استفاده شد: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save()
    {
        try { Json.Save(AppPaths.SettingsFile, this); }
        catch (Exception ex) { Log.Error($"ذخیره تنظیمات ناموفق: {ex.Message}"); }
    }
}
