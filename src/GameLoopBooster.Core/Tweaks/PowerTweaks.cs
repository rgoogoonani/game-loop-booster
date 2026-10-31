using System.Globalization;
using System.Text.RegularExpressions;
using GameLoopBooster.Core.Backup;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.Core.Tweaks;

public static partial class PowerCfg
{
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})")]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"\(([^)]*)\)\s*\*?\s*$")]
    private static partial Regex NameRegex();

    private static string Exe => Shell.SystemTool("powercfg.exe");

    public static ShellResult Run(string args) => Shell.Run(Exe, args);

    public static string ActiveScheme()
    {
        var r = Run("/getactivescheme");
        var m = GuidRegex().Match(r.Output);
        return m.Success ? m.Value.ToLowerInvariant() : throw new InvalidOperationException("powercfg /getactivescheme failed");
    }

    /// <summary>All schemes as (guid, name). Names come from powercfg so they are localized.</summary>
    public static List<(string Guid, string Name)> ListSchemes()
    {
        var list = new List<(string, string)>();
        foreach (var line in Run("/list").Output.Split('\n'))
        {
            var g = GuidRegex().Match(line);
            if (!g.Success) continue;
            var n = NameRegex().Match(line.Trim());
            list.Add((g.Value.ToLowerInvariant(), n.Success ? n.Groups[1].Value : g.Value));
        }
        return list;
    }

    public static string SchemeName(string guid) =>
        ListSchemes().FirstOrDefault(s => s.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase)).Name ?? guid;

    /// <summary>Reads the AC index. powercfg output is localized, so instead of labels we take the
    /// second-to-last hex number (AC) — the last one is the DC index.</summary>
    public static uint? ReadAc(string scheme, string sub, string setting)
    {
        var r = Run($"/q {scheme} {sub} {setting}");
        if (!r.Success) return null;
        var matches = HexRegex().Matches(r.Output);
        if (matches.Count < 2) return null;
        return uint.Parse(matches[^2].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static void WriteAc(string scheme, string sub, string setting, uint value)
    {
        var r = Run($"/setacvalueindex {scheme} {sub} {setting} {value}");
        if (!r.Success) throw new InvalidOperationException($"powercfg: {(r.Error + r.Output).Trim()}");
        if (scheme.Equals(ActiveScheme(), StringComparison.OrdinalIgnoreCase))
            Run($"/setactive {scheme}");
    }

    public static void SetActive(string scheme)
    {
        var r = Run($"/setactive {scheme}");
        if (!r.Success) throw new InvalidOperationException($"powercfg: {(r.Error + r.Output).Trim()}");
    }

    /// <summary>Makes sure the Ultimate Performance plan exists (it is hidden by default) and returns its GUID.</summary>
    public static string EnsureUltimate()
    {
        var existing = ListSchemes().FirstOrDefault(s => s.Guid == UltimatePerformance);
        if (existing.Guid is not null) return existing.Guid;

        // A previous duplicate keeps the name of the template; reuse it instead of creating another copy.
        var ultimateName = ListSchemes().FirstOrDefault(s => s.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
        if (ultimateName.Guid is not null) return ultimateName.Guid;

        var r = Run($"-duplicatescheme {UltimatePerformance}");
        var m = GuidRegex().Match(r.Output);
        if (r.Success && m.Success) return m.Value.ToLowerInvariant();

        // Some editions (e.g. with Modern Standby) refuse it; fall back to High performance.
        return HighPerformance;
    }
}

/// <summary>A processor/PCIe/USB setting of a power plan, changed with powercfg on the active plan.</summary>
public sealed class PowerSettingTweak : Tweak
{
    public required string SubGroup { get; init; }
    public required string Setting { get; init; }
    public string SettingAlias { get; init; } = "";

    public override string Location => $"powercfg  {SettingAlias}  ({SubGroup} / {Setting})";

    public override string? ReadCurrent() =>
        PowerCfg.ReadAc(PowerCfg.ActiveScheme(), SubGroup, Setting)?.ToString(CultureInfo.InvariantCulture);

    public override BackupEntry Capture()
    {
        var scheme = PowerCfg.ActiveScheme();
        var value = PowerCfg.ReadAc(scheme, SubGroup, Setting);
        return new BackupEntry
        {
            TweakId = Id,
            Title = Title,
            Kind = BackupKind.PowerSetting,
            Existed = value is not null,
            Value = value?.ToString(CultureInfo.InvariantCulture),
            SchemeGuid = scheme,
            SubGroupGuid = SubGroup,
            SettingGuid = Setting,
        };
    }

    public override void Apply(string target)
    {
        if (!uint.TryParse(target.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"'{target}' is not a number");
        if (PowerCfg.ReadAc(PowerCfg.ActiveScheme(), SubGroup, Setting) is null)
            throw new NotSupportedException("این تنظیم روی این سیستم وجود ندارد");
        PowerCfg.WriteAc(PowerCfg.ActiveScheme(), SubGroup, Setting, v);
    }

    public static void Restore(BackupEntry e)
    {
        if (!e.Existed || e.Value is null) return;
        PowerCfg.WriteAc(e.SchemeGuid!, e.SubGroupGuid!, e.SettingGuid!, uint.Parse(e.Value, CultureInfo.InvariantCulture));
    }
}

/// <summary>Switches the active power plan. Target is "ultimate", "high", or a plan GUID.</summary>
public sealed class PowerPlanTweak : Tweak
{
    public override string Location => "powercfg /setactive";

    public override string? ReadCurrent() => PowerCfg.ActiveScheme();

    public override string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "high" => PowerCfg.HighPerformance,
        "ultimate" => ResolveUltimateIfPresent(),
        var v => v,
    };

    private static string ResolveUltimateIfPresent()
    {
        var s = PowerCfg.ListSchemes();
        return s.FirstOrDefault(x => x.Guid == PowerCfg.UltimatePerformance).Guid
            ?? s.FirstOrDefault(x => x.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)).Guid
            ?? "ultimate";
    }

    public override string Display(string? value) =>
        value is null ? base.Display(value) : value.Trim().ToLowerInvariant() switch
        {
            "ultimate" => "Ultimate Performance",
            "high" => "High Performance",
            var g => $"{PowerCfg.SchemeName(g)}",
        };

    public override BackupEntry Capture() => new()
    {
        TweakId = Id,
        Title = Title,
        Kind = BackupKind.PowerPlan,
        Existed = true,
        Value = PowerCfg.ActiveScheme(),
    };

    public override void Apply(string target)
    {
        var guid = target.Trim().ToLowerInvariant() switch
        {
            "ultimate" => PowerCfg.EnsureUltimate(),
            "high" => PowerCfg.HighPerformance,
            var g => g,
        };
        PowerCfg.SetActive(guid);
    }

    public static void Restore(BackupEntry e)
    {
        if (e.Value is not null) PowerCfg.SetActive(e.Value);
    }
}
