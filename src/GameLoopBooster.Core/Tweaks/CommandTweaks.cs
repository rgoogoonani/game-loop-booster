using GameLoopBooster.Core.Backup;
using GameLoopBooster.Core.Util;

namespace GameLoopBooster.Core.Tweaks;

/// <summary>A setting changed through a Windows tool rather than a single registry value.</summary>
public sealed class CommandTweak : Tweak
{
    public required string CommandLocation { get; init; }
    public required Func<string?> Read { get; init; }
    public required Action<string> Write { get; init; }

    /// <summary>Called with null when the setting was not present originally.</summary>
    public required Action<string?> Revert { get; init; }

    public override string Location => CommandLocation;
    public override string? ReadCurrent() => Read();

    public override BackupEntry Capture()
    {
        var v = Read();
        return new BackupEntry { TweakId = Id, Title = Title, Kind = BackupKind.Command, Existed = v is not null, Value = v };
    }

    public override void Apply(string target) => Write(target.Trim().ToLowerInvariant());

    /// <summary>Command tweaks are not hardware-dependent, so restore can always find them by id.</summary>
    public static readonly Dictionary<string, CommandTweak> Known = new();

    public static CommandTweak Register(CommandTweak t)
    {
        Known[t.Id] = t;
        return t;
    }

    public static void Restore(BackupEntry e)
    {
        if (!Known.TryGetValue(e.TweakId, out var t))
            throw new InvalidOperationException($"Unknown command tweak {e.TweakId}");
        t.Revert(e.Existed ? e.Value : null);
    }
}

public static class CommandTweaks
{
    public static readonly CommandTweak MemoryCompression = CommandTweak.Register(new CommandTweak
    {
        Id = "mem.compression",
        Title = "غیرفعال‌سازی فشرده‌سازی حافظه (Memory Compression)",
        Description = "ویندوز صفحات کم‌استفاده RAM را فشرده می‌کند که روی CPU هزینه دارد و می‌تواند باعث مکث‌های کوتاه شود. با RAM زیاد (۱۶ گیگ و بیشتر) فشرده‌سازی تقریباً لازم نیست.",
        Category = TweakCategory.Memory,
        RequiresReboot = true,
        RecommendedValue = "off",
        ValueHint = "on / off",
        CommandLocation = "PowerShell: Disable-MMAgent -MemoryCompression",
        Recommend = hw => hw.RamGb >= 16 ? TweakLevel.Aggressive : null,
        Read = () =>
        {
            var r = Shell.PowerShell("(Get-MMAgent).MemoryCompression");
            var o = r.Output.Trim();
            return o.Equals("True", StringComparison.OrdinalIgnoreCase) ? "on"
                : o.Equals("False", StringComparison.OrdinalIgnoreCase) ? "off" : null;
        },
        Write = v =>
        {
            var r = Shell.PowerShell(v == "off" ? "Disable-MMAgent -MemoryCompression" : "Enable-MMAgent -MemoryCompression");
            if (!r.Success) throw new InvalidOperationException(r.Error.Trim());
        },
        Revert = v =>
        {
            if (v is null) return;
            Shell.PowerShell(v == "off" ? "Disable-MMAgent -MemoryCompression" : "Enable-MMAgent -MemoryCompression");
        },
    });

    private static string Bcd => Shell.SystemTool("bcdedit.exe");

    private static string? ReadBcd(string name)
    {
        var r = Shell.Run(Bcd, "/enum {current}");
        foreach (var line in r.Output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
                return t[name.Length..].Trim().ToLowerInvariant();
        }
        return null;
    }

    public static readonly CommandTweak DisableDynamicTick = CommandTweak.Register(new CommandTweak
    {
        Id = "timer.dynamictick",
        Title = "غیرفعال‌سازی Dynamic Tick (bcdedit)",
        Description = "تیک تایمر کرنل را ثابت نگه می‌دارد تا در حالت idle خاموش نشود. روی بعضی سیستم‌ها Jitter را کم می‌کند، روی بعضی اثری ندارد. فقط با تست Jitter قبل/بعد تصمیم بگیرید. نیاز به ری‌استارت دارد.",
        Category = TweakCategory.Timer,
        RequiresReboot = true,
        RecommendedValue = "yes",
        ValueHint = "yes / no",
        CommandLocation = "bcdedit /set {current} disabledynamictick yes",
        Recommend = _ => TweakLevel.Expert,
        Read = () => ReadBcd("disabledynamictick"),
        Write = v =>
        {
            var r = Shell.Run(Bcd, $"/set {{current}} disabledynamictick {(v == "yes" ? "yes" : "no")}");
            if (!r.Success) throw new InvalidOperationException((r.Output + r.Error).Trim());
        },
        Revert = v => Shell.Run(Bcd, v is null
            ? "/deletevalue {current} disabledynamictick"
            : $"/set {{current}} disabledynamictick {v}"),
    });
}
