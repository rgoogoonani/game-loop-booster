using System.Management;
using GameLoopBooster.Core.Hardware;
using GameLoopBooster.Core.Tweaks;
using GameLoopBooster.Core.Util;
using Microsoft.Win32;

namespace GameLoopBooster.Core.Diagnostics;

public enum CheckState { Good, Warning, Bad, Info }

public sealed record SystemCheck(string Title, string Value, CheckState State, string Advice);

/// <summary>Read-only health checks of things that commonly cause stutter in Android emulators.</summary>
public static class SystemChecks
{
    public static List<SystemCheck> Run(HardwareInfo hw)
    {
        var list = new List<SystemCheck>();
        Add(list, Vbs);
        Add(list, HyperV);
        Add(list, () => Timer());
        Add(list, PowerPlan);
        Add(list, () => Ram(hw));
        Add(list, GameMode);
        Add(list, Hags);
        Add(list, () => Gpu(hw));
        return list;
    }

    private static void Add(List<SystemCheck> list, Func<SystemCheck?> check)
    {
        try
        {
            var c = check();
            if (c is not null) list.Add(c);
        }
        catch (Exception ex)
        {
            Log.Warn($"بررسی سیستم: {ex.Message}");
        }
    }

    private static SystemCheck Vbs()
    {
        using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard",
            "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard");
        foreach (var mo in s.Get())
        {
            var status = Convert.ToInt32(mo["VirtualizationBasedSecurityStatus"] ?? 0);
            var running = (mo["SecurityServicesRunning"] as uint[]) ?? Array.Empty<uint>();
            var hvci = running.Contains(2u);
            if (status == 2)
                return new SystemCheck("VBS / Memory Integrity", hvci ? "روشن (HVCI فعال)" : "VBS روشن", CheckState.Bad,
                    "VBS هنوز در حال اجراست. از Windows Security > Device Security > Core isolation خاموش کنید و ری‌استارت کنید.");
            return new SystemCheck("VBS / Memory Integrity", "خاموش", CheckState.Good, "");
        }
        return new SystemCheck("VBS / Memory Integrity", "نامشخص", CheckState.Info, "");
    }

    private static SystemCheck HyperV()
    {
        var r = Shell.Run(Shell.SystemTool("bcdedit.exe"), "/enum {current}");
        var line = r.Output.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("hypervisorlaunchtype", StringComparison.OrdinalIgnoreCase));
        var value = line?.Trim()["hypervisorlaunchtype".Length..].Trim() ?? "Off";
        var on = value.Equals("Auto", StringComparison.OrdinalIgnoreCase);
        return new SystemCheck("Hypervisor (Hyper-V)", on ? "فعال" : "غیرفعال",
            on ? CheckState.Warning : CheckState.Good,
            on ? "GameLoop به Hyper-V نیاز ندارد. اگر از WSL2/Docker/Sandbox استفاده نمی‌کنید، خاموش کردن آن (bcdedit /set hypervisorlaunchtype off) لایه مجازی‌سازی اضافه را حذف می‌کند." : "");
    }

    private static SystemCheck Timer()
    {
        var (_, finest, current) = TimerResolution.Query();
        var good = current <= 1.0;
        return new SystemCheck("دقت تایمر ویندوز", $"{current:0.###} ms (حداقل ممکن {finest:0.###} ms)",
            good ? CheckState.Good : CheckState.Warning,
            good ? "" : "وقتی GameLoop اجرا شود، برنامه تایمر را خودکار روی 0.5ms می‌گذارد (اگر برنامه باز باشد).");
    }

    private static SystemCheck PowerPlan()
    {
        var guid = PowerCfg.ActiveScheme();
        var name = PowerCfg.SchemeName(guid);
        var good = guid is PowerCfg.HighPerformance or PowerCfg.UltimatePerformance || name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
        return new SystemCheck("پاورپلن فعال", name, good ? CheckState.Good : CheckState.Warning,
            good ? "" : "پاورپلن Balanced با Core Parking و EPP بالا عامل رایج میکرو‌استاتر است. پروفایل را اعمال کنید.");
    }

    private static SystemCheck? Ram(HardwareInfo hw)
    {
        if (hw.RamSpeedMts == 0) return null;
        var value = $"{hw.RamGb:0} GB · {hw.RamSpeedMts} MT/s · {hw.RamModules} ماژول";
        if (hw.RamRatedSpeedMts > 0 && hw.RamSpeedMts < hw.RamRatedSpeedMts * 0.9)
            return new SystemCheck("رم", value, CheckState.Warning, "رم پایین‌تر از سرعت مجاز کار می‌کند؛ XMP/EXPO را در BIOS بررسی کنید.");
        if (hw.RamModules == 1)
            return new SystemCheck("رم", value, CheckState.Warning, "فقط یک ماژول رم (Single Channel) پهنای باند را نصف می‌کند.");
        return new SystemCheck("رم", value, CheckState.Good, "");
    }

    private static SystemCheck GameMode()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
        var v = k?.GetValue("AutoGameModeEnabled");
        var on = v is null || Convert.ToInt32(v) == 1;
        return new SystemCheck("Game Mode", on ? "روشن" : "خاموش", on ? CheckState.Good : CheckState.Warning, on ? "" : "Game Mode را روشن کنید.");
    }

    private static SystemCheck Hags()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
        var v = k?.GetValue("HwSchMode");
        var text = v is null ? "پیش‌فرض" : Convert.ToInt32(v) == 2 ? "روشن" : "خاموش";
        return new SystemCheck("HAGS", text, CheckState.Info, "اثرش روی امولاتور متغیر است؛ هر دو حالت را تست کنید.");
    }

    private static SystemCheck? Gpu(HardwareInfo hw)
    {
        var g = hw.PrimaryGpu;
        if (g is null) return null;
        var advice = hw.HasIntegratedAndDedicated
            ? "گرافیک داخلی هم فعال است؛ تنظیم GPU Preference را اعمال کنید تا امولاتور روی کارت اصلی اجرا شود."
            : "";
        return new SystemCheck("کارت گرافیک", $"{g.Name} · درایور {g.DriverVersion}",
            hw.HasIntegratedAndDedicated ? CheckState.Warning : CheckState.Good, advice);
    }
}
